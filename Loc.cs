using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DomeOSAgentDev
{
    /// <summary>
    /// 界面的运行时翻译。XAML 里写的是中文原文，这里拿原文当键查表；
    /// 选中语言后整棵可视树走一遍，能翻的都换掉，不用重启。
    /// 没收录的字符串保持原文（英文表里收录得最全，其余语言查不到就落到英文）。
    /// </summary>
    public static class Loc
    {
        /// <summary>当前语言标签（就是 config 里存的 BCP-47）。</summary>
        public static string CurrentTag { get; private set; } = "auto";

        /// <summary>切到某个语言，之后 T() 和 ApplyTo() 都按它来。</summary>
        public static void Use(string tag)
        {
            CurrentTag = string.IsNullOrWhiteSpace(tag) ? "auto" : tag.Trim();
        }

        public static string T(string chinese)
        {
            if (string.IsNullOrEmpty(chinese))
            {
                return chinese;
            }

            string code = Normalize(CurrentTag);
            if (code == "zh")
            {
                return chinese;
            }

            Dictionary<string, string> table = Tables[code];
            if (table.TryGetValue(chinese, out string? text))
            {
                return text;
            }

            // 俄语 / 乌克兰语 / 日语 / 韩语的长说明还没全翻，缺句落到英文，别让界面突然冒出中文
            if ((code == "ru" || code == "uk" || code == "ja" || code == "ko")
                && Tables["en"].TryGetValue(chinese, out string? english))
            {
                return english;
            }

            return chinese;
        }

        /// <summary>把语言标签归到一个翻译表：zh-CN/zh-SG 用原文，zh-TW/HK 用繁体，其余按语种。</summary>
        private static string Normalize(string tag)
        {
            string t = (tag ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
            if (t.Length == 0 || t == "auto")
            {
                return "zh";
            }

            if (t.StartsWith("zh", StringComparison.Ordinal))
            {
                // 先认脚本：zh-Hans-HK 这种「简体 + 香港」还是简体，不能因为地区就翻成繁体
                if (t.Contains("hans"))
                {
                    return "zh";
                }

                return t.Contains("hant") || t.EndsWith("-tw") || t.EndsWith("-hk") || t.EndsWith("-mo")
                    ? "zh-Hant"
                    : "zh";
            }

            if (t.StartsWith("en", StringComparison.Ordinal)) return "en";
            if (t.StartsWith("ja", StringComparison.Ordinal)) return "ja";
            if (t.StartsWith("ko", StringComparison.Ordinal)) return "ko";
            if (t.StartsWith("ru", StringComparison.Ordinal)) return "ru";
            if (t.StartsWith("uk", StringComparison.Ordinal)) return "uk";
            if (t.StartsWith("fr", StringComparison.Ordinal)) return "fr";
            if (t.StartsWith("de", StringComparison.Ordinal)) return "de";
            if (t.StartsWith("es", StringComparison.Ordinal)) return "es";

            // 还没配翻译表的语种先落到英文，界面不会整片留着中文
            return "en";
        }

        // ------------------------------------------------------------------ 可视树

        private sealed class Slot
        {
            public string Original = "";
            public string Applied = "";
        }

        private sealed class Originals
        {
            public Dictionary<string, Slot> Values { get; } = new Dictionary<string, Slot>();
        }

        private static readonly ConditionalWeakTable<DependencyObject, Originals> Saved =
            new ConditionalWeakTable<DependencyObject, Originals>();

        /// <summary>从根往下把文字都换一遍。可以反复调用；已经翻过的不重复处理。</summary>
        public static void ApplyTo(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            LocalizeNode(root);

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                ApplyTo(VisualTreeHelper.GetChild(root, i));
            }
        }

        private static void LocalizeNode(DependencyObject node)
        {
            switch (node)
            {
                case ComboBoxItem item when item.Content is string content:
                    Swap(item, "Content", content, v => item.Content = v);
                    break;

                case NavigationViewItem nav when nav.Content is string content:
                    Swap(nav, "Content", content, v => nav.Content = v);
                    break;

                case Expander expander when expander.Header is string header:
                    Swap(expander, "Header", header, v => expander.Header = v);
                    break;

                case ToggleSwitch toggle:
                    if (toggle.OnContent is string on)
                    {
                        Swap(toggle, "OnContent", on, v => toggle.OnContent = v);
                    }

                    if (toggle.OffContent is string off)
                    {
                        Swap(toggle, "OffContent", off, v => toggle.OffContent = v);
                    }

                    break;

                case ToggleButton toggleButton when toggleButton.Content is string content:
                    Swap(toggleButton, "Content", content, v => toggleButton.Content = v);
                    break;

                case HyperlinkButton link when link.Content is string content:
                    Swap(link, "Content", content, v => link.Content = v);
                    break;

                case Button button when button.Content is string content:
                    Swap(button, "Content", content, v => button.Content = v);
                    break;

                case TextBox box:
                    Swap(box, "Placeholder", box.PlaceholderText, v => box.PlaceholderText = v);
                    break;

                case PasswordBox box:
                    Swap(box, "Placeholder", box.PlaceholderText, v => box.PlaceholderText = v);
                    break;

                case ComboBox combo:
                    Swap(combo, "Placeholder", combo.PlaceholderText, v => combo.PlaceholderText = v);

                    // 下拉项不在可视树里，得从 Items 里挨个换，收起状态显示的那一行才会跟着变
                    foreach (object entry in combo.Items)
                    {
                        if (entry is ComboBoxItem comboItem && comboItem.Content is string itemText)
                        {
                            Swap(comboItem, "Content", itemText, v => comboItem.Content = v);
                        }
                    }

                    break;

                case TextBlock text when !string.IsNullOrEmpty(text.Text):
                    Swap(text, "Text", text.Text, v => text.Text = v);
                    break;
            }
        }

        /// <summary>
        /// 记住原文和上次翻出来的文字。快速连续切语言时，永远拿最初的原文去查新表，
        /// 不能把上一轮的译文当成原文，否则来回切几次就各翻各的了。
        /// </summary>
        private static void Swap(DependencyObject node, string slot, string current, Action<string> apply)
        {
            if (string.IsNullOrEmpty(current))
            {
                return;
            }

            Originals originals = Saved.GetOrCreateValue(node);
            if (!originals.Values.TryGetValue(slot, out Slot? state))
            {
                state = new Slot { Original = current, Applied = current };
                originals.Values[slot] = state;
            }

            if (string.Equals(current, state.Applied, StringComparison.Ordinal)
                || string.Equals(current, state.Original, StringComparison.Ordinal))
            {
                string next = T(state.Original);
                if (!string.Equals(current, next, StringComparison.Ordinal))
                {
                    apply(next);
                    state.Applied = next;
                }

                return;
            }

            // 代码自己改过这一格（例如状态提示）：把新文字当成原文重来一次
            state.Original = current;
            string translated = T(current);
            state.Applied = translated;
            if (!string.Equals(current, translated, StringComparison.Ordinal))
            {
                apply(translated);
            }
        }

        // ------------------------------------------------------------------ 翻译表

        private static Dictionary<string, Dictionary<string, string>>? _tables;

        // 放在属性里懒建：静态字段是按书写顺序初始化的，直接写成字段会拿到后面那些表的 null
        private static Dictionary<string, Dictionary<string, string>> Tables =>
            _tables ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = English,
                ["zh-Hant"] = Traditional,
                ["ja"] = Japanese,
                ["ko"] = Korean,
                ["ru"] = Russian,
                ["uk"] = Ukrainian,
                ["fr"] = French,
                ["de"] = German,
                ["es"] = Spanish,
            };

        // ---- 英文：XAML 里所有静态文案 + 向导里代码写的那几句

        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            ["语言"] = "Language",
            ["API Key"] = "API Key",
            ["语音"] = "Voice",
            ["外观"] = "Appearance",
            ["开发版"] = "Dev build",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "Before we set up DomeOS AI, choose the language you would like to use.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "I will answer in the language you pick; Auto follows the language you type in.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "Enter your provider key. Pick Custom API to connect any OpenAI-compatible endpoint.",
            ["服务商"] = "Provider",
            ["接口地址"] = "Endpoint",
            ["模型"] = "Model",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "The key is stored only in your local config and is never uploaded.",
            ["语音系统"] = "Voice",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "Pick a reading voice. MiMo needs a key; Microsoft voice and Virtual singer use local voices.",
            ["语音引擎"] = "Voice engine",
            ["MiMo API Key（可选）"] = "MiMo API Key (optional)",
            ["试听"] = "Preview",
            ["自动朗读回复"] = "Auto-read replies",
            ["外观设置"] = "Appearance",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "Finally, pick a theme and background. You can change them anytime in Settings.",
            ["主题"] = "Theme",
            ["背景"] = "Background",
            ["跟随系统"] = "Follow system",
            ["浅色"] = "Light",
            ["深色"] = "Dark",
            ["云母"] = "Mica",
            ["亚克力"] = "Acrylic",
            ["纯色"] = "Solid color",
            ["图片"] = "Image",
            ["上一步"] = "Back",
            ["下一步"] = "Next",
            ["完成"] = "Finish",
            ["跳过引导"] = "Skip setup",
            ["自动（跟随主人的语言）"] = "Auto (follow my language)",
            ["自动（MiMo 优先，系统语音兜底）"] = "Auto (MiMo first, system voice fallback)",
            ["仅 MiMo TTS"] = "MiMo TTS only",
            ["仅 Microsoft 语音"] = "Microsoft voice only",
            ["虚拟歌姬"] = "Virtual singer",
            ["对话"] = "Chat",
            ["工作区"] = "Workspace",
            ["最近对话"] = "Recent chats",
            ["设置"] = "Settings",
            ["新对话"] = "New chat",
            ["只看收藏"] = "Favorites only",
            ["点一张卡片打开对话"] = "Click a card to open a chat",
            ["发送"] = "Send",
            ["清除"] = "Clear",
            ["收藏"] = "Favorite",
            ["删除"] = "Delete",
            ["删除对话"] = "Delete chat",
            ["未配置服务商"] = "No provider",
            ["未选择模型"] = "No model",
            ["当前服务商"] = "Current provider",
            ["角色"] = "Persona",
            ["权限"] = "Permissions",
            ["通用"] = "General",
            ["关于"] = "About",
            ["返回"] = "Back",
            ["选择文件夹"] = "Choose folder",
            ["未选择工作区"] = "No workspace",
            ["关闭"] = "Off",
            ["开启"] = "On",
            ["自动"] = "Auto",
            ["自定义"] = "Custom",
            ["名称"] = "Name",
            ["刷新"] = "Refresh",
            ["添加"] = "Add",
            ["设为当前"] = "Set as current",
            ["应用到当前服务商"] = "Apply to current provider",
            ["保存这家服务商"] = "Save this provider",
            ["保存语音设置"] = "Save voice settings",
            ["温度 / 最大回复"] = "Temperature / Max reply",
            ["朗读引擎"] = "Reading engine",
            ["自动朗读"] = "Auto-read",
            ["自动批准"] = "Auto-approve",
            ["联网搜索"] = "Web search",
            ["深度思考"] = "Deep thinking",
            ["桌面通知"] = "Desktop notifications",
            ["开机自启"] = "Start with Windows",
            ["应用主题"] = "Theme",
            ["侧栏宽度"] = "Sidebar width",
            ["保留的历史轮数"] = "Conversation turns to keep",
            ["隐私政策"] = "Privacy policy",
            ["用户协议"] = "Terms of service",
            ["开源声明"] = "Open-source notices",
            ["鸣谢"] = "Credits",
            ["添加其他服务商"] = "Add another provider",
            ["＋ 添加自定义 API"] = "+ Add custom API",
            ["挑一个预设加入…"] = "Pick a preset to add…",
            ["挑一个，或直接输入"] = "Pick one, or type your own",
            ["协议"] = "Protocol",
            ["OpenAI 兼容"] = "OpenAI-compatible",
            ["内置 39 家预设，挑一家加入后，接口地址、模型都可以按自己的端点改。"] =
                "39 built-in presets. After adding one, its endpoint and model can be edited.",
            ["0 表示最大回复不限；密钥只存在本机。"] = "0 means no reply limit; keys stay on this PC.",
            ["这段文字作为系统提示词，随每一轮对话发给当前服务商。"] =
                "This text is the system prompt sent to the current provider on every turn.",
            ["恢复默认人设"] = "Restore default persona",
            ["外观设置"] = "Appearance",
            ["应用主题"] = "Theme",
            ["跟随系统，或固定浅色 / 深色。"] = "Follow the system, or force light or dark.",
            ["界面与回答的默认语言。"] = "Default language for the UI and replies.",
            ["侧栏宽度"] = "Sidebar width",
            ["NavigationView 展开时的宽度（像素）。"] = "Width of the expanded NavigationView (px).",
            ["保留的历史轮数"] = "Conversation turns to keep",
            ["每次请求带上的上文轮数。"] = "Turns of context sent with each request.",
            ["任务结束时提醒一声。"] = "Notify me when a task finishes.",
            ["登录后自动把本小姐叫起来。"] = "Start me automatically after sign-in.",
            ["自动会先试 MiMo，失败或没钥匙时改用系统讲述人的语音。"] =
                "Tries MiMo first; falls back to the system narrator if it fails or no key is set.",
            ["每条回答出来后直接读一遍。"] = "Read every answer as soon as it arrives.",
            ["语速"] = "Speed",
            ["-10 到 10，0 是正常语速。"] = "-10 to 10; 0 is normal speed.",
            ["歌姬预设"] = "Singer preset",
            ["洛天依（近似）"] = "Luo Tianyi (approximate)",
            ["初音未来（近似）"] = "Hatsune Miku (approximate)",
            ["洛天依、初音未来是本机声库配音高的近似音色；装了真声库就在下面直接选。"] =
                "Luo Tianyi and Hatsune Miku are approximated with local voices and pitch; if real voicebanks are installed, pick one below.",
            ["声库"] = "Voicebank",
            ["跟随系统默认声库"] = "Use the system default voice",
            ["音高"] = "Pitch",
            ["歌姬语速"] = "Singer speed",
            ["配音软件"] = "Dubbing app",
            ["接上 X Studio 这类外部歌姬软件。它没有公开接口，朗读时会把文字复制好并启动它。"] =
                "Connect external singing software such as X Studio. It has no public API, so reading copies the text and launches it.",
            ["内置（近似音色）"] = "Built-in (approximate voice)",
            ["自定义程序…"] = "Custom program…",
            ["程序路径"] = "Program path",
            ["用 {file} 传入临时文本文件。"] = "Use {file} to pass a temporary text file.",
            ["浏览…"] = "Browse…",
            ["选一个本机语音"] = "Choose a local voice",
            ["背景"] = "Background",
            ["云母和亚克力是系统的两种材料；纯色和图片由本软件自己铺一层。"] =
                "Mica and Acrylic are system materials; Solid and Image are drawn by this app.",
            ["显示方式"] = "Display mode",
            ["铺满"] = "Fill",
            ["适应"] = "Fit",
            ["底色"] = "Base color",
            ["留空则使用默认颜色"] = "Leave empty for the default color",
            ["纯色模式下铺满整窗的颜色。"] = "Color that fills the window in Solid mode.",
            ["选一张图片…"] = "Choose an image…",
            ["压暗"] = "Dim",
            ["图片模式下把背景压暗一点，字才看得清。"] = "Dim the image a little so text stays readable.",
            ["显示 Agent Orb"] = "Show Agent Orb",
            ["思考与执行工具时这个球体会加速呼吸；空闲时缓慢起伏。"] =
                "The orb breathes faster while thinking or running tools, and slowly when idle.",
            ["改动会立即生效，拖动球体可以把它放到任何位置。"] =
                "Changes apply immediately; drag the orb anywhere.",
            ["主窗口尺寸 34px"] = "Main window 34px",
            ["指令栏尺寸 96px"] = "Command bar 96px",
            ["主色 A"] = "Accent A",
            ["主色 B"] = "Accent B",
            ["整个电脑（所有磁盘）"] = "Whole PC (all drives)",
            ["仅当前工作区文件夹"] = "Current workspace folder only",
            ["危险操作（删除、覆盖、装东西、跑命令）"] = "Risky actions (delete, overwrite, install, run commands)",
            ["每次询问"] = "Ask every time",
            ["可操作的目录"] = "Folders the agent may touch",
            ["选择一个文件夹作为 Agent 的工作目录"] = "Choose a folder as the Agent's working directory",
            ["展开文件夹查看内容，点文件可看完整路径。"] =
                "Expand a folder to view its contents; click a file to see its full path.",
            ["这里没什么好看的。"] = "Nothing to see here.",
            ["回车发送 · Shift+回车 换行 · Ctrl+L 清屏"] =
                "Enter to send · Shift+Enter for a new line · Ctrl+L to clear",
            ["所见即所想，所想即所求"] = "See it, think it, make it real.",
            ["按 空格 / 点击 重新开始"] = "Press Space / click to restart",
            ["空格 / ↑ 跳跃 · ↓ 下蹲"] = "Space / ↑ jump · ↓ duck",
            ["工作区 · 自动批准"] = "Workspace · Auto-approve",
            ["隐藏"] = "Hide",
            ["显示"] = "Show",
            ["保存"] = "Save",
            ["未设置"] = "Not set",
            ["选一张图片…"] = "Choose an image…",
        };

        // ---- 以下非英语语言覆盖向导和主界面最常看到的那些；缺的会落到英文表

        private static readonly Dictionary<string, string> Traditional = new Dictionary<string, string>
        {
            ["语言"] = "語言",
            ["语音"] = "語音",
            ["外观"] = "外觀",
            ["开发版"] = "開發版",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "在我們開始設定 DomeOS AI 之前，請選擇您希望使用的語言。",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "選了哪種語言，本小姐就用哪種語言回答；「自動」會跟著您輸入的語言走。",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "填入服務商的密鑰。也可以選「Custom API」，接任何 OpenAI 相容的介面。",
            ["服务商"] = "服務商",
            ["接口地址"] = "介面位址",
            ["模型"] = "模型",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "密鑰只存在本機的設定裡，不會上傳到別處。",
            ["语音系统"] = "語音系統",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "挑一個朗讀的嗓子。MiMo 需要填 Key，Microsoft 語音和虛擬歌姬都用本機聲庫。",
            ["语音引擎"] = "語音引擎",
            ["MiMo API Key（可选）"] = "MiMo API Key（可選）",
            ["试听"] = "試聽",
            ["自动朗读回复"] = "自動朗讀回覆",
            ["外观设置"] = "外觀設定",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "最後挑一下主題和背景。這些之後隨時能在「設定」裡改。",
            ["主题"] = "主題",
            ["背景"] = "背景",
            ["跟随系统"] = "跟隨系統",
            ["浅色"] = "淺色",
            ["深色"] = "深色",
            ["云母"] = "雲母",
            ["亚克力"] = "亞克力",
            ["纯色"] = "純色",
            ["图片"] = "圖片",
            ["上一步"] = "上一步",
            ["下一步"] = "下一步",
            ["完成"] = "完成",
            ["跳过引导"] = "跳過引導",
            ["自动（跟随主人的语言）"] = "自動（跟隨主人的語言）",
            ["自动（MiMo 优先，系统语音兜底）"] = "自動（MiMo 優先，系統語音兜底）",
            ["仅 MiMo TTS"] = "僅 MiMo TTS",
            ["仅 Microsoft 语音"] = "僅 Microsoft 語音",
            ["虚拟歌姬"] = "虛擬歌姬",
            ["对话"] = "對話",
            ["工作区"] = "工作區",
            ["最近对话"] = "最近對話",
            ["设置"] = "設定",
            ["新对话"] = "新對話",
            ["只看收藏"] = "只看收藏",
            ["点一张卡片打开对话"] = "點一張卡片開啟對話",
            ["发送"] = "傳送",
            ["清除"] = "清除",
            ["收藏"] = "收藏",
            ["删除"] = "刪除",
            ["删除对话"] = "刪除對話",
            ["未配置服务商"] = "未設定服務商",
            ["未选择模型"] = "未選擇模型",
            ["当前服务商"] = "目前服務商",
            ["角色"] = "角色",
            ["权限"] = "權限",
            ["通用"] = "通用",
            ["关于"] = "關於",
            ["返回"] = "返回",
            ["选择文件夹"] = "選擇資料夾",
            ["未选择工作区"] = "未選擇工作區",
            ["关闭"] = "關閉",
            ["开启"] = "開啟",
            ["自动"] = "自動",
            ["自定义"] = "自訂",
            ["名称"] = "名稱",
            ["刷新"] = "重新整理",
            ["添加"] = "新增",
            ["设为当前"] = "設為目前",
            ["应用到当前服务商"] = "套用到目前服務商",
            ["保存这家服务商"] = "儲存這家服務商",
            ["保存语音设置"] = "儲存語音設定",
            ["温度 / 最大回复"] = "溫度 / 最大回覆",
            ["朗读引擎"] = "朗讀引擎",
            ["自动朗读"] = "自動朗讀",
            ["自动批准"] = "自動批准",
            ["联网搜索"] = "聯網搜尋",
            ["深度思考"] = "深度思考",
            ["桌面通知"] = "桌面通知",
            ["开机自启"] = "開機自啟",
            ["应用主题"] = "套用主題",
            ["侧栏宽度"] = "側欄寬度",
            ["保留的历史轮数"] = "保留的歷史輪數",
            ["隐私政策"] = "隱私政策",
            ["用户协议"] = "使用者協議",
            ["开源声明"] = "開源聲明",
            ["鸣谢"] = "鳴謝",
        };

        private static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>
        {
            ["语言"] = "言語",
            ["语音"] = "音声",
            ["外观"] = "外観",
            ["开发版"] = "開発版",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "DomeOS AI のセットアップを始める前に、使用する言語を選んでください。",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "選んだ言語で答えるよ。「自動」なら入力した言語に合わせる。",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "プロバイダーのキーを入力。「Custom API」で OpenAI 互換の任意のエンドポイントに接続できます。",
            ["服务商"] = "プロバイダー",
            ["接口地址"] = "エンドポイント",
            ["模型"] = "モデル",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "キーはこの PC の設定にだけ保存され、外部には送信されません。",
            ["语音系统"] = "音声",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "読み上げ音声を選択。MiMo はキーが必要で、Microsoft 音声と仮想歌姫はローカル音声を使います。",
            ["语音引擎"] = "音声エンジン",
            ["MiMo API Key（可选）"] = "MiMo API Key（任意）",
            ["试听"] = "試聴",
            ["自动朗读回复"] = "返答を自動読み上げ",
            ["外观设置"] = "外観",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "最後にテーマと背景を選んでください。あとで「設定」からいつでも変更できます。",
            ["主题"] = "テーマ",
            ["背景"] = "背景",
            ["跟随系统"] = "システムに従う",
            ["浅色"] = "ライト",
            ["深色"] = "ダーク",
            ["云母"] = "Mica",
            ["亚克力"] = "アクリル",
            ["纯色"] = "単色",
            ["图片"] = "画像",
            ["上一步"] = "戻る",
            ["下一步"] = "次へ",
            ["完成"] = "完了",
            ["跳过引导"] = "セットアップをスキップ",
            ["自动（跟随主人的语言）"] = "自動（使用言語に合わせる）",
            ["自动（MiMo 优先，系统语音兜底）"] = "自動（MiMo 優先、システム音声フォールバック）",
            ["仅 MiMo TTS"] = "MiMo TTS のみ",
            ["仅 Microsoft 语音"] = "Microsoft 音声のみ",
            ["虚拟歌姬"] = "仮想歌姫",
            ["对话"] = "チャット",
            ["工作区"] = "ワークスペース",
            ["最近对话"] = "最近のチャット",
            ["设置"] = "設定",
            ["新对话"] = "新しいチャット",
            ["只看收藏"] = "お気に入りのみ",
            ["点一张卡片打开对话"] = "カードをクリックしてチャットを開く",
            ["发送"] = "送信",
            ["清除"] = "クリア",
            ["收藏"] = "お気に入り",
            ["删除"] = "削除",
            ["删除对话"] = "チャットを削除",
            ["未配置服务商"] = "プロバイダー未設定",
            ["未选择模型"] = "モデル未選択",
            ["当前服务商"] = "現在のプロバイダー",
            ["角色"] = "キャラクター",
            ["权限"] = "権限",
            ["通用"] = "一般",
            ["关于"] = "情報",
            ["返回"] = "戻る",
            ["选择文件夹"] = "フォルダーを選択",
            ["未选择工作区"] = "ワークスペース未選択",
            ["关闭"] = "オフ",
            ["开启"] = "オン",
            ["自动"] = "自動",
            ["自定义"] = "カスタム",
            ["名称"] = "名前",
            ["刷新"] = "更新",
            ["添加"] = "追加",
            ["设为当前"] = "現在の設定にする",
            ["应用到当前服务商"] = "現在のプロバイダーに適用",
            ["保存这家服务商"] = "このプロバイダーを保存",
            ["保存语音设置"] = "音声設定を保存",
            ["温度 / 最大回复"] = "温度 / 最大返答",
            ["朗读引擎"] = "読み上げエンジン",
            ["自动朗读"] = "自動読み上げ",
            ["自动批准"] = "自動承認",
            ["联网搜索"] = "ウェブ検索",
            ["深度思考"] = "深く考える",
            ["桌面通知"] = "デスクトップ通知",
            ["开机自启"] = "Windows 起動時に開始",
            ["应用主题"] = "テーマ",
            ["侧栏宽度"] = "サイドバー幅",
            ["保留的历史轮数"] = "保持する会話ターン数",
            ["隐私政策"] = "プライバシーポリシー",
            ["用户协议"] = "利用規約",
            ["开源声明"] = "オープンソース表記",
            ["鸣谢"] = "クレジット",
        };

        private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
        {
            ["语言"] = "언어",
            ["语音"] = "음성",
            ["外观"] = "모양",
            ["开发版"] = "개발판",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "DomeOS AI 설정을 시작하기 전에 사용할 언어를 선택하세요.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "선택한 언어로 답할게요. '자동'은 입력한 언어를 따라갑니다.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "공급자 키를 입력하세요. 'Custom API'를 선택하면 OpenAI 호환 엔드포인트를 연결할 수 있습니다.",
            ["服务商"] = "공급자",
            ["接口地址"] = "엔드포인트",
            ["模型"] = "모델",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "키는 이 PC의 설정에만 저장되며 외부로 전송되지 않습니다.",
            ["语音系统"] = "음성",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "읽기 음성을 고르세요. MiMo는 키가 필요하고, Microsoft 음성과 가상 가수는 로컬 음성을 씁니다.",
            ["语音引擎"] = "음성 엔진",
            ["MiMo API Key（可选）"] = "MiMo API Key(선택)",
            ["试听"] = "미리 듣기",
            ["自动朗读回复"] = "답변 자동 읽기",
            ["外观设置"] = "모양",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "마지막으로 테마와 배경을 고르세요. 나중에 '설정'에서 언제든 바꿀 수 있습니다.",
            ["主题"] = "테마",
            ["背景"] = "배경",
            ["跟随系统"] = "시스템 설정",
            ["浅色"] = "밝게",
            ["深色"] = "어둡게",
            ["云母"] = "Mica",
            ["亚克力"] = "아크릴",
            ["纯色"] = "단색",
            ["图片"] = "이미지",
            ["上一步"] = "이전",
            ["下一步"] = "다음",
            ["完成"] = "완료",
            ["跳过引导"] = "설정 건너뛰기",
            ["自动（跟随主人的语言）"] = "자동(입력 언어 따름)",
            ["自动（MiMo 优先，系统语音兜底）"] = "자동(MiMo 우선, 시스템 음성 대체)",
            ["仅 MiMo TTS"] = "MiMo TTS만",
            ["仅 Microsoft 语音"] = "Microsoft 음성만",
            ["虚拟歌姬"] = "가상 가수",
            ["对话"] = "대화",
            ["工作区"] = "워크스페이스",
            ["最近对话"] = "최근 대화",
            ["设置"] = "설정",
            ["新对话"] = "새 대화",
            ["只看收藏"] = "즐겨찾기만",
            ["点一张卡片打开对话"] = "카드를 클릭해 대화 열기",
            ["发送"] = "보내기",
            ["清除"] = "지우기",
            ["收藏"] = "즐겨찾기",
            ["删除"] = "삭제",
            ["删除对话"] = "대화 삭제",
            ["未配置服务商"] = "공급자 없음",
            ["未选择模型"] = "모델 없음",
            ["当前服务商"] = "현재 공급자",
            ["角色"] = "페르소나",
            ["权限"] = "권한",
            ["通用"] = "일반",
            ["关于"] = "정보",
            ["返回"] = "뒤로",
            ["选择文件夹"] = "폴더 선택",
            ["未选择工作区"] = "워크스페이스 없음",
            ["关闭"] = "끔",
            ["开启"] = "켬",
            ["自动"] = "자동",
            ["自定义"] = "사용자 지정",
            ["名称"] = "이름",
            ["刷新"] = "새로 고침",
            ["添加"] = "추가",
            ["设为当前"] = "현재로 설정",
            ["应用到当前服务商"] = "현재 공급자에 적용",
            ["保存这家服务商"] = "이 공급자 저장",
            ["保存语音设置"] = "음성 설정 저장",
            ["温度 / 最大回复"] = "온도 / 최대 답변",
            ["朗读引擎"] = "읽기 엔진",
            ["自动朗读"] = "자동 읽기",
            ["自动批准"] = "자동 승인",
            ["联网搜索"] = "웹 검색",
            ["深度思考"] = "깊이 생각",
            ["桌面通知"] = "데스크톱 알림",
            ["开机自启"] = "Windows 시작 시 실행",
            ["应用主题"] = "테마",
            ["侧栏宽度"] = "사이드바 너비",
            ["保留的历史轮数"] = "보관할 대화 턴",
            ["隐私政策"] = "개인정보 처리방침",
            ["用户协议"] = "이용 약관",
            ["开源声明"] = "오픈소스 고지",
            ["鸣谢"] = "크레딧",
        };

        private static readonly Dictionary<string, string> Russian = new Dictionary<string, string>
        {
            ["语言"] = "Язык",
            ["语音"] = "Голос",
            ["外观"] = "Оформление",
            ["开发版"] = "Dev-сборка",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "Прежде чем настроить DomeOS AI, выберите язык.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "Отвечаю на выбранном языке; «Авто» следует языку ввода.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "Введите ключ провайдера. «Custom API» подключает любой OpenAI-совместимый эндпоинт.",
            ["服务商"] = "Провайдер",
            ["接口地址"] = "Адрес API",
            ["模型"] = "Модель",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "Ключ хранится только в локальном конфиге и никуда не отправляется.",
            ["语音系统"] = "Голос",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "Выберите голос. MiMo требует ключ; Microsoft и Виртуальная певица используют локальные голоса.",
            ["语音引擎"] = "Голосовой движок",
            ["MiMo API Key（可选）"] = "MiMo API Key (необязательно)",
            ["试听"] = "Прослушать",
            ["自动朗读回复"] = "Авточтение ответов",
            ["外观设置"] = "Оформление",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "Осталось выбрать тему и фон. Их можно изменить в настройках в любой момент.",
            ["主题"] = "Тема",
            ["背景"] = "Фон",
            ["跟随系统"] = "Как в системе",
            ["浅色"] = "Светлая",
            ["深色"] = "Тёмная",
            ["云母"] = "Mica",
            ["亚克力"] = "Акрил",
            ["纯色"] = "Цвет",
            ["图片"] = "Картинка",
            ["上一步"] = "Назад",
            ["下一步"] = "Далее",
            ["完成"] = "Готово",
            ["跳过引导"] = "Пропустить",
            ["自动（跟随主人的语言）"] = "Авто (по языку ввода)",
            ["自动（MiMo 优先，系统语音兜底）"] = "Авто (сначала MiMo, затем системный голос)",
            ["仅 MiMo TTS"] = "Только MiMo TTS",
            ["仅 Microsoft 语音"] = "Только Microsoft",
            ["虚拟歌姬"] = "Виртуальная певица",
            ["对话"] = "Чат",
            ["工作区"] = "Рабочая область",
            ["最近对话"] = "Недавние чаты",
            ["设置"] = "Настройки",
            ["新对话"] = "Новый чат",
            ["只看收藏"] = "Только избранное",
            ["点一张卡片打开对话"] = "Нажмите карточку, чтобы открыть чат",
            ["发送"] = "Отправить",
            ["清除"] = "Очистить",
            ["收藏"] = "В избранное",
            ["删除"] = "Удалить",
            ["删除对话"] = "Удалить чат",
            ["未配置服务商"] = "Провайдер не задан",
            ["未选择模型"] = "Модель не выбрана",
            ["当前服务商"] = "Текущий провайдер",
            ["角色"] = "Персона",
            ["权限"] = "Разрешения",
            ["通用"] = "Общие",
            ["关于"] = "О программе",
            ["返回"] = "Назад",
            ["选择文件夹"] = "Выбрать папку",
            ["未选择工作区"] = "Рабочая область не выбрана",
            ["关闭"] = "Выкл",
            ["开启"] = "Вкл",
            ["自动"] = "Авто",
            ["自定义"] = "Своё",
            ["名称"] = "Название",
            ["刷新"] = "Обновить",
            ["添加"] = "Добавить",
            ["设为当前"] = "Сделать текущим",
            ["应用到当前服务商"] = "Применить к текущему",
            ["保存这家服务商"] = "Сохранить провайдера",
            ["保存语音设置"] = "Сохранить голос",
            ["温度 / 最大回复"] = "Температура / макс. ответ",
            ["朗读引擎"] = "Движок чтения",
            ["自动朗读"] = "Авточтение",
            ["自动批准"] = "Автоподтверждение",
            ["联网搜索"] = "Поиск в сети",
            ["深度思考"] = "Глубокое мышление",
            ["桌面通知"] = "Уведомления",
            ["开机自启"] = "Запуск с Windows",
            ["应用主题"] = "Тема",
            ["侧栏宽度"] = "Ширина панели",
            ["保留的历史轮数"] = "Хранить ходов",
            ["隐私政策"] = "Политика конфиденциальности",
            ["用户协议"] = "Условия использования",
            ["开源声明"] = "Открытый код",
            ["鸣谢"] = "Благодарности",
        };

        private static readonly Dictionary<string, string> French = new Dictionary<string, string>
        {
            ["语言"] = "Langue",
            ["语音"] = "Voix",
            ["外观"] = "Apparence",
            ["开发版"] = "Version de dev",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "Avant de configurer DomeOS AI, choisissez la langue à utiliser.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "Je répondrai dans la langue choisie ; « Auto » suit la langue de saisie.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "Saisissez la clé du fournisseur. « Custom API » connecte toute API compatible OpenAI.",
            ["服务商"] = "Fournisseur",
            ["接口地址"] = "Adresse API",
            ["模型"] = "Modèle",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "La clé reste dans la configuration locale et n'est jamais envoyée ailleurs.",
            ["语音系统"] = "Voix",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "Choisissez une voix. MiMo nécessite une clé ; Microsoft et Chanteuse virtuelle utilisent les voix locales.",
            ["语音引擎"] = "Moteur vocal",
            ["MiMo API Key（可选）"] = "Clé API MiMo (facultatif)",
            ["试听"] = "Écouter",
            ["自动朗读回复"] = "Lire les réponses",
            ["外观设置"] = "Apparence",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "Choisissez enfin un thème et un fond. Modifiables à tout moment dans les Paramètres.",
            ["主题"] = "Thème",
            ["背景"] = "Fond",
            ["跟随系统"] = "Suivre le système",
            ["浅色"] = "Clair",
            ["深色"] = "Sombre",
            ["云母"] = "Mica",
            ["亚克力"] = "Acrylique",
            ["纯色"] = "Uni",
            ["图片"] = "Image",
            ["上一步"] = "Précédent",
            ["下一步"] = "Suivant",
            ["完成"] = "Terminer",
            ["跳过引导"] = "Ignorer",
            ["自动（跟随主人的语言）"] = "Auto (langue de saisie)",
            ["自动（MiMo 优先，系统语音兜底）"] = "Auto (MiMo d'abord, voix système ensuite)",
            ["仅 MiMo TTS"] = "MiMo TTS seulement",
            ["仅 Microsoft 语音"] = "Voix Microsoft seulement",
            ["虚拟歌姬"] = "Chanteuse virtuelle",
            ["对话"] = "Discussion",
            ["工作区"] = "Espace de travail",
            ["最近对话"] = "Discussions récentes",
            ["设置"] = "Paramètres",
            ["新对话"] = "Nouvelle discussion",
            ["只看收藏"] = "Favoris seulement",
            ["点一张卡片打开对话"] = "Cliquez sur une carte pour ouvrir",
            ["发送"] = "Envoyer",
            ["清除"] = "Effacer",
            ["收藏"] = "Favori",
            ["删除"] = "Supprimer",
            ["删除对话"] = "Supprimer la discussion",
            ["未配置服务商"] = "Aucun fournisseur",
            ["未选择模型"] = "Aucun modèle",
            ["当前服务商"] = "Fournisseur actuel",
            ["角色"] = "Personnage",
            ["权限"] = "Autorisations",
            ["通用"] = "Général",
            ["关于"] = "À propos",
            ["返回"] = "Retour",
            ["选择文件夹"] = "Choisir un dossier",
            ["未选择工作区"] = "Aucun espace de travail",
            ["关闭"] = "Désactivé",
            ["开启"] = "Activé",
            ["自动"] = "Auto",
            ["自定义"] = "Personnalisé",
            ["名称"] = "Nom",
            ["刷新"] = "Actualiser",
            ["添加"] = "Ajouter",
            ["设为当前"] = "Définir comme actuel",
            ["应用到当前服务商"] = "Appliquer au fournisseur actuel",
            ["保存这家服务商"] = "Enregistrer ce fournisseur",
            ["保存语音设置"] = "Enregistrer la voix",
            ["温度 / 最大回复"] = "Température / réponse max",
            ["朗读引擎"] = "Moteur de lecture",
            ["自动朗读"] = "Lecture auto",
            ["自动批准"] = "Approbation auto",
            ["联网搜索"] = "Recherche web",
            ["深度思考"] = "Réflexion profonde",
            ["桌面通知"] = "Notifications",
            ["开机自启"] = "Lancer avec Windows",
            ["应用主题"] = "Thème",
            ["侧栏宽度"] = "Largeur du panneau",
            ["保留的历史轮数"] = "Tours conservés",
            ["隐私政策"] = "Confidentialité",
            ["用户协议"] = "Conditions",
            ["开源声明"] = "Logiciels libres",
            ["鸣谢"] = "Crédits",
        };

        private static readonly Dictionary<string, string> German = new Dictionary<string, string>
        {
            ["语言"] = "Sprache",
            ["语音"] = "Stimme",
            ["外观"] = "Darstellung",
            ["开发版"] = "Entwicklerversion",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "Bevor wir DomeOS AI einrichten, wähle die gewünschte Sprache.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "Ich antworte in der gewählten Sprache; „Automatisch“ folgt deiner Eingabe.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "Gib den Schlüssel des Anbieters ein. „Custom API“ verbindet beliebige OpenAI-kompatible Endpunkte.",
            ["服务商"] = "Anbieter",
            ["接口地址"] = "Endpunkt",
            ["模型"] = "Modell",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "Der Schlüssel wird nur lokal gespeichert und nie hochgeladen.",
            ["语音系统"] = "Stimme",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "Wähle eine Stimme. MiMo braucht einen Schlüssel; Microsoft und Virtuelle Sängerin nutzen lokale Stimmen.",
            ["语音引擎"] = "Sprachmotor",
            ["MiMo API Key（可选）"] = "MiMo API Key (optional)",
            ["试听"] = "Anhören",
            ["自动朗读回复"] = "Antworten vorlesen",
            ["外观设置"] = "Darstellung",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "Zum Schluss Thema und Hintergrund wählen. Beides ist später in den Einstellungen änderbar.",
            ["主题"] = "Thema",
            ["背景"] = "Hintergrund",
            ["跟随系统"] = "System folgen",
            ["浅色"] = "Hell",
            ["深色"] = "Dunkel",
            ["云母"] = "Mica",
            ["亚克力"] = "Acryl",
            ["纯色"] = "Einfarbig",
            ["图片"] = "Bild",
            ["上一步"] = "Zurück",
            ["下一步"] = "Weiter",
            ["完成"] = "Fertig",
            ["跳过引导"] = "Überspringen",
            ["自动（跟随主人的语言）"] = "Automatisch (Eingabesprache)",
            ["自动（MiMo 优先，系统语音兜底）"] = "Automatisch (MiMo zuerst, Systemstimme als Fallback)",
            ["仅 MiMo TTS"] = "Nur MiMo TTS",
            ["仅 Microsoft 语音"] = "Nur Microsoft",
            ["虚拟歌姬"] = "Virtuelle Sängerin",
            ["对话"] = "Chat",
            ["工作区"] = "Arbeitsbereich",
            ["最近对话"] = "Letzte Chats",
            ["设置"] = "Einstellungen",
            ["新对话"] = "Neuer Chat",
            ["只看收藏"] = "Nur Favoriten",
            ["点一张卡片打开对话"] = "Klicke eine Karte zum Öffnen",
            ["发送"] = "Senden",
            ["清除"] = "Löschen",
            ["收藏"] = "Favorit",
            ["删除"] = "Löschen",
            ["删除对话"] = "Chat löschen",
            ["未配置服务商"] = "Kein Anbieter",
            ["未选择模型"] = "Kein Modell",
            ["当前服务商"] = "Aktueller Anbieter",
            ["角色"] = "Persona",
            ["权限"] = "Berechtigungen",
            ["通用"] = "Allgemein",
            ["关于"] = "Info",
            ["返回"] = "Zurück",
            ["选择文件夹"] = "Ordner wählen",
            ["未选择工作区"] = "Kein Arbeitsbereich",
            ["关闭"] = "Aus",
            ["开启"] = "An",
            ["自动"] = "Automatisch",
            ["自定义"] = "Benutzerdefiniert",
            ["名称"] = "Name",
            ["刷新"] = "Aktualisieren",
            ["添加"] = "Hinzufügen",
            ["设为当前"] = "Als aktuell",
            ["应用到当前服务商"] = "Auf aktuellen anwenden",
            ["保存这家服务商"] = "Anbieter speichern",
            ["保存语音设置"] = "Stimme speichern",
            ["温度 / 最大回复"] = "Temperatur / Max. Antwort",
            ["朗读引擎"] = "Vorlesemotor",
            ["自动朗读"] = "Automatisch vorlesen",
            ["自动批准"] = "Automatisch bestätigen",
            ["联网搜索"] = "Websuche",
            ["深度思考"] = "Tiefes Denken",
            ["桌面通知"] = "Desktop-Benachrichtigungen",
            ["开机自启"] = "Mit Windows starten",
            ["应用主题"] = "Thema",
            ["侧栏宽度"] = "Seitenleiste",
            ["保留的历史轮数"] = "Gespeicherte Runden",
            ["隐私政策"] = "Datenschutz",
            ["用户协议"] = "Nutzungsbedingungen",
            ["开源声明"] = "Open-Source-Hinweise",
            ["鸣谢"] = "Danksagung",
        };

        private static readonly Dictionary<string, string> Ukrainian = new Dictionary<string, string>
        {
            ["语言"] = "Мова",
            ["语音"] = "Голос",
            ["外观"] = "Вигляд",
            ["开发版"] = "Версія для розробників",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "Перш ніж налаштувати DomeOS AI, виберіть бажану мову.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "Відповідатиму обраною мовою; «Авто» слідує за мовою вводу.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "Введіть ключ постачальника. «Custom API» підключає будь-яку точку, сумісну з OpenAI.",
            ["服务商"] = "Постачальник",
            ["接口地址"] = "Адреса API",
            ["模型"] = "Модель",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "Ключ зберігається лише в локальному конфігу і нікуди не надсилається.",
            ["语音系统"] = "Голос",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "Виберіть голос. MiMo потребує ключа; Microsoft і Віртуальна співачка використовують локальні голоси.",
            ["语音引擎"] = "Голосовий рушій",
            ["MiMo API Key（可选）"] = "MiMo API Key (необов'язково)",
            ["试听"] = "Прослухати",
            ["自动朗读回复"] = "Автоматично читати відповіді",
            ["外观设置"] = "Вигляд",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "Наостанок виберіть тему та фон. Їх можна змінити в налаштуваннях будь-коли.",
            ["主题"] = "Тема",
            ["背景"] = "Фон",
            ["跟随系统"] = "Як у системі",
            ["浅色"] = "Світла",
            ["深色"] = "Темна",
            ["云母"] = "Mica",
            ["亚克力"] = "Акрил",
            ["纯色"] = "Однотонний",
            ["图片"] = "Зображення",
            ["上一步"] = "Назад",
            ["下一步"] = "Далі",
            ["完成"] = "Готово",
            ["跳过引导"] = "Пропустити",
            ["自动（跟随主人的语言）"] = "Авто (за мовою вводу)",
            ["自动（MiMo 优先，系统语音兜底）"] = "Авто (спочатку MiMo, потім системний голос)",
            ["仅 MiMo TTS"] = "Лише MiMo TTS",
            ["仅 Microsoft 语音"] = "Лише Microsoft",
            ["虚拟歌姬"] = "Віртуальна співачка",
            ["对话"] = "Чат",
            ["工作区"] = "Робоча область",
            ["最近对话"] = "Нещодавні чати",
            ["设置"] = "Налаштування",
            ["新对话"] = "Новий чат",
            ["只看收藏"] = "Лише обране",
            ["点一张卡片打开对话"] = "Натисніть картку, щоб відкрити чат",
            ["发送"] = "Надіслати",
            ["清除"] = "Очистити",
            ["收藏"] = "Обране",
            ["删除"] = "Видалити",
            ["删除对话"] = "Видалити чат",
            ["未配置服务商"] = "Постачальника не задано",
            ["未选择模型"] = "Модель не вибрано",
            ["当前服务商"] = "Поточний постачальник",
            ["角色"] = "Персона",
            ["权限"] = "Дозволи",
            ["通用"] = "Загальні",
            ["关于"] = "Про програму",
            ["返回"] = "Назад",
            ["选择文件夹"] = "Вибрати папку",
            ["未选择工作区"] = "Робочу область не вибрано",
            ["关闭"] = "Вимк",
            ["开启"] = "Увімк",
            ["自动"] = "Авто",
            ["自定义"] = "Власне",
            ["名称"] = "Назва",
            ["刷新"] = "Оновити",
            ["添加"] = "Додати",
            ["设为当前"] = "Зробити поточним",
            ["应用到当前服务商"] = "Застосувати до поточного",
            ["保存这家服务商"] = "Зберегти постачальника",
            ["保存语音设置"] = "Зберегти голос",
            ["温度 / 最大回复"] = "Температура / макс. відповідь",
            ["朗读引擎"] = "Рушій читання",
            ["自动朗读"] = "Авточитання",
            ["自动批准"] = "Автопідтвердження",
            ["联网搜索"] = "Пошук у мережі",
            ["深度思考"] = "Глибоке мислення",
            ["桌面通知"] = "Сповіщення",
            ["开机自启"] = "Запуск із Windows",
            ["应用主题"] = "Тема",
            ["侧栏宽度"] = "Ширина панелі",
            ["保留的历史轮数"] = "Збережені ходи",
            ["隐私政策"] = "Конфіденційність",
            ["用户协议"] = "Умови використання",
            ["开源声明"] = "Відкритий код",
            ["鸣谢"] = "Подяки",
        };

        private static readonly Dictionary<string, string> Spanish = new Dictionary<string, string>
        {
            ["语言"] = "Idioma",
            ["语音"] = "Voz",
            ["外观"] = "Apariencia",
            ["开发版"] = "Versión de desarrollo",
            ["在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。"] =
                "Antes de configurar DomeOS AI, elige el idioma que quieres usar.",
            ["选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。"] =
                "Responderé en el idioma elegido; «Automático» sigue el idioma que escribas.",
            ["填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。"] =
                "Introduce la clave del proveedor. «Custom API» conecta cualquier endpoint compatible con OpenAI.",
            ["服务商"] = "Proveedor",
            ["接口地址"] = "Dirección API",
            ["模型"] = "Modelo",
            ["Key 只保存在本机的配置里，不会上传到别处。"] =
                "La clave solo se guarda en la configuración local y nunca se sube.",
            ["语音系统"] = "Voz",
            ["挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。"] =
                "Elige una voz. MiMo necesita clave; Microsoft y Cantante virtual usan voces locales.",
            ["语音引擎"] = "Motor de voz",
            ["MiMo API Key（可选）"] = "Clave API de MiMo (opcional)",
            ["试听"] = "Escuchar",
            ["自动朗读回复"] = "Leer respuestas",
            ["外观设置"] = "Apariencia",
            ["最后挑一下主题和背景。这些之后随时能在「设置」里改。"] =
                "Por último, elige un tema y un fondo. Puedes cambiarlos en Ajustes cuando quieras.",
            ["主题"] = "Tema",
            ["背景"] = "Fondo",
            ["跟随系统"] = "Seguir sistema",
            ["浅色"] = "Claro",
            ["深色"] = "Oscuro",
            ["云母"] = "Mica",
            ["亚克力"] = "Acrílico",
            ["纯色"] = "Color",
            ["图片"] = "Imagen",
            ["上一步"] = "Atrás",
            ["下一步"] = "Siguiente",
            ["完成"] = "Finalizar",
            ["跳过引导"] = "Omitir",
            ["自动（跟随主人的语言）"] = "Automático (idioma de entrada)",
            ["自动（MiMo 优先，系统语音兜底）"] = "Automático (MiMo primero, voz del sistema después)",
            ["仅 MiMo TTS"] = "Solo MiMo TTS",
            ["仅 Microsoft 语音"] = "Solo voz de Microsoft",
            ["虚拟歌姬"] = "Cantante virtual",
            ["对话"] = "Chat",
            ["工作区"] = "Espacio de trabajo",
            ["最近对话"] = "Chats recientes",
            ["设置"] = "Ajustes",
            ["新对话"] = "Nuevo chat",
            ["只看收藏"] = "Solo favoritos",
            ["点一张卡片打开对话"] = "Haz clic en una tarjeta para abrir",
            ["发送"] = "Enviar",
            ["清除"] = "Borrar",
            ["收藏"] = "Favorito",
            ["删除"] = "Eliminar",
            ["删除对话"] = "Eliminar chat",
            ["未配置服务商"] = "Sin proveedor",
            ["未选择模型"] = "Sin modelo",
            ["当前服务商"] = "Proveedor actual",
            ["角色"] = "Personaje",
            ["权限"] = "Permisos",
            ["通用"] = "General",
            ["关于"] = "Acerca de",
            ["返回"] = "Volver",
            ["选择文件夹"] = "Elegir carpeta",
            ["未选择工作区"] = "Sin espacio de trabajo",
            ["关闭"] = "Apagado",
            ["开启"] = "Encendido",
            ["自动"] = "Automático",
            ["自定义"] = "Personalizado",
            ["名称"] = "Nombre",
            ["刷新"] = "Actualizar",
            ["添加"] = "Añadir",
            ["设为当前"] = "Establecer como actual",
            ["应用到当前服务商"] = "Aplicar al proveedor actual",
            ["保存这家服务商"] = "Guardar proveedor",
            ["保存语音设置"] = "Guardar voz",
            ["温度 / 最大回复"] = "Temperatura / respuesta máx.",
            ["朗读引擎"] = "Motor de lectura",
            ["自动朗读"] = "Lectura automática",
            ["自动批准"] = "Aprobación automática",
            ["联网搜索"] = "Búsqueda web",
            ["深度思考"] = "Pensamiento profundo",
            ["桌面通知"] = "Notificaciones",
            ["开机自启"] = "Iniciar con Windows",
            ["应用主题"] = "Tema",
            ["侧栏宽度"] = "Ancho de barra",
            ["保留的历史轮数"] = "Turnos guardados",
            ["隐私政策"] = "Privacidad",
            ["用户协议"] = "Términos",
            ["开源声明"] = "Código abierto",
            ["鸣谢"] = "Créditos",
        };
    }
}
