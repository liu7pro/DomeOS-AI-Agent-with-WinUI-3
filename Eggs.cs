using System;
using System.IO;
using System.Threading.Tasks;

namespace DomeOSAgentDev
{
    /// <summary>Which easter egg, if any, a piece of composer text triggers.</summary>
    internal enum EggKind
    {
        None,
        OnePlus,
        Ysjf,
        SixtySeven,
        FireflyOs,
        Maybe,
        Mermaid,
        Moonlight,
        Midnight,
        Regret,
        JustDoIt,
    }

    /// <summary>
    /// The hidden inputs. The rules are taken from the original app's egg table: the text is trimmed,
    /// then matched against the table exactly, falling back to a case-insensitive match. Eggs never go
    /// through the model and never need an API key.
    /// </summary>
    internal static class Eggs
    {
        public const string OnePlusUrl = "https://www.oneplus.com/global/brand";
        public const string YsjfUrl = "https://www.ysjf.com/index";
        public const string FireflyOsUrl = "https://fireflyos.choling.cyou";

        /// <summary>What 「67」 answers, verbatim.</summary>
        public const string SixtySevenText = "676767676767676767676767";

        public const string MaybeFileName = "maybe.flac";

        /// <summary>「人鱼的眼泪」放的那首。</summary>
        public const string MermaidFileName = "mermaid.flac";

        /// <summary>「朝为田舍埋头郎」放的那首，文件叫 moonlight.flac。</summary>
        public const string MoonlightFileName = "moonlight.flac";

        /// <summary>「Rockstar Games」放的那首 Midnight City。</summary>
        public const string MidnightFileName = "midnight.flac";

        /// <summary>「拼接遗憾」放的那首。同理，磁盘上叫 Regret.flac。</summary>
        public const string RegretFileName = "Regret.flac";

        /// <summary>「Just do it」给的那张图。浅色模式下用白底黑钩，深色模式下用黑底白钩。</summary>
        public const string JustDoItLightFileName = "nike-light.jpg";

        public const string JustDoItDarkFileName = "nike-dark.jpg";

        public static EggKind Match(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return EggKind.None;
            }

            // exact keys: "1+", "67", 无限进步, 無限進步
            if (value == "1+")
            {
                return EggKind.OnePlus;
            }

            if (value == "67")
            {
                return EggKind.SixtySeven;
            }

            if (value == "无限进步" || value == "無限進步")
            {
                return EggKind.Ysjf;
            }

            if (value == "人鱼的眼泪" || value == "人魚的眼淚")
            {
                return EggKind.Mermaid;
            }

            if (value == "朝为田舍埋头郎" || value == "朝為田舍埋頭郎")
            {
                return EggKind.Moonlight;
            }

            if (value == "拼接遗憾" || value == "拼接遺憾")
            {
                return EggKind.Regret;
            }

            // case-insensitive keys: fireflyos, maybe
            string lower = value.ToLowerInvariant();
            if (lower == "fireflyos")
            {
                return EggKind.FireflyOs;
            }

            if (lower == "maybe")
            {
                return EggKind.Maybe;
            }

            if (lower == "rockstar games")
            {
                return EggKind.Midnight;
            }

            // 「Just do it」——句末的标点算口语的一部分，去掉再比
            string key = lower.TrimEnd('.', '!', '。', '！', ' ');
            if (key == "just do it" || key == "justdoit")
            {
                return EggKind.JustDoIt;
            }

            return EggKind.None;
        }

        /// <summary>Hands a link to the default browser.</summary>
        public static async Task<bool> OpenUrlAsync(string url)
        {
            try
            {
                return await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 打开播放器并把这首歌交给它，同时把播放器进程带回去，彩蛋靠它判断「窗口是不是关了」。
        /// Soto Player 没装就退回系统默认播放器（那条路拿不到进程）。
        /// </summary>
        public static async Task<(bool Ok, System.Diagnostics.Process? Player)> StartBundledPlayerAsync(string fileName)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (!File.Exists(path))
            {
                return (false, null);
            }

            string? player = FindSotoPlayer();
            if (player != null)
            {
                try
                {
                    System.Diagnostics.Process? process = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = player,
                            Arguments = "\"" + path + "\"",
                            UseShellExecute = false,
                        });

                    if (process != null)
                    {
                        return (true, process);
                    }
                }
                catch
                {
                    // 起不来就退回系统默认播放器
                }
            }

            return (await OpenBundledFileAsync(fileName), null);
        }

        /// <summary>
        /// Hands a bundled track to a real player window. Soto Player（水芸音乐播放器）装了就用它，
        /// 没装就交给系统默认的播放器——都是「弹出一个播放器窗口」，不在本程序里出声。
        /// </summary>
        public static async Task<bool> PlayBundledAsync(string fileName)
        {
            (bool ok, _) = await StartBundledPlayerAsync(fileName);
            return ok;
        }

        /// <summary>找 Soto Player 的主程序；找不到就返回 null。</summary>
        private static string? FindSotoPlayer()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] roots =
            {
                Path.Combine(local, "Programs"),
                programFiles,
                programFilesX86,
            };

            string[] patterns = { "*soto*", "*水芸*" };

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }

                foreach (string pattern in patterns)
                {
                    try
                    {
                        foreach (string folder in Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly))
                        {
                            foreach (string exe in Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly))
                            {
                                string name = Path.GetFileName(exe);
                                if (name.Contains("unins", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                return exe;
                            }
                        }
                    }
                    catch
                    {
                        // 没权限看就算了，继续找下一处
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Hands a bundled file to whatever program owns it - for the music egg that means the
        /// system's media player opens in its own window rather than anything playing inside the app.
        /// </summary>
        public static async Task<bool> OpenBundledFileAsync(string fileName)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
                if (!File.Exists(path))
                {
                    return false;
                }

                Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                return await Windows.System.Launcher.LaunchFileAsync(file);
            }
            catch
            {
                return false;
            }
        }
    }
}
