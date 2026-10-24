using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DomeOSAgentDev
{
    internal sealed class ImTarget
    {
        public string App { get; set; } = "";

        public string Process { get; set; } = "";

        public string Title { get; set; } = "";

        public long Hwnd { get; set; }
    }

    internal sealed class ImListResult
    {
        public bool Ok { get; set; }

        public string Error { get; set; } = "";

        public List<ImTarget> Targets { get; } = new List<ImTarget>();
    }

    internal sealed class ImSendResult
    {
        public bool Ok { get; set; }

        public string Error { get; set; } = "";

        public string App { get; set; } = "";
    }

    /// <summary>
    /// 微信 / QQ 发送。这两个软件都没有可用的发送接口，所以发消息只能是驱动那个已经登录好的
    /// 窗口：随程序一起发布的 DomeOSIM.exe 负责唤起窗口、搜索联系人、输入并回车，本类只负责
    /// 把它跑起来并读懂它打印出来的那一行 JSON。它从不抛异常，失败一律以结果对象返回。
    /// </summary>
    internal static class Im
    {
        private const string HelperName = "DomeOSIM.exe";

        private const int ListTimeoutMs = 15_000;

        private const int SendTimeoutMs = 20_000;

        private static readonly string[] KnownApps = { "wechat", "qq" };

        /// <summary>助手就在主程序旁边，发布时是一起复制的。</summary>
        public static string HelperPath()
        {
            string beside = Path.Combine(AppContext.BaseDirectory, HelperName);
            if (File.Exists(beside))
            {
                return beside;
            }

            return Path.Combine(AppContext.BaseDirectory, "Assets", HelperName);
        }

        /// <summary>跑一次助手，取它打印的最后一行 JSON。</summary>
        private static async Task<string> RunAsync(List<string> args, int timeoutMs, CancellationToken token)
        {
            string exe = HelperPath();
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("找不到 " + HelperName);
            }

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (string argument in args)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("起不了 " + HelperName);

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 已经退了
                }

                throw new TimeoutException(HelperName + " 超时了");
            }

            string text = await output;
            await error;

            List<string> lines = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    lines.Add(trimmed);
                }
            }

            return lines.Count > 0 ? lines[lines.Count - 1] : string.Empty;
        }

        public static async Task<ImListResult> ListAsync(CancellationToken token)
        {
            try
            {
                string json = await RunAsync(new List<string> { "list" }, ListTimeoutMs, token);
                if (json.Length == 0)
                {
                    return new ImListResult { Error = HelperName + " 没有任何输出" };
                }

                if (JsonNode.Parse(json) is not JsonObject parsed)
                {
                    return new ImListResult { Error = HelperName + " 的输出看不懂：" + json };
                }

                ImListResult result = new ImListResult
                {
                    Ok = parsed["ok"]?.GetValue<bool>() ?? false,
                    Error = parsed["error"]?.GetValue<string>() ?? string.Empty,
                };

                if (parsed["targets"] is JsonArray targets)
                {
                    foreach (JsonNode? item in targets)
                    {
                        if (item is not JsonObject one)
                        {
                            continue;
                        }

                        result.Targets.Add(new ImTarget
                        {
                            App = one["app"]?.GetValue<string>() ?? string.Empty,
                            Process = one["proc"]?.GetValue<string>() ?? string.Empty,
                            Title = one["title"]?.GetValue<string>() ?? string.Empty,
                            Hwnd = one["hwnd"] is JsonValue handle && handle.TryGetValue(out long number) ? number : 0,
                        });
                    }
                }

                return result;
            }
            catch (Exception errorItem)
            {
                return new ImListResult { Error = errorItem.Message };
            }
        }

        public static async Task<ImSendResult> SendAsync(string app, string to, string text, CancellationToken token)
        {
            string name = (app ?? string.Empty).Trim().ToLowerInvariant();
            if (!KnownApps.Contains(name))
            {
                return new ImSendResult { Error = "不认识这个软件：" + app };
            }

            if (string.IsNullOrWhiteSpace(to))
            {
                return new ImSendResult { Error = "缺少收件人" };
            }

            if (string.IsNullOrEmpty(text))
            {
                return new ImSendResult { Error = "缺少消息正文" };
            }

            try
            {
                string json = await RunAsync(
                    new List<string> { "send", "--app", name, "--to", to, "--text", text, "--send", "1" },
                    SendTimeoutMs,
                    token);

                if (json.Length == 0)
                {
                    return new ImSendResult { Error = HelperName + " 没有任何输出" };
                }

                if (JsonNode.Parse(json) is not JsonObject parsed)
                {
                    return new ImSendResult { Error = HelperName + " 的输出看不懂：" + json };
                }

                return new ImSendResult
                {
                    Ok = parsed["ok"]?.GetValue<bool>() ?? false,
                    Error = parsed["error"]?.GetValue<string>() ?? string.Empty,
                    App = parsed["app"]?.GetValue<string>() ?? name,
                };
            }
            catch (Exception error)
            {
                return new ImSendResult { Error = error.Message };
            }
        }
    }
}
