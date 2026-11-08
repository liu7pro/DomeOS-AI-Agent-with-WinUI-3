using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DomeOSAgentDev
{
    /// <summary>一次 Agent 回合的结果。失败不会抛异常，而是回在这里。</summary>
    internal sealed class AgentOutcome
    {
        public string Text { get; set; } = "";

        public bool Ok { get; set; } = true;

        public string Hint { get; set; } = "";

        public int Steps { get; set; }
    }

    /// <summary>Agent 往界面上回报的钩子。界面只需要实现自己关心的那几个。</summary>
    internal sealed class AgentHooks
    {
        /// <summary>模型蹦出来的一小段文字。</summary>
        public Action<string>? Delta { get; set; }

        /// <summary>「正在请求 …」这类状态行。</summary>
        public Action<string>? Status { get; set; }

        /// <summary>模型自己写出来的思考块。</summary>
        public Action<string>? Thinking { get; set; }

        /// <summary>要动手了。第二个参数是这件事在该不该问主人：null 表示不认识这个工具。</summary>
        public Action<ToolCall, ToolSpec?, bool>? ToolStarted { get; set; }

        /// <summary>动手的结果：调用、一句话结论、是否成功、花了多久。</summary>
        public Action<ToolCall, string, bool, long>? ToolFinished { get; set; }

        /// <summary>危险操作要不要放行；返回 true 才继续。</summary>
        public Func<ToolCall, string, Task<bool>>? AskApproval { get; set; }

        /// <summary>主人拒绝了这次调用。</summary>
        public Action<ToolCall, string>? ToolDeclined { get; set; }
    }

    /// <summary>
    /// 工具循环：模型 → 工具 → 模型，直到这一轮结束。
    /// </summary>
    internal static class Agent
    {
        /// <summary>只是防止跑飞的模型无限循环。</summary>
        private const int HardStepCap = 400;

        private const string StepSeparator = "\n\n";

        /// <summary>
        /// 回答语言的规则。auto 就跟着主人的语言走——「精通所有语言」靠的就是这一条；
        /// 只有在设置里明确选了简体中文或者英文，才把语言钉死。
        /// </summary>
        private static string LanguageRuleFor(string language, bool persona)
        {
            if (string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                return "语言：只用简体中文回答（代码、命令、路径、报错原文可以保持原样）。";
            }

            if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            {
                return "语言：只用英文回答（代码、命令、路径、报错原文可以保持原样）。";
            }

            // 语言列表现在是整张 Windows 语言表，选了什么就用什么回答，不再只有中英两档
            if (!string.IsNullOrWhiteSpace(language)
                && !language.Equals(LanguageCatalog.AutoTag, StringComparison.OrdinalIgnoreCase))
            {
                string name = LanguageCatalog.NameFor(language);
                return "语言：只用 " + name + " 回答（代码、命令、路径、报错原文可以保持原样）。";
            }

            return persona
                ? "语言：主人用什么语言，就用什么语言回答，世界上任何语言都要接得住——中文、英文、日文、"
                  + "韩文、法文、德文、西班牙文、俄文、阿拉伯文、印地文等等，不许说「本小姐只会中文」，"
                  + "主人换语言就立刻跟着换；代码、命令、路径、报错原文保持原样。"
                : "语言：用主人所用的语言回答，任何语言都要接得住。";
        }

        /// <summary>把系统提示词拼出来：人设 + 能力说明 + 规矩，权限不同说法也不同。</summary>
        public static string BuildSystemPrompt(
            string basePrompt, string workspace, bool agentOn, int steps, string scope, bool deep, string language)
        {
            string trimmed = (basePrompt ?? string.Empty).Trim();

            // 鲸鱼娘人设本身就是中文的，界面语言不能反过来压过它，所以语言规则只是兜底
            bool persona = trimmed.Contains("CETACEA_LOLI", StringComparison.Ordinal);
            string languageRules = LanguageRuleFor(language, persona);

            if (!agentOn)
            {
                return (trimmed.Length > 0 ? trimmed : "You are a concise, helpful assistant.")
                       + "\n\n" + languageRules;
            }

            // 没挑工作区照样能聊天：只是这一轮手里没有文件工具，需要动文件时就请主人先选
            // 工作区。注意：PowerShell 命令不是沙箱，即使 cwd 在工作区中也能访问其他位置。
            bool hasWorkspace = !string.IsNullOrWhiteSpace(workspace);

            List<string> parts = new List<string>();
            if (trimmed.Length > 0)
            {
                parts.Add(trimmed);
            }

            List<string> rules = new List<string>
            {
                "You are DomeOS Team AI Agent, an autonomous coding and operations agent running on the user's Windows machine.",
                hasWorkspace
                    ? "Your workspace root is: " + workspace
                    : "No workspace folder has been chosen yet. That is perfectly fine: chat, answer questions, "
                      + "reason and research as usual, and you still have the web tools. You simply have no file or "
                      + "shell tools this turn, so never pretend to have read, written or deleted anything. As soon "
                      + "as the request needs files or a command, stop and ask the user (in their own language) to "
                      + "pick a workspace folder in the 「工作区」 page first; then wait for their next message.",
                hasWorkspace
                    ? "You have tools to read, write, move and delete files, search a codebase, run shell commands, "
                      + "search the internet and read web pages."
                    : "The only tools you have this turn are the web ones: search the internet and read web pages.",
                hasWorkspace
                    ? (scope == "machine"
                        ? "PERMISSION: whole-computer access. Any absolute path on any drive is allowed, not just the "
                          + "workspace: Windows system folders, other user profiles, other drives, the registry through "
                          + "run_command - all reachable. Be careful, a wrong delete_path or run_command here is permanent."
                        : "PERMISSION: file tools are restricted to the workspace root. IMPORTANT: run_command starts "
                          + "an ordinary PowerShell process, not a sandbox; commands can access files and resources "
                          + "outside the workspace with the user's Windows permissions. Never claim shell commands are "
                          + "confined to the workspace, and get explicit user approval before each command.")
                    : "PERMISSION: no file access at all this turn.",
                string.Empty,
                "Rules:",
            };

            if (deep)
            {
                rules.Add("0. Think it through first: work out the plan, the files involved and the failure modes");
                rules.Add("   before you touch anything. State the conclusion, not the deliberation.");
            }

            if (hasWorkspace)
            {
                rules.Add("1. Inspect before you change: list the folder or read the file first, then edit.");
                rules.Add("2. Prefer replace_in_file over write_file when only part of a file changes.");
                rules.Add(scope == "machine"
                    ? "3. Use absolute paths (C:\\Users\\..., D:\\...) for anything outside the workspace."
                    : "3. Every path you pass to a file tool is relative to the workspace root. This does not sandbox run_command.");
                rules.Add("4. Run the tests or the build after changing code, and fix what breaks.");
            }

            rules.Add(hasWorkspace ? "5. A short tool summary is enough; never paste a whole file back to the user."
                                   : "5. Keep answers short and useful; never invent facts or tool output.");
            rules.Add(hasWorkspace
                ? "6. Never invent file contents or command output - use the tools."
                : "6. Never invent file contents or command output - you have no tools for them right now.");
            rules.Add("7. You may use at most " + steps + " tool rounds for this turn, so be efficient.");
            rules.Add("8. When the task is finished, answer with a short summary and stop calling tools.");
            rules.Add("9. You are online: for anything current - news, prices, versions, documentation, a");
            rules.Add("   library you have not seen - call web_search first, then fetch_url on the best result.");
            rules.Add("   Never answer such a question from memory and never say you cannot browse.");
            rules.Add("10. You run on a real Windows machine with the user's rights: launching an application,");
            rules.Add("    opening a file or driving the shell is within your reach when a workspace is chosen.");
            rules.Add("    Do not claim something is impossible before you have tried the obvious command.");

            parts.Add(string.Join("\n", rules));
            parts.Add(languageRules);

            return string.Join("\n\n", parts);
        }

        /// <summary>把开头的 think 块摘掉，对话里只留能读的部分。</summary>
        public static (string Thinking, string Answer) SplitThinking(string text)
        {
            Match match = Regex.Match(text.TrimStart(), "^<(think|thinking)>([\\s\\S]*?)</\\1>", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return (string.Empty, text);
            }

            string answer = text.TrimStart().Substring(match.Length).TrimStart();
            return (match.Groups[2].Value.Trim(), answer);
        }

        /// <summary>把存档里的消息转成线路上的消息。</summary>
        public static List<WireMessage> BuildHistory(IEnumerable<ChatMessage> messages)
        {
            List<WireMessage> wire = new List<WireMessage>();
            foreach (ChatMessage message in messages)
            {
                wire.Add(new WireMessage
                {
                    Role = message.Role == "user" ? "user" : "assistant",
                    Content = message.Text ?? string.Empty,
                });
            }

            return wire;
        }

        public static async Task<AgentOutcome> RunAsync(
            ProviderProfile provider,
            string systemPrompt,
            List<WireMessage> history,
            AgentSettings settings,
            string workspace,
            bool aiController,
            AgentHooks hooks,
            CancellationToken token)
        {
            // 没有工作区时不挂文件类工具：只聊天不该被迫选文件夹，真动文件时再让主人去挑
            bool hasWorkspace = !string.IsNullOrWhiteSpace(workspace);

            List<ToolSpec> specs = settings.Enabled
                ? Toolbox.Available(settings.Scope, settings.WebSearch, aiController, hasWorkspace)
                : new List<ToolSpec>();

            ToolContext context = new ToolContext
            {
                Workspace = workspace,
                Scope = settings.Scope,
                MaxOutput = settings.MaxOutput,
                ShellTimeoutMs = settings.ShellTimeoutMs,
                AiController = aiController,
            };

            List<WireMessage> wire = new List<WireMessage>
            {
                new WireMessage { Role = "system", Content = systemPrompt },
            };

            wire.AddRange(history);

            string finalText = string.Empty;
            string lastHint = string.Empty;
            int stepLimit = settings.MaxSteps > 0 ? settings.MaxSteps : HardStepCap;
            int step = 0;

            for (; step < stepLimit; step++)
            {
                if (token.IsCancellationRequested)
                {
                    return new AgentOutcome { Text = finalText, Ok = true, Steps = step };
                }

                StreamResult result = await ChatApi.StreamAsync(
                    provider, wire, specs, hooks.Delta, hooks.Status, token);

                if (!result.Ok)
                {
                    AgentOutcome failed = new AgentOutcome
                    {
                        Text = finalText,
                        Ok = false,
                        Hint = result.Hint,
                        Steps = step + 1,
                    };

                    return failed;
                }

                (string thinking, string answer) = SplitThinking(result.Text);
                if (thinking.Length > 0)
                {
                    hooks.Thinking?.Invoke(thinking);
                }

                if (answer.Length > 0)
                {
                    finalText = finalText.Length > 0 ? finalText + StepSeparator + answer : answer;
                }

                List<ToolCall> calls = new List<ToolCall>();
                foreach (ToolCall call in result.ToolCalls)
                {
                    if (call.Name.Length > 0)
                    {
                        calls.Add(call);
                    }
                }

                if (calls.Count == 0)
                {
                    return new AgentOutcome
                    {
                        Text = finalText.Length > 0 ? finalText : result.Text,
                        Ok = true,
                        Steps = step + 1,
                    };
                }

                WireMessage assistant = new WireMessage { Role = "assistant", Content = result.Text };
                foreach (ToolCall call in calls)
                {
                    assistant.ToolCalls.Add(call);
                }

                wire.Add(assistant);

                foreach (ToolCall call in calls)
                {
                    if (token.IsCancellationRequested)
                    {
                        return new AgentOutcome { Text = finalText, Ok = true, Steps = step + 1 };
                    }

                    ToolSpec? spec = null;
                    foreach (ToolSpec candidate in specs)
                    {
                        if (candidate.Name == call.Name)
                        {
                            spec = candidate;
                            break;
                        }
                    }

                    string title = spec?.Title ?? call.Name;
                    bool dangerous = spec?.Dangerous ?? true;
                    // PowerShell is not restricted to its working directory. Always ask before running
                    // commands, even when the general auto-approve option is enabled.
                    bool needsAsk = settings.Enabled && dangerous
                                    && (!settings.AutoApprove || call.Name == "run_command");

                    if (needsAsk)
                    {
                        bool allowed = hooks.AskApproval != null && await hooks.AskApproval(call, title);
                        if (!allowed)
                        {
                            hooks.ToolDeclined?.Invoke(call, title);
                            wire.Add(new WireMessage
                            {
                                Role = "tool",
                                ToolCallId = call.Id,
                                Name = call.Name,
                                Content = "The user declined to run \"" + call.Name
                                          + "\". Ask before trying again, or continue without it.",
                            });

                            continue;
                        }
                    }

                    hooks.ToolStarted?.Invoke(call, spec, needsAsk);

                    Stopwatch watch = Stopwatch.StartNew();
                    ToolOutcome outcome = await Toolbox.ExecuteAsync(context, call, token);
                    watch.Stop();

                    hooks.ToolFinished?.Invoke(call, outcome.Summary, outcome.Ok, watch.ElapsedMilliseconds);

                    wire.Add(new WireMessage
                    {
                        Role = "tool",
                        ToolCallId = call.Id,
                        Name = call.Name,
                        Content = outcome.Text,
                    });

                    if (!outcome.Ok && outcome.Summary.Contains("在工作区", StringComparison.Ordinal))
                    {
                        lastHint = "工作区之外的路径被拒绝了：「" + call.Text("path") + "」。"
                                   + "需要的话把权限切成整台电脑。";
                    }
                }
            }

            hooks.Thinking?.Invoke("已经用完 " + stepLimit + " 轮工具调用，先停在这里。");

            return new AgentOutcome
            {
                Text = finalText,
                Ok = true,
                Hint = lastHint,
                Steps = step,
            };
        }
    }
}
