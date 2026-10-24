using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DomeOSAgentDev
{
    /// <summary>Agent 手里的一件工具：名字、说明、参数表和它是否危险。</summary>
    internal sealed class ToolSpec
    {
        public string Name { get; set; } = "";

        public string Title { get; set; } = "";

        public string Description { get; set; } = "";

        /// <summary>危险工具在动手前要过一遍审批（除非开了自动批准）。</summary>
        public bool Dangerous { get; set; }

        public JsonObject Parameters { get; set; } = new JsonObject();
    }

    /// <summary>一次工具调用所看到的运行环境。</summary>
    internal sealed class ToolContext
    {
        public string Workspace { get; set; } = "";

        /// <summary>workspace = 关在工作区里；machine = 整台电脑都能碰。</summary>
        public string Scope { get; set; } = "workspace";

        public int MaxOutput { get; set; } = 20000;

        public int ShellTimeoutMs { get; set; } = 90000;

        public bool AiController { get; set; }

        /// <summary>有没有工作区。没有的话文件类工具不该被调用——空路径会悄悄指到当前目录去。</summary>
        public bool HasWorkspace => !string.IsNullOrWhiteSpace(Workspace);
    }

    internal sealed class ToolOutcome
    {
        public bool Ok { get; set; }

        public string Summary { get; set; } = "";

        public string Output { get; set; } = "";

        /// <summary>喂回给模型的那段话。</summary>
        public string Text => Output.Length > 0 ? Summary + "\n\n" + Output : Summary;
    }

    internal sealed class PathJailException : Exception
    {
        public PathJailException(string message)
            : base(message)
        {
        }
    }

    internal static class Toolbox
    {
        private const int MaxReadBytes = 400_000;

        private const int MaxWriteBytes = 4_000_000;

        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", ".svn", ".hg", "dist", "build", "out", "release",
            ".next", ".nuxt", ".cache", "__pycache__", ".venv", "venv", ".idea", ".vscode",
            "target", "bin", "obj", ".gradle", ".dart_tool", "coverage",
        };

        private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svgz", ".pdf", ".zip",
            ".7z", ".rar", ".gz", ".tar", ".exe", ".dll", ".so", ".dylib", ".bin", ".dat",
            ".mp3", ".mp4", ".avi", ".mkv", ".wav", ".flac", ".woff", ".woff2", ".ttf", ".otf",
            ".class", ".jar", ".pyc", ".o", ".obj", ".lib", ".pdb", ".sqlite", ".db",
        };

        // ------------------------------------------------------------------ 参数表

        private sealed class Params
        {
            private readonly JsonObject _properties = new JsonObject();

            private readonly JsonArray _required = new JsonArray();

            public Params Add(string name, string type, string description, bool required = false)
            {
                _properties[name] = new JsonObject
                {
                    ["type"] = type,
                    ["description"] = description,
                };

                if (required)
                {
                    _required.Add(name);
                }

                return this;
            }

            public Params Choice(string name, string description, string[] values, bool required = false)
            {
                JsonArray options = new JsonArray();
                foreach (string value in values)
                {
                    options.Add(value);
                }

                _properties[name] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = description,
                    ["enum"] = options,
                };

                if (required)
                {
                    _required.Add(name);
                }

                return this;
            }

            public JsonObject Build()
            {
                JsonObject schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = _properties,
                };

                if (_required.Count > 0)
                {
                    schema["required"] = _required;
                }

                return schema;
            }
        }

        /// <summary>
        /// 这一轮能用的工具。说明文字跟着权限走：整机权限时模型才知道自己可以走绝对路径。
        /// 没有工作区时文件 / 命令类的工具一律不挂——纯聊天不该被迫先挑文件夹。
        /// </summary>
        public static List<ToolSpec> Available(string scope, bool web, bool aiController, bool hasWorkspace)
        {
            bool machine = scope == "machine";
            string file = machine ? "这台电脑上的任意绝对路径" : "相对工作区的路径";
            string folder = machine ? "这台电脑上的任意绝对目录" : "相对工作区的目录";
            string folderOptional = folder + "，可省略";

            List<ToolSpec> specs = new List<ToolSpec>
            {
                new ToolSpec
                {
                    Name = "list_dir",
                    Title = "列出目录",
                    Description = "列出工作区内某个路径下的文件与子目录。",
                    Parameters = new Params()
                        .Add("path", "string", folder + "，默认工作区根目录")
                        .Add("depth", "integer", "递归层数 1-4，默认 1")
                        .Add("limit", "integer", "最多返回多少条，默认 400")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "read_file",
                    Title = "读取文件",
                    Description = "读取一个文本文件，输出带行号，方便精确修改。",
                    Parameters = new Params()
                        .Add("path", "string", file, true)
                        .Add("start", "integer", "起始行，从 1 开始，可省略")
                        .Add("lines", "integer", "读多少行，可省略")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "write_file",
                    Title = "写入文件",
                    Description = "新建一个文件，或者整体覆盖它。上级目录会自动创建。",
                    Dangerous = true,
                    Parameters = new Params()
                        .Add("path", "string", file, true)
                        .Add("content", "string", "完整的新内容", true)
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "replace_in_file",
                    Title = "修改文件",
                    Description = "把文件里的一段原文替换掉。找不到或多处命中时会失败。",
                    Dangerous = true,
                    Parameters = new Params()
                        .Add("path", "string", file, true)
                        .Add("find", "string", "要被替换的原文", true)
                        .Add("replace", "string", "替换成什么", true)
                        .Add("all", "boolean", "是否替换全部命中，默认否")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "make_dir",
                    Title = "创建目录",
                    Description = "创建目录，缺的上级目录一起建。",
                    Dangerous = true,
                    Parameters = new Params().Add("path", "string", folder, true).Build(),
                },
                new ToolSpec
                {
                    Name = "move_path",
                    Title = "移动 / 改名",
                    Description = "在工作区内移动或重命名一个文件或目录。",
                    Dangerous = true,
                    Parameters = new Params()
                        .Add("from", "string", "原路径", true)
                        .Add("to", "string", "目标路径", true)
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "delete_path",
                    Title = "删除",
                    Description = "删除一个文件或目录，删掉就找不回来了。",
                    Dangerous = true,
                    Parameters = new Params().Add("path", "string", "要删除的路径", true).Build(),
                },
                new ToolSpec
                {
                    Name = "find_files",
                    Title = "查找文件",
                    Description = "按通配符找文件，例如 \"**/*.ts\" 或 \"src/*.json\"。",
                    Parameters = new Params()
                        .Add("pattern", "string", "通配符，** 匹配任意层级", true)
                        .Add("path", "string", folderOptional)
                        .Add("limit", "integer", "最多返回多少条，默认 80")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "search_text",
                    Title = "在文件里搜索",
                    Description = "用正则搜索文件内容，返回命中的行。",
                    Parameters = new Params()
                        .Add("query", "string", "正则表达式", true)
                        .Add("glob", "string", "可选的文件过滤，例如 \"*.ts\"")
                        .Add("path", "string", folderOptional)
                        .Add("limit", "integer", "最多返回多少条，默认 60")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "run_command",
                    Title = "执行命令",
                    Description = "在工作区里执行一条 shell 命令并取回输出。Windows 上优先用 PowerShell。",
                    Dangerous = true,
                    Parameters = new Params()
                        .Add("command", "string", "要执行的命令行", true)
                        .Add("cwd", "string", folderOptional)
                        .Add("timeoutMs", "integer", "超时毫秒数，默认 60000")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "web_search",
                    Title = "搜索网页",
                    Description = "上网搜索，返回前几条结果的标题、网址和摘要。需要最新的事实、文档或新闻时先搜这里，"
                                  + "再用 fetch_url 打开其中最合适的一条细看。",
                    Parameters = new Params()
                        .Add("query", "string", "搜索词", true)
                        .Add("limit", "integer", "要几条结果，默认 6，最多 12")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "fetch_url",
                    Title = "读取网页",
                    Description = "下载一个网址并返回可读文本，链接会以 [标题](网址) 的形式保留在正文里。"
                                  + "HTML 页面和 JSON 接口都能读。",
                    Parameters = new Params()
                        .Add("url", "string", "http 或 https 网址", true)
                        .Add("maxChars", "integer", "文本上限，默认 20000")
                        .Build(),
                },
                new ToolSpec
                {
                    Name = "im_list",
                    Title = "查找微信 / QQ",
                    Description = "列出这台电脑上能发消息的聊天软件。微信和 QQ 都没有开放的发送接口，所以发消息是"
                                  + "驱动已经登录的那个窗口来完成的。不确定微信或 QQ 开没开的时候先叫这个。",
                    Parameters = new Params().Build(),
                },
                new ToolSpec
                {
                    Name = "im_send",
                    Title = "发送微信 / QQ 消息",
                    Description = "以主人的身份，通过已经登录的微信或 QQ 发一条消息。它会唤起那个软件、按名字搜索"
                                  + "联系人、打开会话并回车，全程不需要主人动手。名字要和在那个软件的搜索框里出现的"
                                  + "写法一致——昵称和备注都可以。调用之前先把联系人和内容念给主人听一遍，"
                                  + "绝对不要猜一个联系人名字。",
                    Dangerous = true,
                    Parameters = new Params()
                        .Choice("app", "要驱动哪个：'wechat' 或 'qq'", new[] { "wechat", "qq" }, true)
                        .Add("to", "string", "联系人或群名，要和软件里显示的一字不差", true)
                        .Add("text", "string", "要发送的消息正文", true)
                        .Build(),
                },
            };

            if (aiController)
            {
                specs.Add(new ToolSpec
                {
                    Name = "ai_controller",
                    Title = "AI Controller",
                    Description = "启动或停止随程序发布的 AI Controller——一套独立的桌面自动化程序，"
                                  + "能自己控制鼠标、键盘和屏幕把任务做完。需要动手操作桌面时再启动它。",
                    Dangerous = true,
                    Parameters = new Params()
                        .Choice("action", "'start' 启动，'stop' 关掉所有在跑的实例", new[] { "start", "stop" }, true)
                        .Build(),
                });
            }

            if (!web)
            {
                specs.RemoveAll(spec => spec.Name == "web_search" || spec.Name == "fetch_url");
            }

            if (!hasWorkspace)
            {
                specs.RemoveAll(spec => WorkspaceToolNames.Contains(spec.Name));
            }

            return specs;
        }

        /// <summary>离了工作区就没法干活的那些工具。</summary>
        private static readonly HashSet<string> WorkspaceToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "list_dir", "read_file", "write_file", "replace_in_file", "make_dir", "move_path",
            "delete_path", "find_files", "search_text", "run_command",
        };

        // ------------------------------------------------------------------ 路径安全

        private static string ResolvePath(ToolContext ctx, string raw)
        {
            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return Path.GetFullPath(ctx.Workspace);
            }

            if (text == "~" || text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.GetFullPath(Path.Combine(home, text.Substring(1).TrimStart('\\', '/')));
            }

            return Path.IsPathRooted(text)
                ? Path.GetFullPath(text)
                : Path.GetFullPath(Path.Combine(ctx.Workspace, text));
        }

        /// <summary>工作区权限下，跑到工作区外面去的路径直接拒绝。</summary>
        private static string ResolveToolPath(ToolContext ctx, string raw)
        {
            string target = ResolvePath(ctx, raw);
            if (ctx.Scope == "machine")
            {
                return target;
            }

            string root = Path.GetFullPath(ctx.Workspace);
            string withSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!string.Equals(target, root, StringComparison.OrdinalIgnoreCase)
                && !target.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new PathJailException(
                    "「" + raw + "」在工作区（" + ctx.Workspace + "）之外。确实要动那里的话，"
                    + "把权限切成整台电脑。");
            }

            return target;
        }

        /// <summary>相对工作区的显示路径；跑到外面去了就直接显示绝对路径。</summary>
        private static string Rel(ToolContext ctx, string absolute)
        {
            try
            {
                string root = Path.GetFullPath(ctx.Workspace);
                string relative = Path.GetRelativePath(root, absolute);
                if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                {
                    return absolute;
                }

                return relative.Length == 0 ? "." : relative.Replace('\\', '/');
            }
            catch
            {
                return absolute;
            }
        }

        /// <summary>盘符或共享根，例如 C:\ ——从来不是能删的东西。</summary>
        private static bool IsVolumeRoot(string target)
        {
            try
            {
                string full = Path.GetFullPath(target).TrimEnd('\\', '/');
                string? root = Path.GetPathRoot(full);
                return root != null && string.Equals(root.TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ 小工具

        private static bool IsProbablyBinary(string file)
        {
            if (BinaryExtensions.Contains(Path.GetExtension(file)))
            {
                return true;
            }

            try
            {
                using FileStream stream = File.OpenRead(file);
                byte[] buffer = new byte[512];
                int read = stream.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 读不了的文件，对我们来说和二进制一样
            }

            return false;
        }

        private static (string Text, bool Cut) Truncate(string text, int limit)
        {
            if (text.Length <= limit)
            {
                return (text, false);
            }

            return (text.Substring(0, limit), true);
        }

        private sealed class Budget
        {
            public int N;
        }

        private static IEnumerable<(string Abs, bool Dir, int Depth)> Walk(string dir, int depth, Budget budget)
        {
            if (budget.N <= 0)
            {
                yield break;
            }

            List<(bool Dir, string Name)> entries = new List<(bool Dir, string Name)>();
            try
            {
                DirectoryInfo info = new DirectoryInfo(dir);
                foreach (FileSystemInfo entry in info.EnumerateFileSystemInfos())
                {
                    entries.Add((entry is DirectoryInfo, entry.Name));
                }
            }
            catch
            {
                yield break;
            }

            entries.Sort((left, right) => left.Dir == right.Dir
                ? string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                : (left.Dir ? -1 : 1));

            foreach ((bool isDir, string name) in entries)
            {
                if (budget.N-- <= 0)
                {
                    yield break;
                }

                if (isDir && SkipDirs.Contains(name))
                {
                    continue;
                }

                string abs = Path.Combine(dir, name);
                yield return (abs, isDir, depth);

                if (isDir && depth > 1)
                {
                    foreach ((string child, bool childDir, int childDepth) in Walk(abs, depth - 1, budget))
                    {
                        yield return (child, childDir, childDepth);
                    }
                }
            }
        }

        /// <summary>*、?、** 和 [abc] 组成的简易通配符。</summary>
        public static Regex GlobToRegExp(string pattern)
        {
            string norm = (pattern ?? string.Empty).Replace('\\', '/');
            if (norm.StartsWith("./", StringComparison.Ordinal))
            {
                norm = norm.Substring(2);
            }

            StringBuilder output = new StringBuilder();
            for (int i = 0; i < norm.Length; i++)
            {
                char c = norm[i];
                if (c == '*')
                {
                    if (i + 1 < norm.Length && norm[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < norm.Length && norm[i + 1] == '/')
                        {
                            i++;
                        }

                        output.Append("(?:.*/)?");
                    }
                    else
                    {
                        output.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    output.Append("[^/]");
                }
                else if (c == '[')
                {
                    int end = norm.IndexOf(']', i);
                    if (end > i)
                    {
                        output.Append(norm, i, end - i + 1);
                        i = end;
                    }
                    else
                    {
                        output.Append("\\[");
                    }
                }
                else
                {
                    output.Append(Regex.Escape(c.ToString()));
                }
            }

            return new Regex("^" + output + "$", RegexOptions.IgnoreCase);
        }

        // ------------------------------------------------------------------ 执行

        public static async Task<ToolOutcome> ExecuteAsync(ToolContext ctx, ToolCall call, CancellationToken token)
        {
            try
            {
                return await RunAsync(ctx, call, token);
            }
            catch (PathJailException error)
            {
                return new ToolOutcome { Ok = false, Summary = error.Message };
            }
            catch (OperationCanceledException)
            {
                return new ToolOutcome { Ok = false, Summary = "已取消" };
            }
            catch (Exception error)
            {
                return new ToolOutcome { Ok = false, Summary = error.Message };
            }
        }

        private static async Task<ToolOutcome> RunAsync(ToolContext ctx, ToolCall call, CancellationToken token)
        {
            // 设置里填 0 表示不限输出，这里用一个宽松的封顶代替
            int limit = ctx.MaxOutput > 0 ? ctx.MaxOutput : 400_000;

            // 没有工作区时这些工具本就不该挂在模型手里；万一还是被叫了，给一句人话而不是空路径乱跑
            if (!ctx.HasWorkspace && WorkspaceToolNames.Contains(call.Name))
            {
                return Fail("还没有选择工作区：「" + call.Name + "」需要先有一个工作目录。"
                            + "请提醒主人到「工作区」页挑一个文件夹。");
            }

            switch (call.Name)
            {
                case "list_dir":
                {
                    string basePath = ResolveToolPath(ctx, call.Text("path"));
                    if (!Directory.Exists(basePath))
                    {
                        return Fail(File.Exists(basePath) ? call.Text("path") + " 是一个文件" : "找不到：" + call.Text("path"));
                    }

                    int depth = Math.Clamp(call.Int("depth", 1), 1, 4);
                    int cap = Math.Clamp(call.Int("limit", 400), 1, 2000);
                    Budget budget = new Budget { N = cap + 1 };
                    List<string> lines = new List<string>();

                    foreach ((string abs, bool dir, int _) in Walk(basePath, depth, budget))
                    {
                        if (lines.Count >= cap)
                        {
                            lines.Add("… 还有更多条目被藏起来了");
                            break;
                        }

                        string size = string.Empty;
                        if (!dir)
                        {
                            try
                            {
                                size = " (" + new FileInfo(abs).Length + "b)";
                            }
                            catch
                            {
                                // 拿不到大小就不显示
                            }
                        }

                        lines.Add((dir ? "d " : "- ") + Rel(ctx, abs) + size);
                    }

                    if (lines.Count == 0)
                    {
                        lines.Add("（空目录）");
                    }

                    return Done(lines.Count + " 条，位于 " + Rel(ctx, basePath), string.Join("\n", lines));
                }

                case "read_file":
                {
                    string file = ResolveToolPath(ctx, call.Text("path"));
                    if (!File.Exists(file))
                    {
                        return Fail("找不到：" + call.Text("path"));
                    }

                    if (Directory.Exists(file))
                    {
                        return Fail(call.Text("path") + " 是一个目录");
                    }

                    FileInfo info = new FileInfo(file);
                    if (IsProbablyBinary(file))
                    {
                        return Fail(call.Text("path") + " 看起来是二进制文件（" + info.Length + " 字节）");
                    }

                    string raw;
                    using (FileStream stream = File.OpenRead(file))
                    {
                        int take = (int)Math.Min(stream.Length, MaxReadBytes);
                        byte[] buffer = new byte[take];
                        int read = stream.Read(buffer, 0, take);
                        raw = Encoding.UTF8.GetString(buffer, 0, read);
                    }

                    bool cut = info.Length > MaxReadBytes;
                    string[] all = Regex.Split(raw, "\r\n|\n|\r");
                    int start = Math.Max(1, call.Int("start", 1));
                    int count = Math.Clamp(call.Int("lines", all.Length), 1, 20000);

                    List<string> numbered = new List<string>();
                    for (int i = start - 1; i < all.Length && i < start - 1 + count; i++)
                    {
                        numbered.Add((start + (i - (start - 1))).ToString().PadLeft(5) + "| " + all[i]);
                    }

                    string tail = cut ? "\n… 文件在 " + MaxReadBytes + " 字节处被截断" : string.Empty;
                    int lastLine = start + numbered.Count - 1;

                    return Done(
                        Rel(ctx, file) + " 的第 " + start + "-" + lastLine + " 行，共 " + all.Length + " 行",
                        string.Join("\n", numbered) + tail);
                }

                case "write_file":
                {
                    string file = ResolveToolPath(ctx, call.Text("path"));
                    string content = call.Text("content");
                    if (Encoding.UTF8.GetByteCount(content) > MaxWriteBytes)
                    {
                        return Fail("拒绝写入 " + (content.Length / 1e6).ToString("0.0") + " MB");
                    }

                    string? parent = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    bool existed = File.Exists(file);
                    await File.WriteAllTextAsync(file, content, Encoding.UTF8, token);

                    int lineCount = content.Split('\n').Length;
                    return Done((existed ? "覆盖了 " : "新建了 ") + Rel(ctx, file)
                                + "（" + content.Length + " 字符，" + lineCount + " 行）");
                }

                case "replace_in_file":
                {
                    string file = ResolveToolPath(ctx, call.Text("path"));
                    if (!File.Exists(file))
                    {
                        return Fail("找不到：" + call.Text("path"));
                    }

                    string original = await File.ReadAllTextAsync(file, token);
                    string find = call.Text("find");
                    string replace = call.Text("replace");
                    if (find.Length == 0)
                    {
                        return Fail("「find」是空的");
                    }

                    int hits = CountOccurrences(original, find);
                    if (hits == 0)
                    {
                        return Fail("在 " + call.Text("path") + " 里没找到这段原文");
                    }

                    if (hits > 1 && !call.Flag("all"))
                    {
                        return Fail("这段原文在 " + call.Text("path") + " 里出现了 " + hits
                                    + " 次；请加上 all=true，或者把它改得更独特一些");
                    }

                    string next = call.Flag("all")
                        ? original.Replace(find, replace)
                        : ReplaceFirst(original, find, replace);

                    await File.WriteAllTextAsync(file, next, Encoding.UTF8, token);

                    return Done((hits > 1 ? hits + " 处替换" : "1 处替换") + "，在 " + Rel(ctx, file));
                }

                case "make_dir":
                {
                    string dir = ResolveToolPath(ctx, call.Text("path"));
                    Directory.CreateDirectory(dir);
                    return Done("目录已就绪：" + Rel(ctx, dir));
                }

                case "move_path":
                {
                    string from = ResolveToolPath(ctx, call.Text("from"));
                    string to = ResolveToolPath(ctx, call.Text("to"));
                    if (!File.Exists(from) && !Directory.Exists(from))
                    {
                        return Fail("找不到：" + call.Text("from"));
                    }

                    string? parent = Path.GetDirectoryName(to);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    if (Directory.Exists(from))
                    {
                        Directory.Move(from, to);
                    }
                    else
                    {
                        File.Move(from, to, true);
                    }

                    return Done(Rel(ctx, from) + " → " + Rel(ctx, to));
                }

                case "delete_path":
                {
                    string target = ResolveToolPath(ctx, call.Text("path"));
                    string root = Path.GetFullPath(ctx.Workspace);

                    if (string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
                    {
                        return Fail("拒绝删除工作区根目录");
                    }

                    if (IsVolumeRoot(target))
                    {
                        return Fail("拒绝删除整个盘（" + target + "）");
                    }

                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, true);
                    }
                    else if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                    else
                    {
                        return Fail("找不到：" + call.Text("path"));
                    }

                    return Done("已删除 " + Rel(ctx, target));
                }

                case "find_files":
                {
                    string pattern = string.IsNullOrEmpty(call.Text("pattern")) ? "**/*" : call.Text("pattern");
                    Regex regex = GlobToRegExp(pattern);
                    int cap = Math.Clamp(call.Int("limit", 80), 1, 500);
                    List<string> found = new List<string>();
                    Budget budget = new Budget { N = 20_000 };

                    string from = call.Text("path").Length > 0
                        ? ResolveToolPath(ctx, call.Text("path"))
                        : Path.GetFullPath(ctx.Workspace);

                    if (!Directory.Exists(from))
                    {
                        return Fail("找不到：" + call.Text("path"));
                    }

                    foreach ((string abs, bool dir, int _) in Walk(from, 12, budget))
                    {
                        if (dir)
                        {
                            continue;
                        }

                        string relative = Rel(ctx, abs);
                        if (regex.IsMatch(relative) || regex.IsMatch(Path.GetFileName(relative)))
                        {
                            found.Add(relative);
                            if (found.Count >= cap)
                            {
                                break;
                            }
                        }
                    }

                    return Done(found.Count + " 个文件匹配 " + pattern,
                        found.Count > 0 ? string.Join("\n", found) : "（没有匹配）");
                }

                case "search_text":
                {
                    string query = call.Text("query");
                    if (query.Length == 0)
                    {
                        return Fail("搜索词是空的");
                    }

                    Regex regex;
                    try
                    {
                        regex = new Regex(query, RegexOptions.IgnoreCase);
                    }
                    catch (Exception error)
                    {
                        return Fail("正则表达式不合法：" + error.Message);
                    }

                    Regex? fileFilter = call.Text("glob").Length > 0 ? GlobToRegExp(call.Text("glob")) : null;
                    int cap = Math.Clamp(call.Int("limit", 60), 1, 400);
                    List<string> hits = new List<string>();
                    Budget budget = new Budget { N = 20_000 };

                    string from = call.Text("path").Length > 0
                        ? ResolveToolPath(ctx, call.Text("path"))
                        : Path.GetFullPath(ctx.Workspace);

                    if (!Directory.Exists(from))
                    {
                        return Fail("找不到：" + call.Text("path"));
                    }

                    foreach ((string abs, bool dir, int _) in Walk(from, 12, budget))
                    {
                        if (dir || IsProbablyBinary(abs))
                        {
                            continue;
                        }

                        string relative = Rel(ctx, abs);
                        if (fileFilter != null
                            && !fileFilter.IsMatch(relative)
                            && !fileFilter.IsMatch(Path.GetFileName(relative)))
                        {
                            continue;
                        }

                        string text;
                        try
                        {
                            if (new FileInfo(abs).Length > 1_500_000)
                            {
                                continue;
                            }

                            text = File.ReadAllText(abs);
                        }
                        catch
                        {
                            continue;
                        }

                        string[] lines = Regex.Split(text, "\r\n|\n|\r");
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                            {
                                string trimmed = lines[i].Trim();
                                hits.Add(relative + ":" + (i + 1) + ": "
                                         + (trimmed.Length > 240 ? trimmed.Substring(0, 240) : trimmed));

                                if (hits.Count >= cap)
                                {
                                    break;
                                }
                            }
                        }

                        if (hits.Count >= cap)
                        {
                            break;
                        }
                    }

                    return Done(hits.Count + " 处匹配 /" + query + "/",
                        hits.Count > 0 ? string.Join("\n", hits) : "（没有匹配）");
                }

                case "run_command":
                {
                    string command = call.Text("command").Trim();
                    if (command.Length == 0)
                    {
                        return Fail("命令是空的");
                    }

                    string cwd = call.Text("cwd").Length > 0
                        ? ResolveToolPath(ctx, call.Text("cwd"))
                        : Path.GetFullPath(ctx.Workspace);

                    if (!Directory.Exists(cwd))
                    {
                        return Fail("工作目录不存在：" + call.Text("cwd"));
                    }

                    int requested = call.Int("timeoutMs", ctx.ShellTimeoutMs);
                    int timeout = requested > 0 ? Math.Clamp(requested, 1000, 600_000) : 0;

                    return await RunShellAsync(command, cwd, timeout, limit, token);
                }

                case "web_search":
                {
                    string query = call.Text("query").Trim();
                    if (query.Length == 0)
                    {
                        return Fail("搜索词是空的");
                    }

                    int count = Math.Clamp(call.Int("limit", 6), 1, 12);
                    WebSearchOutcome result = await WebSearch.SearchAsync(query, count, token);
                    if (!result.Ok)
                    {
                        return Fail("搜索失败：" + result.Error);
                    }

                    List<string> lines = new List<string>();
                    for (int i = 0; i < result.Hits.Count; i++)
                    {
                        WebHit hit = result.Hits[i];
                        string block = (i + 1) + ". " + hit.Title + "\n   " + hit.Url;
                        if (hit.Snippet.Length > 0)
                        {
                            block += "\n   " + hit.Snippet;
                        }

                        lines.Add(block);
                    }

                    return Done(result.Hits.Count + " 条结果，来自 " + result.Engine + "，搜索「" + query + "」",
                        string.Join("\n\n", lines));
                }

                case "fetch_url":
                {
                    string url = call.Text("url");
                    if (!Regex.IsMatch(url, "^https?://", RegexOptions.IgnoreCase))
                    {
                        return Fail("只支持 http 和 https 网址");
                    }

                    int budget = Math.Clamp(call.Int("maxChars", Math.Min(30_000, limit)), 2000, 80_000);
                    WebPage page = await WebSearch.ReadPageAsync(url, budget, token);
                    if (page.Error.Length > 0)
                    {
                        return Fail("打不开 " + url + "：" + page.Error);
                    }

                    return new ToolOutcome
                    {
                        Ok = page.Ok,
                        Summary = page.Status + " " + page.Title + "（" + page.Text.Length + " 字符）",
                        Output = page.Text,
                    };
                }

                case "im_list":
                {
                    ImListResult found = await Im.ListAsync(token);
                    if (!found.Ok)
                    {
                        return Fail("找不到微信 / QQ：" + found.Error);
                    }

                    if (found.Targets.Count == 0)
                    {
                        return Done("微信和 QQ 都没有在运行");
                    }

                    List<string> names = new List<string>();
                    foreach (ImTarget target in found.Targets)
                    {
                        names.Add(target.App + "（" + (target.Title.Length > 0 ? target.Title : target.Process) + "）");
                    }

                    return Done("正在运行：" + string.Join("、", names)
                                + "。用 im_send 时要给出软件名和一字不差的联系人名。");
                }

                case "im_send":
                {
                    string app = call.Text("app").Trim();
                    string to = call.Text("to").Trim();
                    string text = call.Text("text");
                    if (to.Length == 0 || text.Length == 0)
                    {
                        return Fail("im_send 需要联系人和消息正文");
                    }

                    ImSendResult sent = await Im.SendAsync(app, to, text, token);
                    if (!sent.Ok)
                    {
                        return Fail("发给 " + to + " 失败：" + sent.Error);
                    }

                    string preview = text.Length > 40 ? text.Substring(0, 40) + "…" : text;
                    return Done("已把「" + preview + "」通过 " + (sent.App.Length > 0 ? sent.App : app) + " 发给 " + to);
                }

                case "ai_controller":
                {
                    if (!ctx.AiController)
                    {
                        return Fail("AI Controller 联动没有开启");
                    }

                    return Fail("这个版本没有随包发布 AI Controller 程序");
                }

                default:
                    return Fail("未知工具「" + call.Name + "」");
            }
        }

        private static ToolOutcome Done(string summary, string output = "")
        {
            return new ToolOutcome { Ok = true, Summary = summary, Output = output };
        }

        private static ToolOutcome Fail(string summary)
        {
            return new ToolOutcome { Ok = false, Summary = summary };
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static string ReplaceFirst(string text, string find, string replace)
        {
            int index = text.IndexOf(find, StringComparison.Ordinal);
            return index < 0 ? text : text.Substring(0, index) + replace + text.Substring(index + find.Length);
        }

        private static async Task<ToolOutcome> RunShellAsync(
            string command, string cwd, int timeoutMs, int limit, CancellationToken token)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(command);

            using Process process = new Process { StartInfo = start };

            try
            {
                process.Start();
            }
            catch (Exception error)
            {
                return Fail("起不了 shell：" + error.Message);
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            bool killed = false;
            bool finished = timeoutMs > 0
                ? process.WaitForExit(timeoutMs)
                : WaitForever(process);

            if (!finished)
            {
                killed = true;
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 已经退了
                }

                process.WaitForExit(5000);
            }

            string outText = await stdout;
            string errText = await stderr;

            List<string> merged = new List<string>
            {
                "$ " + command,
                "退出码 " + process.ExitCode + (killed ? "（超过 " + timeoutMs + " 毫秒被杀掉）" : string.Empty),
                string.Empty,
            };

            if (outText.Trim().Length > 0)
            {
                merged.Add(outText.TrimEnd());
            }

            if (errText.Trim().Length > 0)
            {
                merged.Add(string.Empty);
                merged.Add("[stderr]");
                merged.Add(errText.TrimEnd());
            }

            (string text, bool cut) = Truncate(string.Join("\n", merged), limit);

            return new ToolOutcome
            {
                Ok = process.ExitCode == 0 && !killed,
                Summary = killed ? "超过 " + timeoutMs + " 毫秒超时" : "退出码 " + process.ExitCode,
                Output = text + (cut ? "\n[输出被截断]" : string.Empty),
            };
        }

        private static bool WaitForever(Process process)
        {
            process.WaitForExit();
            return true;
        }
    }
}
