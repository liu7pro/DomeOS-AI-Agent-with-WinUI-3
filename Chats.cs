using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace DomeOSAgentDev
{
    /// <summary>One message inside a stored conversation.</summary>
    public sealed class ChatMessage
    {
        /// <summary>user or assistant.</summary>
        public string Role { get; set; } = "user";

        public string Text { get; set; } = "";

        public string Time { get; set; } = "";
    }

    /// <summary>
    /// A conversation, as the gallery shows it: a title, a snippet and its messages. The three
    /// read-only members below are the ones the tile binds to, which keeps the tile template free of
    /// value converters.
    /// </summary>
    public sealed class Conversation
    {
        public string Id { get; set; } = "";

        public string Title { get; set; } = "";

        public bool Favorite { get; set; }

        public string Created { get; set; } = "";

        public string Updated { get; set; } = "";

        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        /// <summary>The newest message flattened to one line, for the tile.</summary>
        public string Preview
        {
            get
            {
                for (int i = Messages.Count - 1; i >= 0; i--)
                {
                    string text = (Messages[i].Text ?? string.Empty)
                        .Replace("\r", " ").Replace("\n", " ").Trim();

                    if (text.Length > 0)
                    {
                        return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
                    }
                }

                return "（空对话）";
            }
        }

        public string When => ChatStore.RelativeTime(Updated);

        public string Count => Messages.Count + " 条";

        public string Badge => Favorite ? "★ 收藏" : string.Empty;
    }

    /// <summary>
    /// Where the conversations live. Deliberately its own file next to the profile, so the chat
    /// archive is not rebuilt from scratch on every settings save.
    /// </summary>
    internal static class ChatStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public static List<Conversation> Items { get; private set; } = new List<Conversation>();

        /// <summary>存档文件在、但这次没读出来。此时不要拿空列表去覆盖它。</summary>
        public static bool LoadFailed { get; private set; }

        public static string FilePath => System.IO.Path.Combine(SettingsStore.DataDir, "chats.json");

        /// <summary>同时有好几处保存时，别让两个写操作抢同一个临时文件。</summary>
        private static readonly object SaveGate = new object();

        public static void Load()
        {
            bool damaged = false;

            for (int attempt = 0; attempt < 3 && !damaged; attempt++)
            {
                try
                {
                    if (!System.IO.File.Exists(FilePath))
                    {
                        return;
                    }

                    string json = System.IO.File.ReadAllText(FilePath);
                    List<Conversation>? loaded = JsonSerializer.Deserialize<List<Conversation>>(json, Options);
                    if (loaded != null)
                    {
                        Items = loaded;
                        break;
                    }

                    damaged = true;
                }
                catch
                {
                    // 可能只是别的进程正占着文件，稍等再试一次
                    damaged = true;
                    System.Threading.Thread.Sleep(120);
                }
            }

            if (Items.Count == 0 && System.IO.File.Exists(FilePath) && damaged)
            {
                LoadFailed = true;

                // 真读坏了：把原文件留一份，别让下一次保存把它抹掉
                try
                {
                    string backup = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    System.IO.File.Copy(FilePath, backup, true);
                }
                catch
                {
                    // 备份都写不了就只能继续
                }
            }

            foreach (Conversation conversation in Items)
            {
                if (conversation.Messages == null)
                {
                    conversation.Messages = new List<ChatMessage>();
                }
            }

            Sort();
        }

        public static void Save()
        {
            lock (SaveGate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(SettingsStore.DataDir);
                    string tmp = FilePath + ".tmp";
                    System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(Items, Options));

                    // 上一份完好的存档留个底，下一次真写坏了还能捞回来
                    try
                    {
                        if (System.IO.File.Exists(FilePath))
                        {
                            System.IO.File.Copy(FilePath, FilePath + ".bak", true);
                        }
                    }
                    catch
                    {
                        // 备份失败不影响这次保存
                    }

                    System.IO.File.Move(tmp, FilePath, true);
                }
                catch
                {
                    // a read-only profile must not take the app down
                }
            }
        }

        /// <summary>Newest first, which is the order the gallery reads in.</summary>
        public static void Sort()
        {
            Items.Sort((left, right) => string.CompareOrdinal(right.Updated, left.Updated));
        }

        public static Conversation Create(string title)
        {
            Conversation conversation = new Conversation
            {
                Id = Guid.NewGuid().ToString("n"),
                Title = title,
                Created = Now(),
                Updated = Now(),
            };

            Items.Insert(0, conversation);
            Save();
            return conversation;
        }

        public static void Remove(Conversation conversation)
        {
            Items.Remove(conversation);
            Save();
        }

        public static string Now()
        {
            return DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
        }

        public static string RelativeTime(string stamp)
        {
            if (!DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when))
            {
                return string.Empty;
            }

            DateTime now = DateTime.Now;
            TimeSpan age = now - when;

            if (age.TotalMinutes < 1)
            {
                return "刚刚";
            }

            if (age.TotalMinutes < 60)
            {
                return (int)age.TotalMinutes + " 分钟前";
            }

            if (when.Date == now.Date)
            {
                return when.ToString("HH:mm");
            }

            if (when.Date == now.Date.AddDays(-1))
            {
                return "昨天 " + when.ToString("HH:mm");
            }

            if (age.TotalDays < 7)
            {
                return (int)age.TotalDays + " 天前";
            }

            return when.ToString("yyyy-MM-dd");
        }

        /// <summary>A title that fits the tile, taken from whatever the user first said.</summary>
        public static string TitleFrom(string text)
        {
            string flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  "))
            {
                flat = flat.Replace("  ", " ");
            }

            if (flat.Length == 0)
            {
                return "新的对话";
            }

            return flat.Length > 24 ? flat.Substring(0, 24) + "…" : flat;
        }
    }
}
