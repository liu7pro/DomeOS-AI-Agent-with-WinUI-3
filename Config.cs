using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace DomeOSAgentDev
{
    /// <summary>A live provider profile: a preset the user may have edited, plus their key.</summary>
    public sealed class ProviderProfile
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Protocol { get; set; } = "openai";
        public string BaseUrl { get; set; } = "";
        public string Model { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public double Temperature { get; set; } = 0.7;
        /// <summary>0 means "no ceiling" on the reply length.</summary>
        public int MaxTokens { get; set; }
    }

    public sealed class AgentSettings
    {
        public bool Enabled { get; set; } = true;
        public bool DeepThinking { get; set; }
        public bool WebSearch { get; set; } = true;
        /// <summary>workspace = jailed to the folder; machine = every drive on the computer.</summary>
        public string Scope { get; set; } = "workspace";
        /// <summary>When true no confirmation card is shown before a risky tool runs.</summary>
        public bool AutoApprove { get; set; } = true;
        /// <summary>0 means "no limit" for all three.</summary>
        public int MaxSteps { get; set; } = 12;
        public int MaxOutput { get; set; } = 20000;
        public int ShellTimeoutMs { get; set; } = 90000;
    }

    public sealed class UiSettings
    {
        public string Hotkey { get; set; } = "Ctrl+Alt+G";
        public bool ShowOnStart { get; set; } = true;
        /// <summary>one of the accent families: qq / jade / violet / sakura / amber / mono.</summary>
        public string ThemePreset { get; set; } = "qq";
        /// <summary>a flat pattern painted under the ambience: none / grid / diag / wave / diamond.</summary>
        public string Ornament { get; set; } = "none";
        public int SidebarWidth { get; set; } = 244;
        /// <summary>The page the window was last showing: chat / workspace / settings.</summary>
        public string StartPage { get; set; } = "chat";
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public int WindowWidth { get; set; } = 1100;
        public int WindowHeight { get; set; } = 760;
    }

    public sealed class OrbSettings
    {
        public bool Enabled { get; set; } = true;
        public string Primary { get; set; } = "4A90E2";
        public string Secondary { get; set; } = "FFC90E";
        public int Size { get; set; } = 34;
        public int BarSize { get; set; } = 96;
    }

    public sealed class BackgroundSettings
    {
        /// <summary>mica = 云母；acrylic = 亚克力；solid = 自己铺的纯色；image = 自己铺的图片。</summary>
        public string Mode { get; set; } = "mica";
        /// <summary>纯色模式的底色，RRGGBB。</summary>
        public string Color { get; set; } = "101828";
        public string File { get; set; } = "";
        public double Blur { get; set; } = 18;
        public double Dim { get; set; } = 0.35;
        public string Fit { get; set; } = "cover";
        public double Volume { get; set; }
    }

    public sealed class TtsSettings
    {
        public string Engine { get; set; } = "auto";
        public bool AutoRead { get; set; }
        public string MimoKey { get; set; } = "";
        public string MimoBase { get; set; } = "https://api.xiaomimimo.com/v1";
        public string MimoModel { get; set; } = "mimo-v2.5-tts";
        public string MimoVoice { get; set; } = "mimo_default";
        public string MimoStyle { get; set; } = "";
        public string WindowsVoice { get; set; } = "";
        public int WindowsRate { get; set; }

        /// <summary>
        /// 虚拟歌姬：luotianyi / miku / custom。用本机 SAPI 声库配音高来模仿二次元歌姬的嗓子；
        /// 真装了洛天依、初音那样的 SAPI 声库时会直接出现在声库下拉里，选中就用真的。
        /// </summary>
        public string VocaloidPreset { get; set; } = "luotianyi";

        public string VocaloidVoice { get; set; } = "";

        /// <summary>音高偏移，百分比。-50 到 60，0 是原声。</summary>
        public int VocaloidPitch { get; set; } = 20;

        /// <summary>SAPI 语速，-10 到 10。</summary>
        public int VocaloidRate { get; set; } = 1;

        /// <summary>
        /// 配音软件：builtin 用本机 SAPI 近似；xstudio / custom 把文字交给外部程序。
        /// </summary>
        public string VocaloidAppMode { get; set; } = "builtin";

        /// <summary>
        /// 外部配音软件的路径（例如 X Studio）。留空就是内置的近似音色；
        /// 填了以后朗读会把文字交给这个程序，而不是本机 SAPI。
        /// </summary>
        public string VocaloidApp { get; set; } = "";

        /// <summary>外部配音软件的命令行参数模板，可用 {file} 和 {text}。</summary>
        public string VocaloidAppArgs { get; set; } = "{file}";
    }

    public sealed class QuickSettings
    {
        public bool Enabled { get; set; } = true;
        public string Hotkey { get; set; } = "Ctrl+Alt+Space";
        public int HideAfterMs { get; set; } = 9000;
        public int Thickness { get; set; } = 6;
        public string Color { get; set; } = "4A90E2";
        public bool OpenMain { get; set; }
    }

    /// <summary>The whole profile, one document.</summary>
    public sealed class AppSettings
    {
        public bool RunAtLogin { get; set; }
        public bool Notifications { get; set; } = true;
        public int HistoryLimit { get; set; } = 20;
        public string Language { get; set; } = "auto";
        /// <summary>auto / light / dark - how the window follows the system theme.</summary>
        public string Theme { get; set; } = "auto";
        public string LegalAccepted { get; set; } = "";
        public bool Onboarded { get; set; }
        public string Workspace { get; set; } = "";
        public bool VoiceEnabled { get; set; } = true;
        public bool AiControllerEnabled { get; set; }
        public bool MediaEnabled { get; set; } = true;
        public bool MediaShowChip { get; set; } = true;
        /// <summary>Index into <see cref="Providers"/>.</summary>
        public int ActiveProvider { get; set; }

        public UiSettings Ui { get; set; } = new UiSettings();
        public AgentSettings Agent { get; set; } = new AgentSettings();
        public OrbSettings Orb { get; set; } = new OrbSettings();
        public BackgroundSettings Background { get; set; } = new BackgroundSettings();
        public TtsSettings Tts { get; set; } = new TtsSettings();
        public QuickSettings Quick { get; set; } = new QuickSettings();
        public List<ProviderProfile> Providers { get; set; } = new List<ProviderProfile>();

        public ProviderProfile Active()
        {
            if (Providers == null || Providers.Count == 0)
            {
                return new ProviderProfile();
            }

            if (ActiveProvider < 0 || ActiveProvider >= Providers.Count)
            {
                return Providers[0];
            }

            return Providers[ActiveProvider];
        }

        public static AppSettings CreateDefault()
        {
            AppSettings settings = new AppSettings();
            // fully qualified: inside this class "Providers" is the property, not the catalogue
            settings.Providers = DomeOSAgentDev.Providers.DefaultProviders();
            return settings;
        }
    }

    /// <summary>
    /// Where this app keeps its profile. It is deliberately its own folder - the WinUI3 build is
    /// a separate program and never touches another version's files.
    /// </summary>
    public static class SettingsStore
    {
        public const string FolderName = "DomeOS AI Agent Dev";
        public const string FileName = "config.json";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private static string _dataDir = string.Empty;

        public static string DataDir
        {
            get
            {
                if (_dataDir.Length == 0)
                {
                    _dataDir = ResolveDataDir();
                }

                return _dataDir;
            }
        }

        public static string ConfigPath => Path.Combine(DataDir, FileName);

        /// <summary>The loaded profile; Load() fills it in.</summary>
        public static AppSettings Current { get; private set; } = AppSettings.CreateDefault();

        private static string ResolveDataDir()
        {
            List<string> candidates = new List<string>();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(roaming))
            {
                candidates.Add(Path.Combine(roaming, FolderName));
            }

            if (!string.IsNullOrEmpty(local))
            {
                candidates.Add(Path.Combine(local, FolderName));
            }

            candidates.Add(Path.Combine(AppContext.BaseDirectory, "data"));

            foreach (string dir in candidates)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string probe = Path.Combine(dir, ".write-probe");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    return dir;
                }
                catch
                {
                    // try the next candidate
                }
            }

            return candidates[candidates.Count - 1];
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    Current = AppSettings.CreateDefault();
                    Save();
                    return Current;
                }

                string json = File.ReadAllText(ConfigPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault();
            }
            catch
            {
                // a damaged file must not stop the app from starting
                Current = AppSettings.CreateDefault();
            }

            Repair(Current);
            return Current;
        }

        public static void Save()
        {
            if (Current == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(DataDir);
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
                File.Move(tmp, ConfigPath, true);
            }
            catch
            {
                // a read-only profile must not take the app down
            }
        }

        /// <summary>Everything a hand-edited file could get wrong, put back in range.</summary>
        private static void Repair(AppSettings settings)
        {
            if (settings.Ui == null)
            {
                settings.Ui = new UiSettings();
            }

            if (settings.Agent == null)
            {
                settings.Agent = new AgentSettings();
            }

            if (settings.Orb == null)
            {
                settings.Orb = new OrbSettings();
            }

            if (settings.Background == null)
            {
                settings.Background = new BackgroundSettings();
            }

            if (settings.Tts == null)
            {
                settings.Tts = new TtsSettings();
            }

            if (settings.Quick == null)
            {
                settings.Quick = new QuickSettings();
            }

            if (settings.Providers == null || settings.Providers.Count == 0)
            {
                settings.Providers = Providers.DefaultProviders();
            }

            // 老版本写下的「只说简体中文」会在这里被换成多语言人设，主人自己改过的部分不动
            foreach (ProviderProfile provider in settings.Providers)
            {
                provider.SystemPrompt = Persona.MigratePrompt(provider.SystemPrompt, Persona.DefaultSys);

                // 名字被非 UTF-8 的方式写坏过，中文会变成一串问号；内置服务商按预设改回来
                if (provider.Name != null && provider.Name.Contains("???", StringComparison.Ordinal))
                {
                    ProviderPreset preset = Providers.Find(provider.Id);
                    if (preset.Id == provider.Id)
                    {
                        provider.Name = preset.Name;
                    }
                }
            }

            settings.HistoryLimit = Math.Max(2, settings.HistoryLimit);
            settings.ActiveProvider = Math.Max(0, settings.ActiveProvider);
            if (settings.ActiveProvider >= settings.Providers.Count)
            {
                settings.ActiveProvider = 0;
            }

            if (!OneOf(settings.Theme, "auto", "light", "dark"))
            {
                settings.Theme = "auto";
            }

            // 老配置里的 zh-Hans-CN、en-GB 之类统一收拢到现在的五个标准标签
            settings.Language = LanguageCatalog.Canonical(settings.Language);

            UiSettings ui = settings.Ui;
            ui.SidebarWidth = Math.Clamp(ui.SidebarWidth, 190, 560);
            // 启动页只恢复「对话」或「工作区」；上次停在设置页的话，下次从对话开始
            if (!OneOf(ui.StartPage, "chat", "workspace"))
            {
                ui.StartPage = "chat";
            }
            ui.WindowWidth = Math.Max(640, ui.WindowWidth);
            ui.WindowHeight = Math.Max(460, ui.WindowHeight);
            if (!OneOf(ui.ThemePreset, "qq", "jade", "violet", "sakura", "amber", "mono"))
            {
                ui.ThemePreset = "qq";
            }

            if (!OneOf(ui.Ornament, "none", "grid", "diag", "wave", "diamond"))
            {
                ui.Ornament = "none";
            }

            AgentSettings agent = settings.Agent;
            if (agent.Scope != "machine")
            {
                agent.Scope = "workspace";
            }

            agent.MaxSteps = Limit(agent.MaxSteps, 40);
            agent.MaxOutput = Limit(agent.MaxOutput, 200000);
            agent.ShellTimeoutMs = Limit(agent.ShellTimeoutMs, 600000);

            settings.Orb.Size = Math.Clamp(settings.Orb.Size, 16, 64);
            settings.Orb.BarSize = Math.Clamp(settings.Orb.BarSize, 40, 180);

            BackgroundSettings background = settings.Background;
            background.Blur = Math.Clamp(background.Blur, 0, 60);
            background.Dim = Math.Clamp(background.Dim, 0, 0.9);
            background.Volume = Math.Clamp(background.Volume, 0, 1);
            if (!OneOf(background.Fit, "cover", "contain", "tile"))
            {
                background.Fit = "cover";
            }

            if (!OneOf(background.Mode, "mica", "acrylic", "solid", "image"))
            {
                background.Mode = "mica";
            }

            if (!OneOf(settings.Tts.Engine, "auto", "mimo", "microsoft", "vocaloid"))
            {
                settings.Tts.Engine = "auto";
            }

            // SAPI 的语速就是 -10 到 10；老存档里按 -50..100 存的值在这里收回范围内
            settings.Tts.WindowsRate = Math.Clamp(settings.Tts.WindowsRate, -10, 10);

            TtsSettings tts = settings.Tts;
            if (!OneOf(tts.VocaloidPreset, "luotianyi", "miku", "custom"))
            {
                tts.VocaloidPreset = "luotianyi";
            }

            tts.VocaloidPitch = Math.Clamp(tts.VocaloidPitch, -50, 60);
            tts.VocaloidRate = Math.Clamp(tts.VocaloidRate, -10, 10);
            if (!OneOf(tts.VocaloidAppMode, "builtin", "xstudio", "custom"))
            {
                tts.VocaloidAppMode = "builtin";
            }

            tts.VocaloidApp = (tts.VocaloidApp ?? string.Empty).Trim();
            tts.VocaloidAppArgs = string.IsNullOrWhiteSpace(tts.VocaloidAppArgs)
                ? "{file}"
                : tts.VocaloidAppArgs.Trim();
            settings.Quick.HideAfterMs = Math.Clamp(settings.Quick.HideAfterMs, 0, 120000);
            settings.Quick.Thickness = Math.Clamp(settings.Quick.Thickness, 2, 24);

            foreach (ProviderProfile provider in settings.Providers)
            {
                provider.Temperature = Math.Clamp(provider.Temperature, 0, 2);
                provider.MaxTokens = Math.Max(0, provider.MaxTokens);
                if (!OneOf(provider.Protocol, "openai", "anthropic", "gemini", "ollama"))
                {
                    provider.Protocol = "openai";
                }
            }
        }

        private static bool OneOf(string value, params string[] allowed)
        {
            return !string.IsNullOrEmpty(value) && Array.IndexOf(allowed, value) >= 0;
        }

        private static int Limit(int value, int max)
        {
            return value <= 0 ? 0 : Math.Min(max, value);
        }
    }

    /// <summary>语言下拉里的一项：标签（BCP-47）+ 母语写法的名字。</summary>
    public sealed class LanguageOption
    {
        public string Tag { get; set; } = LanguageCatalog.AutoTag;
        public string Name { get; set; } = "";

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// 向导第一步和设置里的语言列表。不写死一份几十行的名单，直接用 .NET 携带的
    /// 区域设置表——它和 Windows 认得的语言同源，装了语言包就自然跟着多出来。
    /// </summary>
    public static class LanguageCatalog
    {
        public const string AutoTag = "auto";

        private static List<LanguageOption>? _all;

        public static IReadOnlyList<LanguageOption> All => _all ??= Build();

        /// <summary>换了界面语言之后把「自动」那一项的名字重新生成一遍。</summary>
        public static void Refresh()
        {
            _all = null;
        }

        private static List<LanguageOption> Build()
        {
            // 只留这几套翻好的语言，其余旧配置统一落到对应的规范标签
            return new List<LanguageOption>
            {
                new LanguageOption { Tag = AutoTag, Name = Loc.T("自动（跟随主人的语言）") },
                new LanguageOption { Tag = "en-US", Name = "English (United States)" },
                new LanguageOption { Tag = "ru-RU", Name = "Русский (Россия)" },
                new LanguageOption { Tag = "uk-UA", Name = "Українська (Україна)" },
                new LanguageOption { Tag = "ja-JP", Name = "日本語 (日本)" },
                new LanguageOption { Tag = "ko-KR", Name = "한국어 (대한민국)" },
                new LanguageOption { Tag = "zh-Hans", Name = "中文（简体）" },
                new LanguageOption { Tag = "zh-Hant", Name = "中文（繁體）" },
            };
        }

        /// <summary>把老配置里的 zh-Hans-CN、en-GB 这类标签收拢到列表里的五个标准标签。</summary>
        public static string Canonical(string tag)
        {
            string t = (tag ?? string.Empty).Trim();
            if (t.Length == 0 || t.Equals(AutoTag, StringComparison.OrdinalIgnoreCase))
            {
                return AutoTag;
            }

            string lower = t.ToLowerInvariant();
            if (lower.StartsWith("zh", StringComparison.Ordinal))
            {
                if (lower.Contains("hans"))
                {
                    return "zh-Hans";
                }

                return lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk")
                       || lower.EndsWith("-mo")
                    ? "zh-Hant"
                    : "zh-Hans";
            }

            if (lower.StartsWith("en", StringComparison.Ordinal)) return "en-US";
            if (lower.StartsWith("ru", StringComparison.Ordinal)) return "ru-RU";
            if (lower.StartsWith("uk", StringComparison.Ordinal)) return "uk-UA";
            if (lower.StartsWith("ja", StringComparison.Ordinal)) return "ja-JP";
            if (lower.StartsWith("ko", StringComparison.Ordinal)) return "ko-KR";
            return "en-US";
        }

        /// <summary>语言标签在列表里的位置；认不出来就落到「自动」。</summary>
        public static int IndexOf(string tag)
        {
            string wanted = Canonical(tag);
            IReadOnlyList<LanguageOption> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Tag, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>把标签换成母语名字，给系统提示词用。</summary>
        public static string NameFor(string tag)
        {
            string wanted = (tag ?? string.Empty).Trim();
            foreach (LanguageOption option in All)
            {
                if (string.Equals(option.Tag, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return option.Name;
                }
            }

            return wanted;
        }
    }
}
