using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DomeOSAgentDev
{
    internal sealed class WebHit
    {
        public string Title { get; set; } = "";

        public string Url { get; set; } = "";

        public string Snippet { get; set; } = "";
    }

    internal sealed class WebSearchOutcome
    {
        public bool Ok { get; set; }

        public string Engine { get; set; } = "";

        public List<WebHit> Hits { get; } = new List<WebHit>();

        public string Error { get; set; } = "";
    }

    internal sealed class WebPage
    {
        public bool Ok { get; set; }

        public int Status { get; set; }

        public string Title { get; set; } = "";

        public string Text { get; set; } = "";

        public string Error { get; set; } = "";
    }

    /// <summary>
    /// 不需要任何付费搜索密钥的联网能力：直接读公开的结果页，一家不通就换下一家。
    /// 顺序按可连通性排，国内能直连的排前面，国际的排后面。
    /// </summary>
    internal static class WebSearch
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = true,
            });

            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            return client;
        }

        private static readonly Dictionary<string, string> Entities = new Dictionary<string, string>
        {
            ["&amp;"] = "&",
            ["&lt;"] = "<",
            ["&gt;"] = ">",
            ["&quot;"] = "\"",
            ["&#39;"] = "'",
            ["&apos;"] = "'",
            ["&nbsp;"] = " ",
            ["&hellip;"] = "…",
            ["&mdash;"] = "—",
            ["&ndash;"] = "–",
            ["&rsquo;"] = "’",
            ["&lsquo;"] = "‘",
            ["&ldquo;"] = "“",
            ["&rdquo;"] = "”",
            ["&middot;"] = "·",
            ["&times;"] = "×",
        };

        public static string DecodeEntities(string text)
        {
            string decoded = Regex.Replace(text ?? string.Empty, "&(?:amp|lt|gt|quot|apos|#39|nbsp|hellip|mdash|ndash|rsquo|lsquo|ldquo|rdquo|middot|times);",
                match => Entities.TryGetValue(match.Value, out string? value) ? value : match.Value);

            decoded = Regex.Replace(decoded, "&#(\\d+);", match =>
            {
                int code = int.Parse(match.Groups[1].Value);
                return char.ConvertFromUtf32(code);
            });

            decoded = Regex.Replace(decoded, "&#x([0-9a-f]+);", match =>
            {
                int code = Convert.ToInt32(match.Groups[1].Value, 16);
                return char.ConvertFromUtf32(code);
            }, RegexOptions.IgnoreCase);

            return decoded;
        }

        private static string StripTags(string html)
        {
            string text = DecodeEntities(Regex.Replace(html ?? string.Empty, "<[^>]*>", " "));
            return Regex.Replace(text, "\\s+", " ").Trim();
        }

        /// <summary>搜索引擎会把结果链接包一层，这里拆掉常见的那几种。</summary>
        private static string Unwrap(string url)
        {
            string output = DecodeEntities(url ?? string.Empty);
            Match redirect = Regex.Match(output, "[?&]uddg=([^&]+)");
            if (redirect.Success)
            {
                try
                {
                    output = Uri.UnescapeDataString(redirect.Groups[1].Value);
                }
                catch
                {
                    // 解不开就留着原始链接
                }
            }

            if (output.StartsWith("//", StringComparison.Ordinal))
            {
                output = "https:" + output;
            }

            return output;
        }

        /// <summary>两个标记之间的所有片段，不在意属性顺序。</summary>
        private static List<string> Blocks(string html, string startPattern)
        {
            List<string> output = new List<string>();
            Regex regex = new Regex(startPattern, RegexOptions.IgnoreCase);
            Match match = regex.Match(html);

            while (match.Success)
            {
                string rest = html.Substring(match.Index + match.Length);
                Match next = regex.Match(rest);
                int end = next.Success ? match.Index + match.Length + next.Index : match.Index + 4000;
                end = Math.Min(end, html.Length);
                output.Add(html.Substring(match.Index, end - match.Index));
                match = regex.Match(html, match.Index + match.Length);
            }

            return output;
        }

        private static string FirstLink(string block)
        {
            Match match = Regex.Match(block, "<a[^>]+href=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? Unwrap(match.Groups[1].Value) : string.Empty;
        }

        private static List<WebHit> ParseBing(string html, int limit)
        {
            List<WebHit> hits = new List<WebHit>();
            foreach (string block in Blocks(html, "<li class=\"b_algo\""))
            {
                if (hits.Count >= limit)
                {
                    break;
                }

                Match heading = Regex.Match(block, "<h2[^>]*>[\\s\\S]*?<a[^>]+href=\"([^\"]+)\"[^>]*>([\\s\\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                if (!heading.Success)
                {
                    continue;
                }

                string url = Unwrap(heading.Groups[1].Value);
                if (!Regex.IsMatch(url, "^https?:", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                Match snippet = Regex.Match(block, "<p[^>]*>([\\s\\S]*?)</p>", RegexOptions.IgnoreCase);
                hits.Add(new WebHit
                {
                    Title = StripTags(heading.Groups[2].Value),
                    Url = url,
                    Snippet = snippet.Success ? StripTags(snippet.Groups[1].Value) : string.Empty,
                });
            }

            return hits;
        }

        private static List<WebHit> ParseBaidu(string html, int limit)
        {
            List<WebHit> hits = new List<WebHit>();
            foreach (string block in Blocks(html, "<div[^>]+class=\"result[ \"]"))
            {
                if (hits.Count >= limit)
                {
                    break;
                }

                Match heading = Regex.Match(block, "<h3[^>]*>[\\s\\S]*?<a[^>]+href=\"([^\"]+)\"[^>]*>([\\s\\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                if (!heading.Success)
                {
                    continue;
                }

                string url = Unwrap(heading.Groups[1].Value);
                // 百度自己的 AI 卡片和中转链接都是噪音
                if (!Regex.IsMatch(url, "^https?:", RegexOptions.IgnoreCase) || Regex.IsMatch(url, "baidu\\.com/link\\?"))
                {
                    continue;
                }

                Match snippet = Regex.Match(block, "<span[^>]+class=\"[^\"]*content-right[^\"]*\"[^>]*>([\\s\\S]*?)</span>",
                    RegexOptions.IgnoreCase);
                if (!snippet.Success)
                {
                    snippet = Regex.Match(block, "<div[^>]+class=\"[^\"]*c-abstract[^\"]*\"[^>]*>([\\s\\S]*?)</div>",
                        RegexOptions.IgnoreCase);
                }

                hits.Add(new WebHit
                {
                    Title = StripTags(heading.Groups[2].Value),
                    Url = url,
                    Snippet = snippet.Success ? StripTags(snippet.Groups[1].Value) : string.Empty,
                });
            }

            return hits;
        }

        private static List<WebHit> Parse360(string html, int limit)
        {
            List<WebHit> hits = new List<WebHit>();
            foreach (string block in Blocks(html, "<li[^>]+class=\"res-list\""))
            {
                if (hits.Count >= limit)
                {
                    break;
                }

                Match anchor = Regex.Match(block, "<h3[^>]*>[\\s\\S]*?<a[^>]+href=\"([^\"]+)\"([^>]*)>([\\s\\S]*?)</a>",
                    RegexOptions.IgnoreCase);
                if (!anchor.Success)
                {
                    continue;
                }

                // 360 会经 /link?m=… 中转，真正的地址在 data-mdurl 里
                Match real = Regex.Match(anchor.Groups[2].Value, "data-mdurl=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                string url = Unwrap(real.Success ? real.Groups[1].Value : anchor.Groups[1].Value);
                if (!Regex.IsMatch(url, "^https?:", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                Match snippet = Regex.Match(block, "<p[^>]+class=\"[^\"]*res-desc[^\"]*\"[^>]*>([\\s\\S]*?)</p>",
                    RegexOptions.IgnoreCase);
                hits.Add(new WebHit
                {
                    Title = StripTags(anchor.Groups[3].Value),
                    Url = url,
                    Snippet = snippet.Success ? StripTags(snippet.Groups[1].Value) : string.Empty,
                });
            }

            return hits;
        }

        private static List<WebHit> ParseDuckDuckGo(string html, int limit)
        {
            List<WebHit> hits = new List<WebHit>();
            List<string> snippets = new List<string>();

            Match snippetMatch = Regex.Match(html, "<a[^>]+class=\"[^\"]*result__snippet[^\"]*\"[^>]*>([\\s\\S]*?)</a>",
                RegexOptions.IgnoreCase);
            while (snippetMatch.Success)
            {
                snippets.Add(StripTags(snippetMatch.Groups[1].Value));
                snippetMatch = snippetMatch.NextMatch();
            }

            Match match = Regex.Match(html, "<a[^>]+class=\"[^\"]*result__a[^\"]*\"[^>]+href=\"([^\"]+)\"[^>]*>([\\s\\S]*?)</a>",
                RegexOptions.IgnoreCase);

            int index = 0;
            while (match.Success && hits.Count < limit)
            {
                string url = Unwrap(match.Groups[1].Value);
                if (Regex.IsMatch(url, "^https?:", RegexOptions.IgnoreCase))
                {
                    hits.Add(new WebHit
                    {
                        Title = StripTags(match.Groups[2].Value),
                        Url = url,
                        Snippet = index < snippets.Count ? snippets[index] : string.Empty,
                    });

                    index++;
                }

                match = match.NextMatch();
            }

            return hits;
        }

        private static List<WebHit> ParseBrave(string html, int limit)
        {
            List<WebHit> hits = new List<WebHit>();
            Match match = Regex.Match(html,
                "<a[^>]+href=\"(https?://[^\"]+)\"[^>]*>[\\s\\S]*?<div[^>]*class=\"[^\"]*title[^\"]*\"[^>]*>([\\s\\S]*?)</div>",
                RegexOptions.IgnoreCase);

            while (match.Success && hits.Count < limit)
            {
                hits.Add(new WebHit
                {
                    Title = StripTags(match.Groups[2].Value),
                    Url = match.Groups[1].Value,
                });

                match = match.NextMatch();
            }

            return hits;
        }

        private sealed class Engine
        {
            public string Name = "";

            public Func<string, string> Url = _ => "";

            public Func<string, int, List<WebHit>> Parse = (_, _) => new List<WebHit>();
        }

        private static readonly List<Engine> Engines = new List<Engine>
        {
            new Engine
            {
                Name = "bing",
                Url = q => "https://cn.bing.com/search?q=" + Uri.EscapeDataString(q) + "&setlang=zh-CN",
                Parse = ParseBing,
            },
            new Engine
            {
                Name = "baidu",
                Url = q => "https://www.baidu.com/s?wd=" + Uri.EscapeDataString(q) + "&rn=20",
                Parse = ParseBaidu,
            },
            new Engine
            {
                Name = "so360",
                Url = q => "https://www.so.com/s?q=" + Uri.EscapeDataString(q),
                Parse = Parse360,
            },
            new Engine
            {
                Name = "duckduckgo",
                Url = q => "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q),
                Parse = ParseDuckDuckGo,
            },
            new Engine
            {
                Name = "brave",
                Url = q => "https://search.brave.com/search?q=" + Uri.EscapeDataString(q),
                Parse = ParseBrave,
            },
        };

        public static async Task<WebSearchOutcome> SearchAsync(string query, int limit, CancellationToken token)
        {
            List<string> tried = new List<string>();

            foreach (Engine engine in Engines)
            {
                try
                {
                    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(12_000);

                    using HttpResponseMessage response = await Client.GetAsync(engine.Url(query), timeout.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        tried.Add(engine.Name + "：HTTP " + (int)response.StatusCode);
                        continue;
                    }

                    string html = await response.Content.ReadAsStringAsync(timeout.Token);
                    List<WebHit> hits = engine.Parse(html, limit);
                    if (hits.Count > 0)
                    {
                        WebSearchOutcome outcome = new WebSearchOutcome { Ok = true, Engine = engine.Name };
                        outcome.Hits.AddRange(hits);
                        return outcome;
                    }

                    tried.Add(engine.Name + "：没有解析出结果");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return new WebSearchOutcome { Ok = true };
                }
                catch (Exception error)
                {
                    tried.Add(engine.Name + "：" + error.Message);
                }
            }

            return new WebSearchOutcome
            {
                Ok = false,
                Error = tried.Count > 0 ? string.Join("；", tried) : "所有搜索引擎都拒绝了这次请求",
            };
        }

        /// <summary>读一个页面，把 HTML 压成可读文本，链接留在正文里。</summary>
        public static async Task<WebPage> ReadPageAsync(string url, int limit, CancellationToken token)
        {
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(25_000);

                using HttpResponseMessage response = await Client.GetAsync(url, timeout.Token);
                string raw = await response.Content.ReadAsStringAsync(timeout.Token);
                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                int status = (int)response.StatusCode;

                if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    return new WebPage
                    {
                        Ok = response.IsSuccessStatusCode,
                        Status = status,
                        Title = url,
                        Text = raw.Length > limit ? raw.Substring(0, limit) + "\n[已截断]" : raw,
                    };
                }

                Match titleMatch = Regex.Match(raw, "<title[^>]*>([\\s\\S]*?)</title>", RegexOptions.IgnoreCase);
                string title = DecodeEntities((titleMatch.Success ? titleMatch.Groups[1].Value : string.Empty).Trim());

                // 保留链接目标，方便 Agent 继续往下追
                string withLinks = Regex.Replace(raw, "<a\\b[^>]*href=\"(https?://[^\"]+)\"[^>]*>([\\s\\S]*?)</a>",
                    match =>
                    {
                        string label = StripTags(match.Groups[2].Value);
                        return "[" + (label.Length > 0 ? label : match.Groups[1].Value) + "](" + match.Groups[1].Value + ")";
                    }, RegexOptions.IgnoreCase);

                string stripped = Regex.Replace(withLinks, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
                stripped = Regex.Replace(stripped, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
                stripped = Regex.Replace(stripped, "<noscript[\\s\\S]*?</noscript>", " ", RegexOptions.IgnoreCase);
                stripped = Regex.Replace(stripped, "<head[\\s\\S]*?</head>", " ", RegexOptions.IgnoreCase);
                stripped = Regex.Replace(stripped, "</(p|div|section|article|li|h[1-6]|tr|table)>", "\n", RegexOptions.IgnoreCase);
                stripped = Regex.Replace(stripped, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
                stripped = Regex.Replace(stripped, "<[^>]+>", " ");
                stripped = DecodeEntities(stripped);
                stripped = Regex.Replace(stripped, "[ \\t\\u00a0]{2,}", " ");
                stripped = Regex.Replace(stripped, "\n{3,}", "\n\n");

                List<string> lines = new List<string>();
                foreach (string line in stripped.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        lines.Add(trimmed);
                    }
                }

                string text = string.Join("\n", lines);
                bool cut = text.Length > limit;

                return new WebPage
                {
                    Ok = response.IsSuccessStatusCode,
                    Status = status,
                    Title = title.Length > 0 ? title : url,
                    Text = cut ? text.Substring(0, limit) + "\n\n[在 " + limit + " 字符处截断]" : text,
                };
            }
            catch (Exception error)
            {
                return new WebPage
                {
                    Ok = false,
                    Title = url,
                    Error = error.Message,
                };
            }
        }
    }
}
