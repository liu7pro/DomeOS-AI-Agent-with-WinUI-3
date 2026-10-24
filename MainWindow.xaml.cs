using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;
using Windows.Media.SpeechSynthesis;
using Windows.System;
using Windows.UI;

// Windows.System also declares these names, so the WinUI ones are pinned down here.
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace DomeOSAgentDev
{
    internal enum DomeThemeMode
    {
        System,
        Light,
        Dark,
    }

    /// <summary>
    /// The shell. It is built the way a Windows 11 app is built: the system title bar area carries
    /// the app icon and name, a NavigationView owns the navigation (including the settings page,
    /// which is the control's own settings item), and every piece of chrome inside is a stock
    /// WinUI control rather than a hand-written template.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>Entries listed per folder before the tree says there is more.</summary>
        private const int MaxFolderEntries = 300;

        private readonly AppSettings _settings;
        private DomeThemeMode _theme;

        /// <summary>The 「1+」 scene, while it is on screen.</summary>
        private NeverSettleScene? _neverSettle;

        /// <summary>The RunDuck egg, once it has been opened at least once.</summary>
        private RunDuckGame? _runDuck;

        /// <summary>Left clicks counted towards the About card's hidden gesture.</summary>
        private int _aboutLeftClicks;

        /// <summary>The newest answer, which is what the read-aloud button reads.</summary>
        private string _lastAssistantText = string.Empty;

        /// <summary>正在朗读的那条消息旁边的小喇叭；没在念就是 null。</summary>
        private Button? _speakingButton;

        /// <summary>The conversation the reader is inside, if any.</summary>
        private Conversation? _current;

        /// <summary>The floating orb window, while it is switched on.</summary>
        private OrbWindow? _orbWindow;

        /// <summary>The command bar that pops out of the orb.</summary>
        private QuickBarWindow? _quickBar;

        /// <summary>The live copy of the orb shown inside the settings page.</summary>
        private OrbVisual? _orbPreview;

        private DispatcherQueueTimer? _orbPreviewTimer;

        /// <summary>Set while a thread is being opened from the sidebar, so the nav list is not
        /// rebuilt out from under the selection that is being handled.</summary>
        private bool _openingThread;

        /// <summary>True while the sidebar list is being rebuilt: the selection events that stirs up
        /// are ours, not the user's, so they must not be treated as navigation.</summary>
        private bool _navRebuilding;

        /// <summary>Set while a sidebar rebuild is already waiting for the dispatcher.</summary>
        private bool _navRebuildQueued;

        /// <summary>True while the settings fields are being filled, or while the XAML is still being
        /// parsed. Setting Minimum on a slider coerces its Value and fires ValueChanged straight away,
        /// so every settings handler stays quiet until the window has finished being built.</summary>
        private bool _loading = true;

        /// <summary>The turn currently being streamed, so it can be called off halfway.</summary>
        private CancellationTokenSource? _agentRun;

        private bool _agentBusy;

        /// <summary>The answer bubble being grown by the stream; null between turns.</summary>
        private TextBlock? _streamText;

        /// <summary>The one-line status under the transcript while a turn runs.</summary>
        private TextBlock? _agentStatus;

        private readonly System.Text.StringBuilder _streamBuffer = new System.Text.StringBuilder();

        private readonly Dictionary<string, ToolCard> _toolCards = new Dictionary<string, ToolCard>();

        /// <summary>按服务商 Id 缓存的「这家到底有哪些模型」，给输入框上方的快捷选择用。</summary>
        private readonly Dictionary<string, List<string>> _modelCache = new Dictionary<string, List<string>>();

        /// <summary>正在拉取模型列表的那次请求，新的一次会把它取消掉。</summary>
        private CancellationTokenSource? _modelsRequest;

        /// <summary>上一次问过模型列表的「这一家 + 地址 + 钥匙」，一模一样就不再重复打网络。</summary>
        private string _lastProbeSignature = string.Empty;

        /// <summary>流式输出的合帧定时器：模型吐字比屏幕刷新快得多，攒一小会儿再画一次。</summary>
        private DispatcherQueueTimer? _streamTimer;

        private bool _streamDirty;

        /// <summary>记录里那些「Just do it」的图，切换主题时要把它们换成对应的那一版。</summary>
        private readonly List<Image> _nikeImages = new List<Image>();

        /// <summary>已经贴在记录里的回答气泡；换主题时把它们一起刷成新的底色。</summary>
        private readonly List<Border> _assistantBubbles = new List<Border>();

        /// <summary>语言切换的收尾版本号，快速连点时只让最后一次生效。</summary>
        private int _languageApplyVersion;

        // ---- 音乐彩蛋：播放器是外面的，歌词靠系统媒体会话的进度来对
        private DispatcherQueueTimer? _eggSongTimer;
        private Process? _eggSongPlayer;
        private TextBlock? _eggSongHeader;
        private TextBlock? _eggSongLine;
        private TextBlock? _eggSongNext;
        private string _eggHeaderSuffix = "";
        private Button? _eggSongStop;
        private Button? _eggCalibrateButton;
        private Button? _eggMarkButton;
        private StackPanel? _eggSongButtons;
        private bool _eggCalibrating;
        private int _eggCalibIndex;
        private double[]? _eggCalibTimes;
        private string _eggSongLyricFile = "";
        private LyricTrack? _eggSongTrack;
        private TimeSpan _eggSongDuration = TimeSpan.FromMinutes(4);
        private DateTime _eggSongStart;
        private string _eggSongTitle = "";
        private string _eggSongArtist = "";
        private int _eggSongIndex = -1;
        private bool _eggSongRunning;
        private bool _eggSessionSeen;
        private int _eggSessionMisses;
        private GlobalSystemMediaTransportControlsSession? _eggSession;
        private GlobalSystemMediaTransportControlsSessionManager? _eggSessions;

        private ImageSource? _nikeLight;

        private ImageSource? _nikeDark;

        /// <summary>任务栏通知区里的那个图标。</summary>
        private TrayIcon? _tray;

        public MainWindow()
        {
            InitializeComponent();

            _settings = SettingsStore.Load();

            Title = AppInfo.DisplayName;
            AppWindow.Title = AppInfo.DisplayName;
            BrandTitle.Text = AppInfo.DisplayName;
            AboutNameText.Text = AppInfo.DisplayName;
            OnboardBrandText.Text = AppInfo.DisplayName;

            LoadBranding();
            SetupTitleBar();
            ApplySettingsToShell();
            SeedConversations();

            RootGrid.Loaded += RootGrid_Loaded;
            RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
            RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;
            // the mini-game also needs key releases, which the composer would otherwise swallow
            RootGrid.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(RootGrid_KeyUp), true);
            AppWindow.Closing += AppWindow_Closing;
            Speech.StateChanged += Speech_StateChanged;

            // 托盘图标：让它在任务栏通知区里一直有个位置，右键能直接跳到各个功能页
            _tray = new TrayIcon(TrayAction);
            _tray.Show();

            // Selecting an item is what puts a page on screen, and the last page is restored. The
            // panel is also raised explicitly: selecting the built-in settings item does not raise
            // a selection-changed event, so the settings page would otherwise never come up.
            string start = _settings.Ui.StartPage;
            NavView.SelectedItem = start == "settings"
                ? NavView.SettingsItem
                : NavView.MenuItems[start == "workspace" ? 1 : 0];

            if (start == "settings")
            {
                ShowPanel("settings");
            }

            _loading = false;
        }

        // ------------------------------------------------------------------ window chrome

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            SetupCaptionSpacer();
            ApplyLanguageToUi();
            ComposerBox.Focus(FocusState.Programmatic);

            // first run: the terms have to be read and agreed to before anything else
            if (!_settings.Onboarded)
            {
                if (await Legal.ShowOnboardingAsync(RootGrid.XamlRoot))
                {
                    _settings.LegalAccepted = ChatStore.Now();
                    SettingsStore.Save();

                    // 读完条款接着就是整窗的新手引导（4 步），走完或跳过才算 onboarded
                    ShowOnboarding();
                }
                else
                {
                    Application.Current.Exit();
                }
            }
        }

        private async void LegalDoc_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            string? file = button?.Tag as string;
            string label = button?.Content as string ?? "文档";

            if (!string.IsNullOrEmpty(file))
            {
                await Legal.ShowDocumentAsync(RootGrid.XamlRoot, label, file!);
            }
        }

        /// <summary>鸣谢不是一份文档，而是一个对话框，和 Electron 版那边一样。</summary>
        private async void Credits_Click(object sender, RoutedEventArgs e)
        {
            await Legal.ShowCreditsAsync(RootGrid.XamlRoot);
        }

        private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyCaptionColors();

            // 「Just do it」贴出来的图也是分浅色 / 深色两版的，主题一变就跟着换
            ApplyNikeTheme();

            // 回答气泡的底色和边框也跟着主题走
            foreach (Border bubble in _assistantBubbles)
            {
                ApplyBubbleTheme(bubble);
            }

            // 自己铺背景时侧栏和标题栏那两层纱的颜色也是跟主题走的，换主题要重铺一遍
            if (BackdropLayer.Visibility == Visibility.Visible)
            {
                ApplyPaneScrim();
                AppTitleBar.Background = new SolidColorBrush(ScrimColor());
            }
        }

        private void SetupTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            try
            {
                AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            }
            catch
            {
                // an older runtime may not know the tall caption
            }

            SetTitleBar(AppTitleBar);
        }

        /// <summary>Leaves exactly the room the system caption buttons need on the right.</summary>
        private void SetupCaptionSpacer()
        {
            try
            {
                double scale = 1.0;
                if (RootGrid.XamlRoot != null)
                {
                    scale = RootGrid.XamlRoot.RasterizationScale;
                }

                double dip = AppWindow.TitleBar.RightInset / scale;
                if (dip < 120)
                {
                    dip = 120;
                }

                CaptionSpacer.Width = new GridLength(dip);
            }
            catch
            {
                CaptionSpacer.Width = new GridLength(140);
            }
        }

        private void LoadBranding()
        {
            try
            {
                string assets = Path.Combine(AppContext.BaseDirectory, "Assets");

                string logo = Path.Combine(assets, "logo.png");
                if (File.Exists(logo))
                {
                    BitmapImage artwork = new BitmapImage(new Uri(logo));
                    LogoImage.Source = artwork;
                    AboutLogoImage.Source = artwork;
                    OnboardLogoImage.Source = artwork;
                }

                string icon = Path.Combine(assets, "app.ico");
                if (File.Exists(icon))
                {
                    AppWindow.SetIcon(icon);
                }
            }
            catch
            {
                // the shell still works without the artwork
            }
        }

        // ------------------------------------------------------------------ navigation

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            // 重建侧栏列表时被搅动出来的选中事件是我们自己弄的，不算主人在导航
            if (_navRebuilding)
            {
                return;
            }

            if (args.IsSettingsSelected)
            {
                ShowPanel("settings");
                return;
            }

            NavigationViewItem? item = args.SelectedItem as NavigationViewItem;
            string? tag = item?.Tag as string;

            if (tag != null && tag.StartsWith("thread:", StringComparison.Ordinal))
            {
                OpenThreadFromNav(tag.Substring("thread:".Length));
                return;
            }

            ShowPanel(string.IsNullOrEmpty(tag) ? "chat" : tag!);
        }

        /// <summary>Opens a conversation picked from the sidebar list.</summary>
        private void OpenThreadFromNav(string id)
        {
            Conversation? conversation = ChatStore.Items.Find(entry => entry.Id == id);
            if (conversation == null)
            {
                return;
            }

            _openingThread = true;
            try
            {
                ShowPanel("chat");
                OpenConversation(conversation);
            }
            finally
            {
                _openingThread = false;
            }

            // 列表重建推迟到这次选中处理完之后：在自己的事件里动 MenuItems 会把
            // NavigationView 的选中状态搅乱，点几下就会崩
            RefreshNavThreadsDeferred();
        }

        private void ShowPanel(string key)
        {
            ChatPanel.Visibility = key == "chat" ? Visibility.Visible : Visibility.Collapsed;
            WorkspacePanel.Visibility = key == "workspace" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;

            if (key == "workspace")
            {
                NavView.Header = "工作区";
            }
            else if (key == "settings")
            {
                NavView.Header = "设置";
            }
            else
            {
                // 对话页不要那个大标题：它紧挨着第一行的按钮，两个会挤在一起；
                // 侧栏本来就高亮着「对话」，再来一行大字反而像重影。
                NavView.Header = null;
            }

            if (key == "settings")
            {
                RefreshSettingsFields();
                StartOrbPreview();
            }
            else
            {
                StopOrbPreview();

                if (key == "chat")
                {
                    ShowGallery();
                }
            }

            _settings.Ui.StartPage = key;
            SettingsStore.Save();
        }

        /// <summary>Jumps to the settings page with one section already opened.</summary>
        private void GoToSettings(string section)
        {
            NavView.SelectedItem = NavView.SettingsItem;

            AppearanceExpander.IsExpanded = section == "appearance";
            ProviderExpander.IsExpanded = section == "provider";
            PersonaExpander.IsExpanded = section == "persona";
            PermissionExpander.IsExpanded = section == "permission";
            GeneralExpander.IsExpanded = section == "general";
            AboutExpander.IsExpanded = section == "about";
        }

        private void ProviderButton_Click(object sender, RoutedEventArgs e)
        {
            GoToSettings("provider");
        }

        // ------------------------------------------------------------------ 界面语言

        /// <summary>
        /// 把当前语言应用到整棵可视树。XAML 里写的是中文原文，Loc 会按原文查表替换，
        /// 向导里代码新建的控件也在同一棵树里，所以这一步就把整个界面换掉。
        /// </summary>
        private void ApplyLanguageToUi()
        {
            Loc.Use(_settings.Language);
            LanguageCatalog.Refresh();
            Loc.ApplyTo(RootGrid);

            // 快速连点时，之前几次排队的收尾全部作废，只让最后一次再扫一遍，
            // 免得不同控件停在不同语言上
            int version = ++_languageApplyVersion;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (version != _languageApplyVersion)
                {
                    return;
                }

                Loc.Use(_settings.Language);
                Loc.ApplyTo(RootGrid);
            });
        }

        // ------------------------------------------------------------------ 新手引导

        /// <summary>当前是第几步（0 是语言，一共 4 步）。</summary>
        private int _onboardStep;

        /// <summary>顶部步骤条上的图标和名字，和 Fluent Launcher 的向导一样。</summary>
        private static readonly (string Glyph, string Label)[] OnboardSteps =
        {
            ("\uE8C1", "语言"),
            ("\uE72E", "API Key"),
            ("\uE720", "语音"),
            ("\uE790", "外观"),
        };

        private ComboBox? _onboardLanguage;
        private ComboBox? _onboardProvider;
        private PasswordBox? _onboardKey;
        private TextBox? _onboardBaseUrl;
        private TextBox? _onboardModel;
        private StackPanel? _onboardBaseGroup;
        private StackPanel? _onboardModelGroup;
        private ComboBox? _onboardEngine;
        private PasswordBox? _onboardMimo;
        private StackPanel? _onboardMimoGroup;
        private CheckBox? _onboardAutoRead;
        private ComboBox? _onboardTheme;
        private ComboBox? _onboardBackground;

        /// <summary>服务商下拉里的一项：内置的是索引，Custom API 是 -1。</summary>
        private sealed class OnboardProviderChoice
        {
            public string Name { get; init; } = "";
            public int Index { get; init; } = -1;
        }

        /// <summary>把整窗那一层铺开，从语言开始。</summary>
        private void ShowOnboarding()
        {
            _onboardStep = 0;
            OnboardingLayer.Visibility = Visibility.Visible;
            RenderOnboardingStep();
        }

        private void OnboardBack_Click(object sender, RoutedEventArgs e)
        {
            if (_onboardStep <= 0)
            {
                return;
            }

            _onboardStep--;
            RenderOnboardingStep();
        }

        private void OnboardNext_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveOnboardingStep())
            {
                return;
            }

            if (_onboardStep >= 3)
            {
                FinishOnboarding();
                return;
            }

            _onboardStep++;
            RenderOnboardingStep();
        }

        private void OnboardSkip_Click(object sender, RoutedEventArgs e)
        {
            SaveOnboardingStep();
            FinishOnboarding();
        }

        private void FinishOnboarding()
        {
            _settings.Onboarded = true;
            SettingsStore.Save();

            ApplySettingsToShell();

            // 引导结束后落在对话页，别把主人丢在设置里
            NavView.SelectedItem = NavView.MenuItems[0];
            ShowPanel("chat");

            OnboardingLayer.Visibility = Visibility.Collapsed;
            ComposerBox.Focus(FocusState.Programmatic);
        }

        /// <summary>把当前这一步的控件搭出来。</summary>
        private void RenderOnboardingStep()
        {
            OnboardBody.Children.Clear();
            OnboardNote.Visibility = Visibility.Collapsed;

            _onboardLanguage = null;
            _onboardProvider = null;
            _onboardKey = null;
            _onboardBaseUrl = null;
            _onboardModel = null;
            _onboardBaseGroup = null;
            _onboardModelGroup = null;
            _onboardEngine = null;
            _onboardMimo = null;
            _onboardMimoGroup = null;
            _onboardAutoRead = null;
            _onboardTheme = null;
            _onboardBackground = null;

            BuildOnboardStepBar();

            OnboardBackButton.IsEnabled = _onboardStep > 0;
            OnboardNextButton.Content = _onboardStep >= 3 ? "完成" : "下一步";

            switch (_onboardStep)
            {
                case 0:
                {
                    OnboardTitle("语言");
                    OnboardParagraph("在我们开始设置 DomeOS AI 之前，请选择您希望使用的语言。");

                    _onboardLanguage = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        DisplayMemberPath = "Name",
                        ItemsSource = LanguageCatalog.All,
                        SelectedIndex = LanguageCatalog.IndexOf(_settings.Language),
                    };

                    OnboardBody.Children.Add(_onboardLanguage);
                    OnboardExtra("选了哪种语言，本小姐就用哪种语言回答；「自动」会跟着您输入的语言走。");
                    _onboardLanguage.SelectionChanged += OnboardLanguage_SelectionChanged;
                    break;
                }

                case 1:
                {
                    OnboardTitle("API Key");
                    OnboardParagraph("填入服务商的密钥。也可以选「Custom API」，接任何 OpenAI 兼容的接口。");

                    _onboardProvider = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                    };

                    for (int i = 0; i < _settings.Providers.Count; i++)
                    {
                        _onboardProvider.Items.Add(new ComboBoxItem
                        {
                            Content = _settings.Providers[i].Name,
                            Tag = new OnboardProviderChoice { Name = _settings.Providers[i].Name, Index = i },
                        });
                    }

                    _onboardProvider.Items.Add(new ComboBoxItem
                    {
                        Content = "Custom API",
                        Tag = new OnboardProviderChoice { Name = "Custom API", Index = -1 },
                    });

                    _onboardProvider.SelectedIndex = Math.Clamp(
                        _settings.ActiveProvider, 0, Math.Max(0, _settings.Providers.Count - 1));

                    ProviderProfile active = _settings.Active();
                    _onboardKey = new PasswordBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                        PasswordRevealMode = PasswordRevealMode.Peek,
                        Password = active.ApiKey ?? string.Empty,
                        PlaceholderText = IsOllama(active) ? "（本地模型，无需填写）" : "sk-…",
                    };

                    _onboardBaseUrl = new TextBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                        PlaceholderText = "https://api.example.com/v1",
                    };

                    _onboardModel = new TextBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                        PlaceholderText = "模型名，例如 gpt-5",
                    };

                    _onboardProvider.SelectionChanged += OnboardProvider_SelectionChanged;

                    OnboardField(_onboardProvider, "服务商");
                    OnboardField(_onboardKey, "API Key");
                    _onboardBaseGroup = OnboardField(_onboardBaseUrl, "接口地址");
                    _onboardModelGroup = OnboardField(_onboardModel, "模型");
                    _onboardBaseGroup.Visibility = Visibility.Collapsed;
                    _onboardModelGroup.Visibility = Visibility.Collapsed;

                    OnboardExtra("Key 只保存在本机的配置里，不会上传到别处。");
                    break;
                }

                case 2:
                {
                    OnboardTitle("语音系统");
                    OnboardParagraph("挑一个朗读的嗓子。MiMo 需要填 Key，Microsoft 语音和虚拟歌姬都用本机声库。");

                    _onboardEngine = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                    };

                    _onboardEngine.Items.Add(new ComboBoxItem { Content = "自动（MiMo 优先，系统语音兜底）", Tag = "auto" });
                    _onboardEngine.Items.Add(new ComboBoxItem { Content = "仅 MiMo TTS", Tag = "mimo" });
                    _onboardEngine.Items.Add(new ComboBoxItem { Content = "仅 Microsoft 语音", Tag = "microsoft" });
                    _onboardEngine.Items.Add(new ComboBoxItem { Content = "虚拟歌姬", Tag = "vocaloid" });
                    _onboardEngine.SelectedIndex = _settings.Tts.Engine switch
                    {
                        "mimo" => 1,
                        "microsoft" => 2,
                        "vocaloid" => 3,
                        _ => 0,
                    };

                    _onboardMimo = new PasswordBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                        PasswordRevealMode = PasswordRevealMode.Peek,
                        Password = _settings.Tts.MimoKey ?? string.Empty,
                        PlaceholderText = "sk-…",
                    };

                    OnboardField(_onboardEngine, "语音引擎");
                    _onboardMimoGroup = OnboardField(_onboardMimo, "MiMo API Key（可选）");

                    StackPanel row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Margin = new Thickness(0, 6, 0, 0),
                    };

                    Button hear = new Button { Content = "试听" };
                    hear.Click += OnboardHear_Click;

                    _onboardAutoRead = new CheckBox
                    {
                        Content = "自动朗读回复",
                        IsChecked = _settings.Tts.AutoRead,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    row.Children.Add(hear);
                    row.Children.Add(_onboardAutoRead);
                    OnboardBody.Children.Add(row);

                    _onboardEngine.SelectionChanged += (object s, SelectionChangedEventArgs a) => UpdateOnboardMimoVisibility();
                    UpdateOnboardMimoVisibility();
                    break;
                }

                default:
                {
                    OnboardTitle("外观设置");
                    OnboardParagraph("最后挑一下主题和背景。这些之后随时能在「设置」里改。");

                    _onboardTheme = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                    };

                    _onboardTheme.Items.Add(new ComboBoxItem { Content = "跟随系统", Tag = "auto" });
                    _onboardTheme.Items.Add(new ComboBoxItem { Content = "浅色", Tag = "light" });
                    _onboardTheme.Items.Add(new ComboBoxItem { Content = "深色", Tag = "dark" });
                    _onboardTheme.SelectedIndex = _settings.Theme switch
                    {
                        "light" => 1,
                        "dark" => 2,
                        _ => 0,
                    };

                    _onboardBackground = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = 360,
                    };

                    _onboardBackground.Items.Add(new ComboBoxItem { Content = "云母", Tag = "mica" });
                    _onboardBackground.Items.Add(new ComboBoxItem { Content = "亚克力", Tag = "acrylic" });
                    _onboardBackground.Items.Add(new ComboBoxItem { Content = "纯色", Tag = "solid" });
                    _onboardBackground.Items.Add(new ComboBoxItem { Content = "图片", Tag = "image" });
                    _onboardBackground.SelectedIndex = _settings.Background.Mode switch
                    {
                        "acrylic" => 1,
                        "solid" => 2,
                        "image" => 3,
                        _ => 0,
                    };

                    OnboardField(_onboardTheme, "主题");
                    OnboardField(_onboardBackground, "背景");

                    OnboardExtra("做完这一步就可以开始了，剩下的都能在「设置」里慢慢调。");
                    break;
                }
            }

            // 新建的控件带着中文原文，这里当场换一遍，界面才不会等到下次切页才变
            Loc.ApplyTo(OnboardBody);
            Loc.ApplyTo(OnboardStepBar);
        }

        /// <summary>向导第一步：语言一选中就立刻换整个界面，不用等到点「下一步」。</summary>
        private void OnboardLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_onboardLanguage?.SelectedItem is not LanguageOption option)
            {
                return;
            }

            if (string.Equals(_settings.Language, option.Tag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.Language = option.Tag;
            SettingsStore.Save();

            // 先把下拉收起来：不收起的话，弹层里还留着旧语言的「自动」一项，
            // 看起来就像语言识别错了
            _onboardLanguage.IsDropDownOpen = false;

            ApplyLanguageToUi();

            // 下拉自己正处在事件里，重建这一步放到下一帧，避免把事件源清掉；
            // 如果这期间又换了语言，这次重建就作废，交给最后那一次
            string tag = option.Tag;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.Equals(_settings.Language, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                RenderOnboardingStep();
                Loc.ApplyTo(RootGrid);
            });
        }

        /// <summary>顶部步骤条：图标 + 名字，当前这一步蓝色并带下划线。</summary>
        private void BuildOnboardStepBar()
        {
            OnboardStepBar.Children.Clear();

            for (int i = 0; i < OnboardSteps.Length; i++)
            {
                bool active = i == _onboardStep;
                SolidColorBrush brush = new SolidColorBrush(active
                    ? Color.FromArgb(0xFF, 0x5A, 0xA0, 0xE8)
                    : Color.FromArgb(0xFF, 0xB5, 0xB7, 0xBA));

                Grid item = new Grid();
                item.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                item.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                    Glyph = OnboardSteps[i].Glyph,
                    FontSize = 15,
                    Foreground = brush,
                });
                row.Children.Add(new TextBlock
                {
                    Text = OnboardSteps[i].Label,
                    FontSize = 13,
                    Foreground = brush,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                Grid.SetRow(row, 0);
                item.Children.Add(row);

                Border underline = new Border
                {
                    Height = 2,
                    CornerRadius = new CornerRadius(1),
                    Margin = new Thickness(0, 8, 0, 0),
                    Background = active ? brush : new SolidColorBrush(Colors.Transparent),
                };
                Grid.SetRow(underline, 1);
                item.Children.Add(underline);

                OnboardStepBar.Children.Add(item);
            }
        }

        /// <summary>服务商换了：内置的就带出它的 Key，Custom API 就亮出地址和模型。</summary>
        private void OnboardProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem? item = _onboardProvider?.SelectedItem as ComboBoxItem;
            OnboardProviderChoice? choice = item?.Tag as OnboardProviderChoice;
            if (choice == null)
            {
                return;
            }

            bool custom = choice.Index < 0;
            if (_onboardBaseGroup != null)
            {
                _onboardBaseGroup.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_onboardModelGroup != null)
            {
                _onboardModelGroup.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_onboardKey == null)
            {
                return;
            }

            if (!custom && choice.Index < _settings.Providers.Count)
            {
                ProviderProfile picked = _settings.Providers[choice.Index];
                _onboardKey.Password = picked.ApiKey ?? string.Empty;
                _onboardKey.PlaceholderText = IsOllama(picked) ? "（本地模型，无需填写）" : "sk-…";
                return;
            }

            ProviderProfile? existing = _settings.Providers.Find(p =>
                p.Id.StartsWith("custom-", StringComparison.OrdinalIgnoreCase));
            _onboardKey.Password = existing?.ApiKey ?? string.Empty;
            _onboardKey.PlaceholderText = "sk-…";

            if (_onboardBaseUrl != null)
            {
                _onboardBaseUrl.Text = existing?.BaseUrl ?? string.Empty;
            }

            if (_onboardModel != null)
            {
                _onboardModel.Text = existing?.Model ?? string.Empty;
            }
        }

        /// <summary>Microsoft 语音和虚拟歌姬都用不上 MiMo 的 Key，选了就收起那一行。</summary>
        private void UpdateOnboardMimoVisibility()
        {
            if (_onboardMimoGroup == null)
            {
                return;
            }

            string engine = _onboardEngine == null ? "auto" : SelectedTag(_onboardEngine, "auto");
            bool needed = engine != "microsoft" && engine != "vocaloid";
            _onboardMimoGroup.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnboardTitle(string text)
        {
            OnboardBody.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 25,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 2),
            });
        }

        private void OnboardParagraph(string text)
        {
            OnboardBody.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC9, 0xCB, 0xCE)),
                LineHeight = 21,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        private void OnboardExtra(string text)
        {
            OnboardBody.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x9F, 0xA2, 0xA6)),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        /// <summary>一行「小标题 + 控件」，和 Fluent Launcher 那边一样窄窄一列。</summary>
        private StackPanel OnboardField(FrameworkElement control, string label)
        {
            StackPanel group = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            group.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC9, 0xCB, 0xCE)),
            });

            group.Children.Add(control);
            OnboardBody.Children.Add(group);
            return group;
        }

        private static bool IsOllama(ProviderProfile provider)
        {
            return string.Equals(provider.Protocol, "ollama", StringComparison.OrdinalIgnoreCase);
        }

        private void OnboardHear_Click(object sender, RoutedEventArgs e)
        {
            SaveVoiceSlice();

            OnboardNote.Text = "试听中…";
            OnboardNote.Visibility = Visibility.Visible;
            _ = HearOnboardingAsync();
        }

        private async Task HearOnboardingAsync()
        {
            string error = await Speech.SpeakAsync(_settings, "你好，我是 DomeOS AI，这是语音试听。");
            OnboardNote.Text = error.Length == 0
                ? (Speech.LastNotice.Length > 0 ? Speech.LastNotice : "试听完成。")
                : "合成失败：" + error;
            OnboardNote.Visibility = Visibility.Visible;
        }

        /// <summary>把每一步填的东西落进配置里；返回 false 表示这一步还差必填项。</summary>
        private bool SaveOnboardingStep()
        {
            switch (_onboardStep)
            {
                case 0:
                    if (_onboardLanguage != null && _onboardLanguage.SelectedItem is LanguageOption option)
                    {
                        _settings.Language = option.Tag;
                    }

                    SettingsStore.Save();
                    return true;

                case 1:
                    return SaveOnboardProvider();

                case 2:
                    SaveVoiceSlice();
                    return true;

                case 3:
                    SaveAppearanceSlice();
                    return true;
            }

            return true;
        }

        /// <summary>服务商这一步：内置的直接存 Key，Custom API 就新建（或复用）一条自定义服务商。</summary>
        private bool SaveOnboardProvider()
        {
            ComboBoxItem? item = _onboardProvider?.SelectedItem as ComboBoxItem;
            OnboardProviderChoice? choice = item?.Tag as OnboardProviderChoice;
            if (choice == null)
            {
                return true;
            }

            if (choice.Index >= 0 && choice.Index < _settings.Providers.Count)
            {
                _settings.ActiveProvider = choice.Index;

                if (_onboardKey != null)
                {
                    _settings.Providers[choice.Index].ApiKey = _onboardKey.Password;
                }

                SettingsStore.Save();
                ApplyProviderToShell();
                return true;
            }

            string baseUrl = (_onboardBaseUrl?.Text ?? string.Empty).Trim();
            string model = (_onboardModel?.Text ?? string.Empty).Trim();
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                OnboardNote.Text = "Custom API 至少要把接口地址和模型填上。";
                OnboardNote.Visibility = Visibility.Visible;
                return false;
            }

            int index = _settings.Providers.FindIndex(p =>
                p.Id.StartsWith("custom-", StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                _settings.Providers.Add(new ProviderProfile
                {
                    Id = Providers.CustomId(string.Empty),
                    Name = "Custom API",
                    Protocol = "openai",
                    BaseUrl = string.Empty,
                    Model = string.Empty,
                    SystemPrompt = Persona.DefaultSys,
                    Temperature = 0.7,
                });

                index = _settings.Providers.Count - 1;
            }

            ProviderProfile custom = _settings.Providers[index];
            custom.Name = "Custom API";
            custom.Protocol = "openai";
            custom.BaseUrl = baseUrl;
            custom.Model = model;
            custom.ApiKey = _onboardKey?.Password ?? string.Empty;

            _settings.ActiveProvider = index;
            SettingsStore.Save();
            ApplyProviderToShell();
            return true;
        }

        private void SaveVoiceSlice()
        {
            TtsSettings tts = _settings.Tts;

            if (_onboardEngine != null)
            {
                tts.Engine = SelectedTag(_onboardEngine, "auto");
            }

            if (_onboardMimo != null)
            {
                tts.MimoKey = _onboardMimo.Password;
            }

            if (_onboardAutoRead != null)
            {
                tts.AutoRead = _onboardAutoRead.IsChecked == true;
            }

            SettingsStore.Save();
        }

        private void SaveAppearanceSlice()
        {
            if (_onboardTheme != null)
            {
                _settings.Theme = SelectedTag(_onboardTheme, "auto");
                _theme = ParseTheme(_settings.Theme);
            }

            if (_onboardBackground != null)
            {
                _settings.Background.Mode = SelectedTag(_onboardBackground, "mica");
            }

            SettingsStore.Save();
        }

        // ------------------------------------------------------------------ 托盘

        /// <summary>托盘菜单点了哪一项。已经在小图标那层映射好了，这里只管把主窗口带过来。</summary>
        private void TrayAction(string key)
        {
            if (key == "exit")
            {
                Close();
                return;
            }

            BringToFront();

            switch (key)
            {
                case "chat":
                    NavView.SelectedItem = NavView.MenuItems[0];
                    ShowPanel("chat");
                    break;

                case "workspace":
                    NavView.SelectedItem = NavView.MenuItems[1];
                    ShowPanel("workspace");
                    break;

                case "settings":
                    NavView.SelectedItem = NavView.SettingsItem;
                    ShowPanel("settings");
                    break;
            }
        }

        /// <summary>把窗口从最小化里拉回来并顶到最前。</summary>
        private void BringToFront()
        {
            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter
                    && presenter.State == OverlappedPresenterState.Minimized)
                {
                    presenter.Restore();
                }

                AppWindow.Show();
            }
            catch
            {
                // 展示器不认这个方法也不该把人卡住
            }

            Activate();

            if (ChatPanel.Visibility == Visibility.Visible && _current == null)
            {
                ShowGallery();
            }
        }

        // ------------------------------------------------------------------ settings -> shell

        private void ApplySettingsToShell()
        {
            ApplyWindowGeometry();

            NavView.OpenPaneLength = _settings.Ui.SidebarWidth;

            _theme = ParseTheme(_settings.Theme);
            ApplyTheme();

            DeepThinkChip.IsChecked = _settings.Agent.DeepThinking;
            WebSearchChip.IsChecked = _settings.Agent.WebSearch;

            ApplyProviderToShell();
            ApplyWorkspaceToShell();
            RefreshPermissionChip();
            ApplyBackground();
            ApplyOrb();
        }

        private void ApplyWindowGeometry()
        {
            try
            {
                UiSettings ui = _settings.Ui;
                int width = Math.Max(640, ui.WindowWidth);
                int height = Math.Max(460, ui.WindowHeight);

                Windows.Graphics.RectInt32 work = WorkArea();

                int x;
                int y;
                if (ui.WindowX == 0 && ui.WindowY == 0)
                {
                    x = work.X + Math.Max(0, (work.Width - width) / 2);
                    y = work.Y + Math.Max(0, (work.Height - height) / 2);
                }
                else
                {
                    x = ui.WindowX;
                    y = ui.WindowY;
                }

                int maxX = Math.Max(work.X, work.X + work.Width - width);
                int maxY = Math.Max(work.Y, work.Y + work.Height - height);
                x = Math.Clamp(x, work.X, maxX);
                y = Math.Clamp(y, work.Y, maxY);

                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
            }
            catch
            {
                // a window that cannot be placed still opens somewhere
            }
        }

        private Windows.Graphics.RectInt32 WorkArea()
        {
            try
            {
                DisplayArea? area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                if (area != null)
                {
                    return area.WorkArea;
                }
            }
            catch
            {
                // fall through to the default box
            }

            return new Windows.Graphics.RectInt32(0, 0, 1920, 1080);
        }

        /// <summary>The window box is the one piece of state only the window knows.</summary>
        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            try
            {
                Windows.Graphics.PointInt32 position = AppWindow.Position;
                Windows.Graphics.SizeInt32 size = AppWindow.Size;
                _settings.Ui.WindowX = position.X;
                _settings.Ui.WindowY = position.Y;
                _settings.Ui.WindowWidth = size.Width;
                _settings.Ui.WindowHeight = size.Height;
            }
            catch
            {
                // last known numbers stay
            }

            SettingsStore.Save();

            _tray?.Dispose();
            _tray = null;

            // the orb is a second window, so it has to be taken down with the main one
            if (_quickBar != null)
            {
                _quickBar.Close();
                _quickBar = null;
            }

            if (_orbWindow != null)
            {
                _orbWindow.Close();
                _orbWindow = null;
            }
        }

        private void ApplyProviderToShell()
        {
            ProviderProfile provider = _settings.Active();
            string model = string.IsNullOrEmpty(provider.Model) ? "未选择模型" : provider.Model;

            ProviderNameText.Text = string.IsNullOrEmpty(provider.Name) ? "未配置服务商" : provider.Name;
            ModelNameText.Text = model;
            ModelChipText.Text = model;
        }

        private void RefreshPermissionChip()
        {
            bool machine = IsMachineScope();
            bool auto = _settings.Agent.AutoApprove;

            PermissionText.Text = (machine ? "整个电脑" : "工作区") + " · " + (auto ? "自动批准" : "每次询问");
            PermissionText.Style = (Style)Application.Current.Resources[machine ? "DomeTextWarn" : "DomeTextOk"];
            PermissionIcon.Style = (Style)Application.Current.Resources[machine ? "DomeGlyphWarn" : "DomeGlyphOk"];
        }

        private bool IsMachineScope()
        {
            return string.Equals(_settings.Agent.Scope, "machine", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ 背景

        /// <summary>
        /// 把背景设置铺到窗口上。云母和亚克力是系统的两种材料，交给 SystemBackdrop；
        /// 纯色和图片则由 RootGrid 底下那一层自己画，同时把外壳调成透明，不然它会把背景盖住。
        /// </summary>
        private void ApplyBackground()
        {
            BackgroundSettings background = _settings.Background;

            // 图片模式下还没挑图的时候，别铺那层纯黑：整窗一片黑，主人会以为程序坏了。
            // 这种时候退回系统材料（云母），跟什么都没设置时看起来一样。
            bool imageReady = background.Mode != "image"
                              || (background.File.Length > 0 && File.Exists(background.File));
            string mode = imageReady ? background.Mode : "mica";

            bool systemMaterial = mode == "mica" || mode == "acrylic";
            bool customLayer = !systemMaterial;

            try
            {
                SystemBackdrop = mode switch
                {
                    "mica" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                    "acrylic" => new DesktopAcrylicBackdrop(),
                    _ => null,
                };
            }
            catch
            {
                // 老运行时认不出背景材料，下面的层照样能画
                SystemBackdrop = null;
            }

            SetShellTransparent(customLayer);

            if (!customLayer)
            {
                BackdropLayer.Visibility = Visibility.Collapsed;
                return;
            }

            BackdropLayer.Visibility = Visibility.Visible;

            if (mode == "solid")
            {
                BackdropSolid.Background = new SolidColorBrush(ParseHex(background.Color, Colors.Black));
                BackdropImage.Source = null;
                BackdropImage.Visibility = Visibility.Collapsed;
                BackdropDim.Opacity = 0;
                return;
            }

            BackdropSolid.Background = new SolidColorBrush(Colors.Black);
            BackdropImage.Visibility = Visibility.Visible;
            BackdropImage.Stretch = background.Fit == "contain" ? Stretch.Uniform : Stretch.UniformToFill;
            BackdropDim.Opacity = background.Dim;
            LoadBackdropImage(background.File);
        }

        /// <summary>自己铺背景的时候，外壳得让开：NavigationView 默认会把内容区涂成不透明的。</summary>
        private void SetShellTransparent(bool transparent)
        {
            NavView.Background = transparent ? new SolidColorBrush(Colors.Transparent) : null;

            if (transparent)
            {
                NavView.Resources["NavigationViewContentBackground"] = new SolidColorBrush(Colors.Transparent);
                NavView.Resources["NavigationViewContentGridBorderBrush"] = new SolidColorBrush(Colors.Transparent);
                // 侧栏跟着透明之后，它底下就是主人选的那层底色了。浅色主题里文字是深色的，
                // 压在深色底上就一个字也看不见——所以侧栏自己垫一层主题色的半透明纱。
                ApplyPaneScrim();

                // 标题栏同理：那行应用名也是深色的，压在深色底上一样看不清
                AppTitleBar.Background = new SolidColorBrush(ScrimColor());
            }
            else
            {
                NavView.Resources.Remove("NavigationViewContentBackground");
                NavView.Resources.Remove("NavigationViewContentGridBorderBrush");
                NavView.Resources.Remove("NavigationViewDefaultPaneBackground");
                NavView.Resources.Remove("NavigationViewExpandedPaneBackground");
                AppTitleBar.Background = null;
            }
        }

        /// <summary>
        /// 侧栏自己的垫底纱。自己铺背景时 NavigationView 整块都透明了，侧栏底下直接就是主人选的
        /// 底色；浅色主题配深色底（或者干脆是黑的）就会一个字都看不见。这层半透明的主题色
        /// 既保证字读得出来，又留得住一点背景的通透感。
        /// </summary>
        private void ApplyPaneScrim()
        {
            SolidColorBrush scrim = new SolidColorBrush(ScrimColor());

            NavView.Resources["NavigationViewDefaultPaneBackground"] = scrim;
            NavView.Resources["NavigationViewExpandedPaneBackground"] = scrim;
        }

        /// <summary>垫底纱的颜色：跟着主题走，浅色配浅纱、深色配深纱。</summary>
        private Color ScrimColor()
        {
            return IsEffectiveDark()
                ? Color.FromArgb(0xE6, 0x2B, 0x2B, 0x2B)
                : Color.FromArgb(0xE6, 0xF6, 0xF6, 0xF6);
        }

        private void LoadBackdropImage(string path)
        {
            try
            {
                if (path.Length > 0 && File.Exists(path))
                {
                    BackdropImage.Source = new BitmapImage(new Uri(path));
                    BackgroundFileText.Text = path;
                }
                else
                {
                    BackdropImage.Source = null;
                    BackgroundFileText.Text = path.Length > 0 ? "（这张图已经不在原来的位置了）" : "未设置";
                }
            }
            catch
            {
                BackdropImage.Source = null;
            }
        }

        private static string ToHex(Color color)
        {
            return color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private void RefreshBackgroundFields()
        {
            BackgroundSettings background = _settings.Background;

            _loading = true;
            BackgroundModeCombo.SelectedIndex = background.Mode switch
            {
                "acrylic" => 1,
                "solid" => 2,
                "image" => 3,
                _ => 0,
            };
            BackgroundColorPicker.Color = ParseHex(background.Color, Colors.Black);
            BackgroundDimSlider.Value = background.Dim;
            BackgroundFitCombo.SelectedIndex = background.Fit == "contain" ? 1 : 0;
            BackgroundFileText.Text = background.File.Length > 0 ? background.File : "未设置";
            _loading = false;

            RefreshBackgroundRows();
        }

        /// <summary>当前模式用不上的那一行就收起来，免得摆一堆没用的控件。</summary>
        private void RefreshBackgroundRows()
        {
            string mode = _settings.Background.Mode;
            BackgroundColorRow.Visibility = mode == "solid" ? Visibility.Visible : Visibility.Collapsed;
            BackgroundImageRow.Visibility = mode == "image" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackgroundModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Background.Mode = BackgroundModeCombo.SelectedIndex switch
            {
                1 => "acrylic",
                2 => "solid",
                3 => "image",
                _ => "mica",
            };

            SettingsStore.Save();
            RefreshBackgroundRows();
            ApplyBackground();
        }

        private void BackgroundColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_loading)
            {
                return;
            }

            _settings.Background.Color = ToHex(args.NewColor);
            SettingsStore.Save();

            if (_settings.Background.Mode == "solid")
            {
                ApplyBackground();
            }
        }

        private void BackgroundDimSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Background.Dim = e.NewValue;
            SettingsStore.Save();
            BackdropDim.Opacity = e.NewValue;
        }

        private void BackgroundFitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Background.Fit = BackgroundFitCombo.SelectedIndex == 1 ? "contain" : "cover";
            SettingsStore.Save();
            ApplyBackground();
        }

        private async void PickBackground_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Windows.Storage.Pickers.FileOpenPicker picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".gif");
                picker.FileTypeFilter.Add(".webp");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

                Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    return;
                }

                // 复制一份进应用数据目录，原图之后挪走或删掉都不影响
                Directory.CreateDirectory(SettingsStore.DataDir);
                string target = Path.Combine(SettingsStore.DataDir, "background" + Path.GetExtension(file.Name).ToLowerInvariant());
                File.Copy(file.Path, target, true);

                _settings.Background.File = target;
                _settings.Background.Mode = "image";
                SettingsStore.Save();

                RefreshBackgroundFields();
                ApplyBackground();
                SettingsStatus.Text = "背景图已复制到 " + target;
            }
            catch (Exception error)
            {
                SettingsStatus.Text = "选图失败：" + error.Message;
            }
        }

        private void ClearBackground_Click(object sender, RoutedEventArgs e)
        {
            _settings.Background.File = string.Empty;
            SettingsStore.Save();

            RefreshBackgroundFields();
            ApplyBackground();
            SettingsStatus.Text = "背景图已经清掉了。";
        }

        // ------------------------------------------------------------------ theme

        private void ApplyTheme()
        {
            if (_theme == DomeThemeMode.Light)
            {
                RootGrid.RequestedTheme = ElementTheme.Light;
            }
            else if (_theme == DomeThemeMode.Dark)
            {
                RootGrid.RequestedTheme = ElementTheme.Dark;
            }
            else
            {
                RootGrid.RequestedTheme = ElementTheme.Default;
            }

            ApplyCaptionColors();
        }

        private static DomeThemeMode ParseTheme(string value)
        {
            if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
            {
                return DomeThemeMode.Light;
            }

            if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return DomeThemeMode.Dark;
            }

            return DomeThemeMode.System;
        }

        private static string ThemeName(DomeThemeMode mode)
        {
            if (mode == DomeThemeMode.Light)
            {
                return "light";
            }

            if (mode == DomeThemeMode.Dark)
            {
                return "dark";
            }

            return "auto";
        }

        private bool IsEffectiveDark()
        {
            if (_theme == DomeThemeMode.Dark)
            {
                return true;
            }

            if (_theme == DomeThemeMode.Light)
            {
                return false;
            }

            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }

        /// <summary>The system caption buttons need recolouring by hand once a theme is forced.</summary>
        private void ApplyCaptionColors()
        {
            try
            {
                AppWindowTitleBar bar = AppWindow.TitleBar;
                bool dark = IsEffectiveDark();
                Color fg = dark ? Colors.White : Color.FromArgb(255, 28, 28, 32);

                bar.ButtonBackgroundColor = Colors.Transparent;
                bar.ButtonInactiveBackgroundColor = Colors.Transparent;
                bar.ButtonForegroundColor = fg;
                bar.ButtonHoverForegroundColor = fg;
                bar.ButtonPressedForegroundColor = fg;
                bar.ButtonInactiveForegroundColor = dark
                    ? Color.FromArgb(150, 255, 255, 255)
                    : Color.FromArgb(130, 0, 0, 0);
                bar.ButtonHoverBackgroundColor = dark
                    ? Color.FromArgb(30, 255, 255, 255)
                    : Color.FromArgb(18, 0, 0, 0);
                bar.ButtonPressedBackgroundColor = dark
                    ? Color.FromArgb(46, 255, 255, 255)
                    : Color.FromArgb(30, 0, 0, 0);
            }
            catch
            {
                // the caption may be system drawn
            }
        }

        // ------------------------------------------------------------------ transcript

        private void SeedConversations()
        {
            ChatStore.Load();

            // 存档在、但这次没读出来时不要拿空列表去覆盖它
            if (ChatStore.Items.Count == 0 && !ChatStore.LoadFailed)
            {
                Conversation first = ChatStore.Create("新的对话");
                first.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Text = "（慢吞吞浮上来）本小姐在这儿。填上服务商的钥匙就能聊；等要动文件的时候，再去「工作区」挑个文件夹。",
                    Time = ChatStore.Now(),
                });
                ChatStore.Save();
            }

            RefreshGallery();
        }

        // ------------------------------------------------------------------ the gallery

        /// <summary>Rebuilds the tile grid. The tiles read their own text off the conversation, so
        /// the whole grid is re-fed rather than any single tile being poked at.</summary>
        private void RefreshGallery()
        {
            bool onlyFavorite = FavoriteOnlyToggle.IsChecked == true;

            List<Conversation> shown = new List<Conversation>();
            foreach (Conversation conversation in ChatStore.Items)
            {
                if (!onlyFavorite || conversation.Favorite)
                {
                    shown.Add(conversation);
                }
            }

            ConversationGrid.ItemsSource = shown;
            GalleryCountText.Text = shown.Count + " 段对话" + (onlyFavorite ? "（收藏）" : string.Empty);
            GalleryEmptyText.Text = shown.Count == 0 ? "还没有对话，点「新对话」开始" : "点一张卡片打开对话";

            if (!_openingThread)
            {
                RefreshNavThreadsDeferred();
            }
        }

        /// <summary>
        /// Rebuilds the sidebar list on the next dispatcher turn. Doing it straight from a selection
        /// handler mutates MenuItems while NavigationView is still working that selection out, which
        /// loses the selection and eventually crashes.
        /// </summary>
        private void RefreshNavThreadsDeferred()
        {
            if (_navRebuildQueued)
            {
                return;
            }

            _navRebuildQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _navRebuildQueued = false;
                RefreshNavThreads();
            });
        }

        /// <summary>The conversation the sidebar is currently pointing at, if any.</summary>
        private string SelectedThreadId()
        {
            string? tag = (NavView.SelectedItem as NavigationViewItem)?.Tag as string;
            if (tag != null && tag.StartsWith("thread:", StringComparison.Ordinal))
            {
                return tag.Substring("thread:".Length);
            }

            return string.Empty;
        }

        /// <summary>
        /// Mirrors the newest conversations into the sidebar as well, so a thread can be reopened
        /// without going back to the gallery first. The tile grid stays where it is.
        /// </summary>
        private void RefreshNavThreads()
        {
            string keepId = SelectedThreadId();
            bool wasRebuilding = _navRebuilding;
            _navRebuilding = true;

            try
            {
                for (int i = NavView.MenuItems.Count - 1; i >= 2; i--)
                {
                    object entry = NavView.MenuItems[i];
                    string? tag = (entry as NavigationViewItem)?.Tag as string;

                    if (entry is NavigationViewItemHeader || (tag != null && tag.StartsWith("thread:", StringComparison.Ordinal)))
                    {
                        NavView.MenuItems.RemoveAt(i);
                    }
                }

                if (ChatStore.Items.Count > 0)
                {
                    NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "最近对话" });

                    const int MaxSidebarThreads = 8;
                    int shown = 0;
                    foreach (Conversation conversation in ChatStore.Items)
                    {
                        if (shown >= MaxSidebarThreads)
                        {
                            break;
                        }

                        NavView.MenuItems.Add(ThreadNavItem(conversation));

                        shown++;
                    }
                }

                // 把还得留着的那一项重新指回去，列表重建不该顺手把当前页面也换掉
                if (keepId.Length > 0)
                {
                    foreach (object entry in NavView.MenuItems)
                    {
                        if (entry is NavigationViewItem item && (item.Tag as string) == "thread:" + keepId)
                        {
                            NavView.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (NavView.SelectedItem == null && NavView.MenuItems.Count > 0)
                {
                    NavView.SelectedItem = NavView.MenuItems[0];
                }
            }
            finally
            {
                _navRebuilding = wasRebuilding;
            }
        }

        /// <summary>
        /// 侧栏里的一条对话。右键菜单挂在它自己身上：在「最近对话」里右击某一项就能删掉那一段，
        /// 不用先把它打开再跑到对话页去删。
        /// </summary>
        private NavigationViewItem ThreadNavItem(Conversation conversation)
        {
            NavigationViewItem item = new NavigationViewItem
            {
                Content = conversation.Title,
                Tag = "thread:" + conversation.Id,
                Icon = new FontIcon
                {
                    Glyph = "\uE8BD",
                    FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                },
            };

            MenuFlyout menu = new MenuFlyout();

            MenuFlyoutItem delete = new MenuFlyoutItem
            {
                Text = "删除这段对话",
                Icon = new FontIcon { Glyph = "\uE74D" },
            };

            string id = conversation.Id;
            delete.Click += (object sender, RoutedEventArgs e) => DeleteThreadFromSidebar(id);

            menu.Items.Add(delete);
            item.ContextFlyout = menu;

            return item;
        }

        /// <summary>侧栏右键删对话：先问一声，删完把列表和画廊一起刷新。</summary>
        private async void DeleteThreadFromSidebar(string id)
        {
            Conversation? conversation = ChatStore.Items.Find(entry => entry.Id == id);
            if (conversation == null)
            {
                return;
            }

            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "删除这段对话？",
                Content = "「" + conversation.Title + "」会连同里面的消息一起删掉，删了就找不回来了。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            bool wasOpen = _current == conversation;
            ChatStore.Remove(conversation);

            if (wasOpen)
            {
                _current = null;
                ShowGallery();
            }
            else
            {
                RefreshGallery();
            }
        }

        private void ConversationGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Conversation conversation)
            {
                OpenConversation(conversation);
            }
        }

        private void OpenConversation(Conversation conversation)
        {
            _current = conversation;
            _lastAssistantText = string.Empty;

            TranscriptPanel.Children.Clear();
            _nikeImages.Clear();
            _assistantBubbles.Clear();
            foreach (ChatMessage message in conversation.Messages)
            {
                if (message.Role == "user")
                {
                    AddMessage("DomeUserMessage", message.Text);
                }
                else
                {
                    // 存档里的回答也按 Markdown 重排一遍，重新打开对话不会变成一坨纯文本
                    TranscriptPanel.Children.Add(CreateAssistantBubble(message.Text, string.Empty));
                    _lastAssistantText = message.Text;
                }
            }

            _loading = true;
            ThreadFavoriteToggle.IsChecked = conversation.Favorite;
            _loading = false;

            ThreadTitleText.Text = conversation.Title;
            GalleryPanel.Visibility = Visibility.Collapsed;
            ThreadPanel.Visibility = Visibility.Visible;
            // 标题就写在对话自己的那一行里，导航栏的大标题再来一遍就成了重影
            NavView.Header = null;

            ScrollTranscriptToEnd();
            ComposerBox.Focus(FocusState.Programmatic);
        }

        private void ShowGallery()
        {
            GalleryPanel.Visibility = Visibility.Visible;
            ThreadPanel.Visibility = Visibility.Collapsed;
            // 和大标题保持一致：对话页不出现在导航栏的大标题里
            NavView.Header = null;
            RefreshGallery();
        }

        private void BackToGallery_Click(object sender, RoutedEventArgs e)
        {
            ShowGallery();
        }

        private void NewConversation_Click(object sender, RoutedEventArgs e)
        {
            Conversation conversation = ChatStore.Create("新的对话");
            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Text = "新的一段对话。想聊什么？",
                Time = ChatStore.Now(),
            });
            ChatStore.Save();

            RefreshGallery();
            OpenConversation(conversation);
        }

        private void FavoriteOnly_Click(object sender, RoutedEventArgs e)
        {
            RefreshGallery();
        }

        private void ThreadFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _loading)
            {
                return;
            }

            _current.Favorite = ThreadFavoriteToggle.IsChecked == true;
            ChatStore.Save();
        }

        private async void DeleteConversation_Click(object sender, RoutedEventArgs e)
        {
            Conversation? conversation = _current;
            if (conversation == null)
            {
                return;
            }

            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "删除这段对话？",
                Content = "「" + conversation.Title + "」会连同里面的消息一起删掉，删了就找不回来了。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            ChatStore.Remove(conversation);
            _current = null;
            ShowGallery();
        }

        /// <summary>
        /// Every message that reaches the transcript is also written into the open conversation, so
        /// the archive on disk always matches what is on screen.
        /// </summary>
        private void RecordMessage(string role, string text)
        {
            Conversation? conversation = _current;
            if (conversation == null)
            {
                return;
            }

            conversation.Messages.Add(new ChatMessage
            {
                Role = role,
                Text = text,
                Time = ChatStore.Now(),
            });

            conversation.Updated = ChatStore.Now();

            if (role == "user" && (conversation.Title.Length == 0 || conversation.Title == "新的对话"))
            {
                conversation.Title = ChatStore.TitleFrom(text);
                ThreadTitleText.Text = conversation.Title;
            }

            ChatStore.Save();
        }

        private void AddMessage(string templateKey, string text)
        {
            DataTemplate? template = ChatPanel.Resources[templateKey] as DataTemplate;
            if (template != null)
            {
                ContentControl item = new ContentControl
                {
                    ContentTemplate = template,
                    Content = text,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };

                TranscriptPanel.Children.Add(item);
            }

            ScrollTranscriptToEnd();
        }

        private void ScrollTranscriptToEnd()
        {
            TranscriptScroller.UpdateLayout();
            TranscriptScroller.ChangeView(null, TranscriptScroller.ScrollableHeight, null, true);
        }

        /// <summary>对话那一列跟着视口定宽（上限 780），气泡才能贴着左右两边站好。</summary>
        private void TranscriptScroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double available = e.NewSize.Width
                               - TranscriptScroller.Padding.Left
                               - TranscriptScroller.Padding.Right;
            TranscriptPanel.Width = Math.Max(280, Math.Min(780, available));
        }

        private void AddUserMessage(string text)
        {
            AddMessage("DomeUserMessage", text);
            RecordMessage("user", text);
        }

        private void AddAssistantMessage(string text)
        {
            AddAssistantMessage(text, null, string.Empty);
        }

        /// <summary>
        /// Adds an answer, and reads it out when asked to. A null <paramref name="speak"/> means the
        /// auto-read setting decides; the easter eggs force it on and pick their own language.
        /// </summary>
        private void AddAssistantMessage(string text, bool? speak, string languageTag)
        {
            AddAssistantBubble(text, speak ?? _settings.Tts.AutoRead, languageTag);
        }

        /// <summary>
        /// 一条回答：正文按 Markdown 排出来，装进一个跟着深浅主题走的卡片气泡，
        /// 底下跟着一个小喇叭。喇叭贴着这条消息，读到哪儿一目了然。
        /// </summary>
        private void AddAssistantBubble(string text, bool speak, string languageTag)
        {
            TranscriptPanel.Children.Add(CreateAssistantBubble(text, languageTag));
            RecordMessage("assistant", text);
            _lastAssistantText = text;

            if (speak)
            {
                _ = SpeakAsync(text, languageTag);
            }

            ScrollTranscriptToEnd();
        }

        /// <summary>回答气泡：深色模式是深灰卡片，浅色模式是浅灰卡片，边框也跟着主题走。</summary>
        private Border CreateAssistantBubble(string text, string languageTag)
        {
            StackPanel inner = new StackPanel { Spacing = 4 };
            inner.Children.Add(Markdown.Build(text));
            inner.Children.Add(SpeakButtonFor(text, languageTag));

            Border bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 620,
                Padding = new Thickness(14, 10, 14, 8),
                CornerRadius = new CornerRadius(8),
                Child = inner,
            };

            _assistantBubbles.Add(bubble);
            ApplyBubbleTheme(bubble);
            return bubble;
        }

        /// <summary>主题一变，已经贴在记录里的回答气泡也跟着换色。</summary>
        private void ApplyBubbleTheme(Border bubble)
        {
            bool dark = IsDarkNow();
            bubble.Background = new SolidColorBrush(dark
                ? Color.FromArgb(0xFF, 0x2D, 0x2E, 0x30)
                : Color.FromArgb(0xFF, 0xF7, 0xF7, 0xF7));
            bubble.BorderBrush = new SolidColorBrush(dark
                ? Color.FromArgb(0xFF, 0x40, 0x41, 0x44)
                : Color.FromArgb(0xFF, 0xE2, 0xE2, 0xE2));
            bubble.BorderThickness = new Thickness(1);
        }

        /// <summary>每条回答旁边那个小喇叭。按一下念这条，再按一下停。</summary>
        private Button SpeakButtonFor(string text, string languageTag)
        {
            Button button = new Button
            {
                Padding = new Thickness(6, 2, 8, 2),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                    Glyph = "\uE767",
                    FontSize = 12,
                },
            };

            ToolTipService.SetToolTip(button, "朗读这条回答");
            button.Click += (object sender, RoutedEventArgs e) => ToggleSpeak(text, languageTag, button);
            return button;
        }

        /// <summary>点这条的喇叭：正在念就停，没在念就念这一条。</summary>
        private void ToggleSpeak(string text, string languageTag, Button button)
        {
            if (Speech.IsSpeaking && _speakingButton == button)
            {
                Speech.Stop();
                return;
            }

            if (Speech.IsSpeaking)
            {
                Speech.Stop();
            }

            _speakingButton = button;
            button.Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                Glyph = "\uE71A",
                FontSize = 12,
            };

            _ = SpeakAsync(text, languageTag);
        }

        /// <summary>
        /// An easter-egg bubble: a card in the transcript that opens its link only when it is
        /// clicked, rather than firing the browser off the moment the message is sent.
        /// </summary>
        private void AddEggBubble(string title, string url, bool warm)
        {
            StackPanel content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock
            {
                Text = "DOMEOS AI",
                Style = (Style)Application.Current.Resources["DomeEggWho"],
            });
            content.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources[warm ? "DomeEggTitleWarm" : "DomeEggTitleCool"],
            });

            Button bubble = new Button
            {
                Style = (Style)Application.Current.Resources["DomeEggBubble"],
                Content = content,
            };

            ToolTipService.SetToolTip(bubble, "打开 " + url);
            bubble.Click += (object sender, RoutedEventArgs e) => _ = Eggs.OpenUrlAsync(url);

            TranscriptPanel.Children.Add(bubble);
            ScrollTranscriptToEnd();
        }

        // ------------------------------------------------------------------ composer

        /// <summary>
        /// Enter sends, Shift+Enter breaks the line. This has to be PreviewKeyDown: on a TextBox with
        /// AcceptsReturn the control's own key handling runs before an instance KeyDown handler, so a
        /// plain KeyDown would send the message and still leave a newline behind. The modifier is read
        /// from the keyboard rather than tracked in a field, so it cannot go stale on a focus change.
        /// </summary>
        private void ComposerBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            bool shift = IsKeyDown(VirtualKey.Shift);
            bool control = IsKeyDown(VirtualKey.Control);

            if (e.Key == VirtualKey.L && shift == false && control)
            {
                TranscriptPanel.Children.Clear();
                _nikeImages.Clear();
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Enter && shift == false)
            {
                Send();
                e.Handled = true;
            }

            // anything else, including Shift+Enter, is left to the TextBox
        }

        private static bool IsKeyDown(VirtualKey key)
        {
            try
            {
                Windows.UI.Core.CoreVirtualKeyStates state =
                    Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
                return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            }
            catch
            {
                return false;
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            Send();
        }

        private void Send()
        {
            string text = ComposerBox.Text;
            ComposerBox.Text = string.Empty;
            SendText(text);
        }

        /// <summary>One send path, whichever surface the text came from.</summary>
        private async void SendText(string? raw)
        {
            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return;
            }

            // A hidden input takes over the send before any of it reaches a model.
            EggKind egg = Eggs.Match(text);
            if (egg != EggKind.None)
            {
                if (_current != null)
                {
                    AddUserMessage(text);
                }

                RunEgg(egg);
                return;
            }

            AddUserMessage(text);
            await RunAgentAsync();
        }

        /// <summary>Sends text typed into the command bar, bringing the chat forward first.</summary>
        private void SendFromQuickBar(string text)
        {
            Activate();

            if (NavView.SelectedItem != NavView.MenuItems[0])
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }

            if (ChatPanel.Visibility != Visibility.Visible)
            {
                ShowPanel("chat");
            }

            if (_current == null)
            {
                Conversation conversation = ChatStore.Create("新的对话");
                conversation.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Text = "新的一段对话。想聊什么？",
                    Time = ChatStore.Now(),
                });
                ChatStore.Save();
                OpenConversation(conversation);
            }
            else
            {
                OpenConversation(_current);
            }

            SendText(text);
        }

        private void Attach_Click(object sender, RoutedEventArgs e)
        {
            HintText.Text = "附件（图片 / 文本）会在对话阶段接上。";
        }

        // ------------------------------------------------------------------ agent 回合

        /// <summary>配置目录里的工作区就是 Agent 的活动范围。</summary>
        private string WorkspaceRoot => _settings.Workspace;

        private void OnUi(Action action)
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                action();
            }
            else
            {
                DispatcherQueue.TryEnqueue(() => action());
            }
        }

        /// <summary>
        /// 把 think 块从流里摘出去，正文才不会被 &lt;think&gt; 标签弄脏。还没闭合就什么都先不显示，
        /// 免得半个思考块闪在屏幕上。
        /// </summary>
        private static string VisibleStreamText(string raw)
        {
            string trimmed = raw.TrimStart();
            if (!trimmed.StartsWith("<think", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("<thinking", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            Match closed = Regex.Match(trimmed, "^<(think|thinking)>([\\s\\S]*?)</\\1>", RegexOptions.IgnoreCase);
            return closed.Success ? trimmed.Substring(closed.Length).TrimStart() : string.Empty;
        }

        /// <summary>
        /// 收下一小段吐字。屏幕没必要跟着每一个字重排一次——攒起来交给 45ms 的合帧定时器，
        /// 一次落地一次滚动。模型吐字越快，这么做省下的重排越多。
        /// </summary>
        private void AppendStream(string piece)
        {
            _streamBuffer.Append(piece);

            if (_streamText == null)
            {
                _streamText = BuildAnswerBlock();
                TranscriptPanel.Children.Add(_streamText);
            }

            _streamDirty = true;

            if (_streamTimer == null)
            {
                _streamTimer = DispatcherQueue.CreateTimer();
                _streamTimer.Interval = TimeSpan.FromMilliseconds(45);
                _streamTimer.IsRepeating = true;
                _streamTimer.Tick += (DispatcherQueueTimer sender, object args) => FlushStream();
                _streamTimer.Start();
            }
        }

        /// <summary>把攒着的吐字画到屏幕上。</summary>
        private void FlushStream()
        {
            if (!_streamDirty || _streamText == null)
            {
                return;
            }

            _streamDirty = false;
            _streamText.Text = VisibleStreamText(_streamBuffer.ToString());
            ScrollTranscriptToEnd();
        }

        /// <summary>这一轮结束后合帧定时器就该停了，不然它会一直空转。</summary>
        private void StopStreamTimer()
        {
            _streamTimer?.Stop();
            _streamTimer = null;
            _streamDirty = false;
        }

        private TextBlock BuildAnswerBlock()
        {
            return new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 780,
            };
        }

        /// <summary>一行状态，跑的时候挂在记录最下面。</summary>
        private static TextBlock BuildAgentStatus(string text)
        {
            return new TextBlock
            {
                Text = text,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 780,
            };
        }

        private void ShowThinking(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            TextBlock block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.72,
                MaxWidth = 620,
            };

            // 思考块排在正文前面，看着才顺
            int index = _streamText != null
                ? TranscriptPanel.Children.IndexOf(_streamText)
                : TranscriptPanel.Children.Count;

            if (index < 0)
            {
                index = TranscriptPanel.Children.Count;
            }

            TranscriptPanel.Children.Insert(index, block);
            ScrollTranscriptToEnd();
        }

        /// <summary>一次工具调用在记录里留下的小卡片。</summary>
        private sealed class ToolCard
        {
            public Border Root { get; set; } = null!;

            public TextBlock Head { get; set; } = null!;

            public TextBlock Detail { get; set; } = null!;
        }

        private void AddToolCard(ToolCall call, ToolSpec? spec)
        {
            ToolCard card = new ToolCard
            {
                Head = new TextBlock
                {
                    Text = (spec?.Title ?? call.Name) + " · 运行中",
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    FontSize = 13,
                },
                Detail = new TextBlock
                {
                    Text = DescribeCall(call),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Opacity = 0.75,
                },
            };

            StackPanel content = new StackPanel { Spacing = 2 };
            content.Children.Add(card.Head);
            content.Children.Add(card.Detail);

            card.Root = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 620,
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = content,
            };

            _toolCards[call.Id] = card;
            TranscriptPanel.Children.Add(card.Root);
            ScrollTranscriptToEnd();
        }

        private void FinishToolCard(ToolCall call, string summary, bool ok, long ms)
        {
            if (!_toolCards.TryGetValue(call.Id, out ToolCard? card))
            {
                return;
            }

            string title = card.Head.Text;
            int cut = title.IndexOf(" · ", StringComparison.Ordinal);
            if (cut > 0)
            {
                title = title.Substring(0, cut);
            }

            card.Head.Text = title + (ok ? " · 完成" : " · 失败") + "（" + ms + " ms）";
            card.Detail.Text = summary;
            ScrollTranscriptToEnd();
        }

        private void DeclineToolCard(ToolCall call)
        {
            if (!_toolCards.TryGetValue(call.Id, out ToolCard? card))
            {
                return;
            }

            card.Head.Text = call.Name + " · 已拒绝";
            card.Detail.Text = "主人没有同意这一步。";
        }

        private static string DescribeCall(ToolCall call)
        {
            string args = call.Args == null ? "{}" : call.Args.ToJsonString(Wire.Json);
            return call.Name + " " + (args.Length > 200 ? args.Substring(0, 200) + "…" : args);
        }

        /// <summary>危险操作要先问一声，得到允许才真的动手。</summary>
        private async Task<bool> AskApprovalAsync(ToolCall call, string title)
        {
            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "要动手了：" + title,
                Content = new TextBlock
                {
                    Text = DescribeCall(call),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12.5,
                },
                PrimaryButtonText = "允许",
                CloseButtonText = "拒绝",
                DefaultButton = ContentDialogButton.Primary,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        /// <summary>
        /// 一轮完整的对话：流式接字、跑工具、把过程画进记录里，最后把结论写进存档。
        /// </summary>
        private async Task RunAgentAsync()
        {
            if (_agentBusy)
            {
                HintText.Text = "本小姐手上还有活儿，等这一轮跑完。";
                return;
            }

            Conversation? conversation = _current;
            if (conversation == null)
            {
                return;
            }

            ProviderProfile provider = _settings.Active();
            bool ready = provider.ApiKey.Length > 0
                         || string.Equals(provider.Protocol, "ollama", StringComparison.OrdinalIgnoreCase);
            if (!ready)
            {
                AddAssistantMessage("本小姐还没拿到 " + provider.Name + " 的钥匙。去「设置 → 服务商」填一个 API Key 吧。");
                return;
            }

            // 没有工作区也照样能聊：这一轮手里只是没有文件工具，真要动文件时模型会自己
            // 请主人先去「工作区」挑一个文件夹。工作区被删掉的就当没选。
            string workspace = WorkspaceRoot;
            if (workspace.Length > 0 && !Directory.Exists(workspace))
            {
                workspace = string.Empty;
            }

            _agentBusy = true;
            _agentRun = new CancellationTokenSource();
            _streamText = null;
            _streamBuffer.Clear();
            _toolCards.Clear();

            SendButton.IsEnabled = false;
            HintText.Text = "本小姐开工了。按 Esc 可以叫停。";

            _agentStatus = BuildAgentStatus("正在连接 " + provider.Name + "…");
            TranscriptPanel.Children.Add(_agentStatus);
            ScrollTranscriptToEnd();

            AgentHooks hooks = new AgentHooks
            {
                Delta = piece => OnUi(() => AppendStream(piece)),
                Status = text => OnUi(() =>
                {
                    if (_agentStatus != null)
                    {
                        _agentStatus.Text = text;
                    }
                }),
                Thinking = text => OnUi(() => ShowThinking(text)),
                ToolStarted = (call, spec, _) => OnUi(() => AddToolCard(call, spec)),
                ToolFinished = (call, summary, ok, ms) => OnUi(() => FinishToolCard(call, summary, ok, ms)),
                ToolDeclined = (call, _) => OnUi(() => DeclineToolCard(call)),
                AskApproval = AskApprovalAsync,
            };

            AgentOutcome outcome;
            try
            {
                string systemPrompt = Agent.BuildSystemPrompt(
                    provider.SystemPrompt,
                    workspace,
                    _settings.Agent.Enabled,
                    _settings.Agent.MaxSteps > 0 ? _settings.Agent.MaxSteps : 400,
                    _settings.Agent.Scope,
                    _settings.Agent.DeepThinking,
                    _settings.Language);

                List<WireMessage> history = Agent.BuildHistory(conversation.Messages);

                outcome = await Agent.RunAsync(
                    provider,
                    systemPrompt,
                    history,
                    _settings.Agent,
                    workspace,
                    _settings.AiControllerEnabled,
                    hooks,
                    _agentRun.Token);
            }
            catch (Exception error)
            {
                outcome = new AgentOutcome { Ok = false, Hint = error.Message };
            }
            finally
            {
                _agentBusy = false;
                SendButton.IsEnabled = true;

                if (_agentStatus != null)
                {
                    TranscriptPanel.Children.Remove(_agentStatus);
                    _agentStatus = null;
                }

                StopStreamTimer();

                _agentRun?.Dispose();
                _agentRun = null;
            }

            string finalText = outcome.Text;
            if (finalText.Length == 0 && _streamBuffer.Length > 0)
            {
                finalText = VisibleStreamText(_streamBuffer.ToString());
            }

            TextBlock? stream = _streamText;
            _streamText = null;

            // 流式那段是纯文本，定稿后换成排好的 Markdown
            if (stream != null)
            {
                TranscriptPanel.Children.Remove(stream);
            }

            if (finalText.Length > 0)
            {
                AddAssistantMessage(finalText);
            }

            if (!outcome.Ok)
            {
                AddAssistantMessage(outcome.Hint.Length > 0 ? outcome.Hint : "这一轮没能跑完。");
            }
            else if (outcome.Hint.Length > 0)
            {
                HintText.Text = outcome.Hint;
            }
            else
            {
                HintText.Text = "回车发送 · Shift+回车 换行 · Ctrl+L 清屏";
            }

            ScrollTranscriptToEnd();
        }

        // ------------------------------------------------------------------ 快捷选模型

        /// <summary>
        /// 输入框上方那个模型芯片点开就是一个选择框：先给这家服务商的建议，再给真的拉回来的模型，
        /// 点一下就换过去，不用绕去设置页。
        /// </summary>
        private void ModelFlyout_Opening(object sender, object e)
        {
            BuildModelFlyout();
        }

        private void BuildModelFlyout()
        {
            ModelFlyout.Items.Clear();

            ProviderProfile provider = _settings.Active();
            string current = provider.Model ?? string.Empty;

            List<string> models = CachedModels(provider.Id);
            foreach (string hint in ModelHintsFor(provider.Protocol))
            {
                if (!models.Contains(hint, StringComparer.OrdinalIgnoreCase))
                {
                    models.Add(hint);
                }
            }

            if (current.Length > 0 && !models.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                models.Insert(0, current);
            }

            foreach (string model in models)
            {
                ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
                {
                    Text = model,
                    IsChecked = string.Equals(model, current, StringComparison.OrdinalIgnoreCase),
                };

                string captured = model;
                item.Click += (object s, RoutedEventArgs a) => PickModel(captured);
                ModelFlyout.Items.Add(item);
            }

            if (models.Count == 0)
            {
                ModelFlyout.Items.Add(new MenuFlyoutItem
                {
                    Text = "这家还没有可选的模型",
                    IsEnabled = false,
                });
            }

            ModelFlyout.Items.Add(new MenuFlyoutSeparator());

            MenuFlyoutItem refresh = new MenuFlyoutItem { Text = "刷新模型列表" };
            refresh.Click += RefreshModelsFromChip_Click;
            ModelFlyout.Items.Add(refresh);

            MenuFlyoutItem settings = new MenuFlyoutItem { Text = "去「设置 → 服务商」调整…" };
            settings.Click += (object s, RoutedEventArgs a) => GoToSettings("provider");
            ModelFlyout.Items.Add(settings);
        }

        private void PickModel(string model)
        {
            ProviderProfile provider = _settings.Active();
            provider.Model = model;
            SettingsStore.Save();
            ApplyProviderToShell();
            HintText.Text = "已切到 " + provider.Name + " · " + model;
        }

        private async void RefreshModelsFromChip_Click(object sender, RoutedEventArgs e)
        {
            ProviderProfile provider = _settings.Active();
            if (provider.BaseUrl.Length == 0)
            {
                HintText.Text = "这家还没填接口地址，去「设置 → 服务商」补上。";
                return;
            }

            _modelsRequest?.Cancel();
            _modelsRequest?.Dispose();
            _modelsRequest = new CancellationTokenSource();

            List<string> models;
            try
            {
                models = await ChatApi.ListModelsAsync(provider, _modelsRequest.Token);
            }
            catch
            {
                models = new List<string>();
            }

            if (_modelsRequest == null || _modelsRequest.IsCancellationRequested)
            {
                return;
            }

            _modelCache[provider.Id] = models;
            HintText.Text = models.Count > 0
                ? "拉到 " + models.Count + " 个模型。"
                : "这家没有返回模型列表，去设置里手填一个。";

            BuildModelFlyout();

            // 点击已经把这个菜单关掉了，等它收完再弹一次，就能接着挑
            await Task.Delay(160);
            try
            {
                ModelFlyout.ShowAt(ModelChipButton);
            }
            catch
            {
                // 弹不出来就算了，提示已经写在下面
            }
        }

        private void PermissionChip_Click(object sender, RoutedEventArgs e)
        {
            GoToSettings("permission");
        }

        private void DeepThinkChip_Click(object sender, RoutedEventArgs e)
        {
            _settings.Agent.DeepThinking = DeepThinkChip.IsChecked == true;
            SettingsStore.Save();
            HintText.Text = _settings.Agent.DeepThinking ? "深度思考已开启。" : "深度思考已关闭。";
        }

        private void WebSearchChip_Click(object sender, RoutedEventArgs e)
        {
            _settings.Agent.WebSearch = WebSearchChip.IsChecked == true;
            SettingsStore.Save();
            HintText.Text = _settings.Agent.WebSearch ? "联网搜索已开启。" : "联网搜索已关闭。";
        }

        // ------------------------------------------------------------------ easter eggs

        /// <summary>
        /// Eggs answer on the spot: no model call, no API key. Each one still echoes the user's own
        /// message first, because from the transcript's point of view a message was sent.
        /// </summary>
        private void RunEgg(EggKind egg)
        {
            switch (egg)
            {
                case EggKind.OnePlus:
                    // the bubble opens the brand page on click; the scene is the egg itself
                    AddEggBubble("Never Settle", Eggs.OnePlusUrl, true);
                    ShowNeverSettle();
                    break;

                case EggKind.Ysjf:
                    AddEggBubble("無限進步", Eggs.YsjfUrl, false);
                    break;

                case EggKind.SixtySeven:
                    // this one always reads itself, and always in English
                    AddAssistantMessage(Eggs.SixtySevenText, true, "en");
                    break;

                case EggKind.FireflyOs:
                    _ = Eggs.OpenUrlAsync(Eggs.FireflyOsUrl);
                    break;

                case EggKind.Maybe:
                    // 用 flac 里自带的时间轴滚歌词
                    _ = PlayEggSongAsync("Maybe", "chuuuu", Eggs.MaybeFileName, "maybe.txt");
                    break;

                case EggKind.Mermaid:
                    // 打开播放器，同时在回答里跟着播放进度滚歌词
                    _ = PlayEggSongAsync("人鱼的眼泪", "EXO", Eggs.MermaidFileName, "mermaid.txt");
                    break;

                case EggKind.Moonlight:
                    // 我本将心向明月，英文名就用 Moonlight
                    _ = PlayEggSongAsync("我本将心向明月", "楚山 / DJ雨伞", Eggs.MoonlightFileName, "moonlight.txt");
                    break;

                case EggKind.Midnight:
                    _ = PlayEggSongAsync("Midnight City", "M83", Eggs.MidnightFileName, "midnight.txt");
                    break;

                case EggKind.Regret:
                    _ = PlayEggSongAsync("拼接遗憾", string.Empty, Eggs.RegretFileName, "regret.txt");
                    break;

                case EggKind.JustDoIt:
                    // 直接把图贴进记录里；发的是哪一版跟着当前主题走
                    AddNikeBubble();
                    break;
            }
        }

        /// <summary>
        /// 音乐彩蛋：照旧用外面的播放器打开这首歌，同时在回答下面挂一条歌词，
        /// 按系统媒体会话报的播放进度一句一句往下走。点这里的停止、播放器里按停止、
        /// 或者直接把播放器窗口关掉，都会把这个彩蛋收掉——也只收这个彩蛋。
        /// </summary>
        private async Task PlayEggSongAsync(string title, string artist, string audioFile, string lyricFile)
        {
            // 上一首还开着的话连它的播放器一起收掉，别两首一起放
            await StopEggSongAsync(false, "换了一首。");

            _eggSongTitle = title;
            _eggSongArtist = artist;
            _eggSongLyricFile = lyricFile;
            _eggSongTrack = Lyrics.Load(lyricFile);
            _eggCalibrating = false;
            _eggCalibIndex = 0;
            _eggCalibTimes = null;
            _eggHeaderSuffix = "";
            _eggSongDuration = TimeSpan.FromMinutes(4);
            _eggSongIndex = -1;
            _eggSongRunning = true;
            _eggSession = null;
            _eggSessionSeen = false;
            _eggSessionMisses = 0;

            // 文件自带的时长最稳，系统媒体会话有时报 0
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", audioFile);
                Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                Windows.Storage.FileProperties.MusicProperties music = await file.Properties.GetMusicPropertiesAsync();
                if (music.Duration > TimeSpan.Zero)
                {
                    _eggSongDuration = music.Duration;
                }
            }
            catch
            {
                // 读不到就用四分钟的默认值
            }

            (bool ok, Process? player) = await Eggs.StartBundledPlayerAsync(audioFile);
            _eggSongPlayer = player;

            // 从播放器拉起来那一刻开始算，别把启动等待也算进歌词进度里
            _eggSongStart = DateTime.Now;

            AddEggSongBubble(ok);

            // 把这次彩蛋写进聊天记录，重新打开对话时不会只剩自己发的消息
            RecordMessage("assistant", ok ? "正在播放《" + title + "》。" : "没能打开《" + title + "》。");

            if (ok)
            {
                StartEggSongTimer();
            }
            else
            {
                _eggSongRunning = false;
                if (_eggSongLine != null)
                {
                    _eggSongLine.Text = "没找到这首歌的音频文件：" + audioFile;
                }
            }
        }

        private void AddEggSongBubble(bool started)
        {
            StackPanel inner = new StackPanel { Spacing = 6 };

            _eggSongHeader = new TextBlock
            {
                Text = EggHeaderText(string.Empty),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
            };
            inner.Children.Add(_eggSongHeader);

            _eggSongLine = new TextBlock
            {
                Text = started
                    ? (_eggSongTrack != null && _eggSongTrack.Lines.Length > 0 ? "准备播放…" : "正在播放…")
                    : "歌曲文件不在本机。",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
            };

            _eggSongNext = new TextBlock
            {
                Text = string.Empty,
                FontSize = 12,
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap,
            };

            inner.Children.Add(_eggSongLine);
            inner.Children.Add(_eggSongNext);

            if (started)
            {
                _eggSongButtons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                };

                _eggSongStop = new Button { Content = "停止播放" };
                _eggSongStop.Click += (object sender, RoutedEventArgs e) =>
                {
                    _ = StopEggSongAsync(false, "已停止播放。");
                };

                _eggCalibrateButton = new Button { Content = "校准歌词时间" };
                _eggCalibrateButton.Click += (object sender, RoutedEventArgs e) => StartEggCalibration();

                _eggSongButtons.Children.Add(_eggSongStop);

                // 没有歌词的彩蛋（例如 Maybe）就不给校准按钮
                if (_eggSongTrack != null && _eggSongTrack.Lines.Length > 0)
                {
                    _eggSongButtons.Children.Add(_eggCalibrateButton);
                }

                inner.Children.Add(_eggSongButtons);
            }
            else
            {
                _eggSongStop = null;
                _eggCalibrateButton = null;
                _eggSongButtons = null;
            }

            Border bubble = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 620,
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(8),
                Child = inner,
            };

            _assistantBubbles.Add(bubble);
            ApplyBubbleTheme(bubble);
            TranscriptPanel.Children.Add(bubble);
            ScrollTranscriptToEnd();
        }

        /// <summary>歌曲名 + 歌手 + 状态后缀。</summary>
        private string EggHeaderText(string suffix)
        {
            string header = _eggSongTitle;
            if (!string.IsNullOrEmpty(_eggSongArtist))
            {
                header += " · " + _eggSongArtist;
            }

            return header + suffix;
        }

        /// <summary>只在状态真变了的时候改一次标题，避免每半秒重写一遍。</summary>
        private void SetEggHeaderSuffix(string suffix)
        {
            if (_eggSongHeader == null || _eggHeaderSuffix == suffix)
            {
                return;
            }

            _eggHeaderSuffix = suffix;
            _eggSongHeader.Text = EggHeaderText(suffix);
        }

        /// <summary>播放器没了：把彩蛋收掉，标题后缀写明是关掉了。</summary>
        private void CloseEggSong(string message)
        {
            _eggSongTimer?.Stop();
            _eggSongTimer = null;
            _eggSongRunning = false;
            _eggSongPlayer = null;
            _eggSession = null;
            _eggCalibrating = false;

            SetEggHeaderSuffix("（已关闭播放器）");

            if (_eggSongLine != null)
            {
                _eggSongLine.Text = message;
            }

            if (_eggSongNext != null)
            {
                _eggSongNext.Text = string.Empty;
            }

            if (_eggSongStop != null)
            {
                _eggSongStop.IsEnabled = false;
            }

            if (_eggCalibrateButton != null)
            {
                _eggCalibrateButton.IsEnabled = false;
            }

            if (_eggMarkButton != null)
            {
                _eggMarkButton.IsEnabled = false;
            }
        }

        private void StartEggSongTimer()
        {
            _eggSongTimer = DispatcherQueue.CreateTimer();
            _eggSongTimer.Interval = TimeSpan.FromMilliseconds(300);
            _eggSongTimer.Tick += (DispatcherQueueTimer sender, object args) => EggSongTick();
            _eggSongTimer.Start();
        }

        /// <summary>每半秒问一次系统：这首歌放到哪儿了？播放器还在不在？</summary>
        private async void EggSongTick()
        {
            if (!_eggSongRunning)
            {
                return;
            }

            try
            {
                // 播放器进程退出了（单实例播放器会把新窗口交给老进程，所以先看有没有媒体会话）
                if (_eggSongPlayer != null && _eggSongPlayer.HasExited && !_eggSessionSeen
                    && (DateTime.Now - _eggSongStart).TotalSeconds > 6)
                {
                    CloseEggSong("已关闭播放器。");
                    return;
                }

                double seconds = -1;
                try
                {
                    if (_eggSession == null)
                    {
                        _eggSession = await FindEggSessionAsync();
                    }

                    if (_eggSession != null)
                    {
                        _eggSessionSeen = true;
                        _eggSessionMisses = 0;

                        GlobalSystemMediaTransportControlsSessionPlaybackStatus status =
                            _eggSession.GetPlaybackInfo().PlaybackStatus;

                        if (status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            // 暂停或者停止：标题挂上后缀，彩蛋留着，播放器一继续就接着滚
                            SetEggHeaderSuffix("（已暂停播放）");
                            return;
                        }

                        SetEggHeaderSuffix(string.Empty);

                        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline =
                            _eggSession.GetTimelineProperties();
                        if (timeline.Position > TimeSpan.Zero)
                        {
                            seconds = timeline.Position.TotalSeconds;

                            // 有的播放器几秒才推一次 Position，按最后一次更新的时间补上差值
                            double since = (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds;
                            if (since > 0 && since < 10)
                            {
                                seconds += since;
                            }
                        }
                    }
                    else if (_eggSessionSeen)
                    {
                        // 媒体会话消失了，多半是播放器窗口关了；给它两拍宽限
                        _eggSessionMisses++;
                        if (_eggSessionMisses >= 2)
                        {
                            CloseEggSong("已关闭播放器。");
                            return;
                        }
                    }
                }
                catch
                {
                    _eggSession = null;
                }

                if (seconds < 0)
                {
                    seconds = (DateTime.Now - _eggSongStart).TotalSeconds;
                }

                UpdateEggLyric(seconds);
            }
            catch
            {
                // 彩蛋不该把主程序拖下水
            }
        }

        private async Task<GlobalSystemMediaTransportControlsSession?> FindEggSessionAsync()
        {
            try
            {
                _eggSessions ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_eggSessions == null)
                {
                    return null;
                }

                GlobalSystemMediaTransportControlsSession? playing = null;
                int playingCount = 0;

                foreach (GlobalSystemMediaTransportControlsSession session in _eggSessions.GetSessions())
                {
                    try
                    {
                        GlobalSystemMediaTransportControlsSessionMediaProperties props =
                            await session.TryGetMediaPropertiesAsync();
                        string title = props.Title ?? string.Empty;

                        if (title.Contains(_eggSongTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            return session;
                        }

                        if (session.GetPlaybackInfo().PlaybackStatus
                            == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            playing = session;
                            playingCount++;
                        }
                    }
                    catch
                    {
                        // 有的会话读不到信息，跳过
                    }
                }

                // 标题对不上时，只有一路在放就认它
                return playingCount == 1 ? playing : null;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateEggLyric(double seconds)
        {
            if (_eggCalibrating)
            {
                return;
            }

            LyricTrack? track = _eggSongTrack;
            if (track == null || track.Lines.Length == 0 || _eggSongLine == null || _eggSongNext == null)
            {
                return;
            }

            int index = Lyrics.IndexAt(seconds, track.Times, _eggSongDuration, track.Lines.Length);
            if (index == _eggSongIndex)
            {
                return;
            }

            _eggSongIndex = index;
            _eggSongLine.Text = track.Lines[index];

            // 播放时只露出当前这一句，下一句不提前剧透
            _eggSongNext.Text = string.Empty;
        }

        /// <summary>
        /// 校准：跟着播放器听，每听到一句的开头点一下「记下这句」，
        /// 位置就按当前播放进度记下来，最后写成 LRC，下次不用再对。
        /// </summary>
        private void StartEggCalibration()
        {
            if (!_eggSongRunning || _eggSongTrack == null || _eggSongTrack.Lines.Length == 0
                || _eggSongButtons == null)
            {
                return;
            }

            _eggCalibrating = true;
            _eggCalibIndex = 0;
            _eggCalibTimes = new double[_eggSongTrack.Lines.Length];

            if (_eggCalibrateButton != null)
            {
                _eggCalibrateButton.IsEnabled = false;
            }

            _eggMarkButton = new Button { Content = "记下这句" };
            _eggMarkButton.Click += (object sender, RoutedEventArgs e) => _ = MarkEggLineAsync();
            _eggSongButtons.Children.Add(_eggMarkButton);

            if (_eggSongLine != null)
            {
                _eggSongLine.Text = "校准：「" + _eggSongTrack.Lines[0] + "」";
            }

            if (_eggSongNext != null)
            {
                _eggSongNext.Text = "听到这一句开头就点「记下这句」，共 "
                                    + _eggSongTrack.Lines.Length + " 句。";
            }
        }

        private async Task MarkEggLineAsync()
        {
            if (!_eggCalibrating || _eggSongTrack == null || _eggCalibTimes == null)
            {
                return;
            }

            double seconds = await EggPlaybackSecondsAsync();
            if (_eggCalibIndex < _eggCalibTimes.Length)
            {
                _eggCalibTimes[_eggCalibIndex] = seconds;
            }

            _eggCalibIndex++;

            if (_eggCalibIndex >= _eggSongTrack.Lines.Length)
            {
                FinishEggCalibration();
                return;
            }

            if (_eggSongLine != null)
            {
                _eggSongLine.Text = "下一句：「" + _eggSongTrack.Lines[_eggCalibIndex] + "」";
            }

            if (_eggSongNext != null)
            {
                _eggSongNext.Text = "第 " + (_eggCalibIndex + 1) + " / "
                                    + _eggSongTrack.Lines.Length + " 句，听到就点一下。";
            }
        }

        private void FinishEggCalibration()
        {
            if (_eggSongTrack == null || _eggCalibTimes == null)
            {
                return;
            }

            _eggCalibrating = false;
            bool saved = Lyrics.SaveLrc(_eggSongLyricFile, _eggSongTrack.Lines, _eggCalibTimes);
            _eggSongTrack = new LyricTrack { Lines = _eggSongTrack.Lines, Times = _eggCalibTimes };
            _eggSongIndex = -1;

            if (_eggMarkButton != null)
            {
                _eggMarkButton.IsEnabled = false;
            }

            if (_eggSongLine != null)
            {
                _eggSongLine.Text = saved ? "校准完成，下次就按这条时间轴走。" : "校准完成，但时间轴没能存下来。";
            }

            if (_eggSongNext != null)
            {
                _eggSongNext.Text = string.Empty;
            }
        }

        private async Task<double> EggPlaybackSecondsAsync()
        {
            try
            {
                GlobalSystemMediaTransportControlsSession? session = _eggSession;
                if (session == null)
                {
                    session = await FindEggSessionAsync();
                    _eggSession = session;
                }

                if (session != null)
                {
                    GlobalSystemMediaTransportControlsSessionTimelineProperties timeline =
                        session.GetTimelineProperties();
                    if (timeline.Position > TimeSpan.Zero)
                    {
                        return timeline.Position.TotalSeconds;
                    }
                }
            }
            catch
            {
                // 读不到就按已经过去的时间算
            }

            return (DateTime.Now - _eggSongStart).TotalSeconds;
        }

        /// <summary>
        /// 收掉彩蛋。先通过系统媒体会话让它真的停下（单实例播放器把文件交给老进程时，
        /// 杀新进程是没用的），再顺手关掉我们拉起来的那个进程。
        /// fromPlayer 为真表示播放器那边先停的/关的，就不去杀进程了。
        /// </summary>
        private async Task StopEggSongAsync(bool fromPlayer, string note)
        {
            _eggSongTimer?.Stop();
            _eggSongTimer = null;

            GlobalSystemMediaTransportControlsSession? session = _eggSession;
            if (session == null)
            {
                session = await FindEggSessionAsync();
            }

            if (session != null)
            {
                try
                {
                    // 不管声音是哪个进程在放，这一下都能让它停
                    await session.TryStopAsync();
                }
                catch
                {
                    // 播放器不认这条命令就退回杀进程
                }
            }

            if (!fromPlayer && _eggSongPlayer != null)
            {
                try
                {
                    if (!_eggSongPlayer.HasExited)
                    {
                        _eggSongPlayer.Kill(true);
                    }
                }
                catch
                {
                    // 进程可能已经被主人手动关掉了
                }
            }

            _eggSongPlayer = null;
            _eggSession = null;
            _eggSongRunning = false;
            _eggCalibrating = false;

            // 点了这里的停止，播放器那边的状态就是「暂停」了
            SetEggHeaderSuffix("（已暂停播放）");

            if (_eggCalibrateButton != null)
            {
                _eggCalibrateButton.IsEnabled = false;
            }

            if (_eggMarkButton != null)
            {
                _eggMarkButton.IsEnabled = false;
            }

            if (_eggSongLine != null && !string.IsNullOrEmpty(note))
            {
                _eggSongLine.Text = note;
            }

            if (_eggSongNext != null)
            {
                _eggSongNext.Text = string.Empty;
            }

            if (_eggSongStop != null)
            {
                _eggSongStop.IsEnabled = false;
            }
        }

        /// <summary>
        /// 「Just do it」的那条钩子。浅色模式下给白底黑钩，深色模式下给黑底白钩，所以这一版
        /// 那张图和当前主题是绑在一起的；之后主人换主题，已经贴在记录里的图也跟着换。
        /// </summary>
        private void AddNikeBubble()
        {
            Image shot = new Image
            {
                MaxWidth = 380,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                Source = NikeArtwork(IsDarkNow()),
            };

            StackPanel content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock
            {
                Text = "DOMEOS AI",
                Style = (Style)Application.Current.Resources["DomeEggWho"],
            });
            content.Children.Add(shot);

            Border card = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 420,
                Padding = new Thickness(14, 11, 16, 12),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = content,
            };

            _nikeImages.Add(shot);
            TranscriptPanel.Children.Add(card);
            ScrollTranscriptToEnd();
        }

        /// <summary>主题换过之后，记录里那些钩子图一律换成新主题该用的那一版。</summary>
        private void ApplyNikeTheme()
        {
            if (_nikeImages.Count == 0)
            {
                return;
            }

            ImageSource? artwork = NikeArtwork(IsDarkNow());
            if (artwork == null)
            {
                return;
            }

            foreach (Image shot in _nikeImages)
            {
                shot.Source = artwork;
            }
        }

        private bool IsDarkNow()
        {
            return RootGrid.ActualTheme == ElementTheme.Dark;
        }

        /// <summary>两张图只解码一次，换主题时换指针而不是重新读盘。</summary>
        private ImageSource? NikeArtwork(bool dark)
        {
            if (dark)
            {
                _nikeDark ??= LoadAssetImage(Eggs.JustDoItDarkFileName);
                return _nikeDark;
            }

            _nikeLight ??= LoadAssetImage(Eggs.JustDoItLightFileName);
            return _nikeLight;
        }

        private static ImageSource? LoadAssetImage(string fileName)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
                if (File.Exists(path))
                {
                    return new BitmapImage(new Uri(path));
                }
            }
            catch
            {
                // 图读不出来也不该把人绊住
            }

            return null;
        }

        // ------------------------------------------------------------------ speech

        private async Task SpeakAsync(string text, string languageTag)
        {
            string error = await Speech.SpeakAsync(_settings, text, languageTag);
            if (error.Length == 0)
            {
                // 外部配音软件那条路不会出声，只把文字交出去，提示一下主人
                if (Speech.LastNotice.Length > 0)
                {
                    HintText.Text = Speech.LastNotice;
                }

                return;
            }

            HintText.Text = "朗读失败：" + error;
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                VoiceHintText.Text = "朗读失败：" + error;
            }
        }

        private void Speech_StateChanged(object? sender, EventArgs e)
        {
            bool speaking = Speech.IsSpeaking;

            if (_speakingButton != null)
            {
                _speakingButton.Content = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                    Glyph = speaking ? "\uE71A" : "\uE767",
                    FontSize = 12,
                };

                if (!speaking)
                {
                    _speakingButton = null;
                }
            }

            // the orb breathes faster while it is talking
            ApplyOrb();
        }

        private void ShowNeverSettle()
        {
            CloseNeverSettle();
            EggLayer.Visibility = Visibility.Visible;
            _neverSettle = new NeverSettleScene(EggLayer, DispatcherQueue);
        }

        private void CloseNeverSettle()
        {
            _neverSettle?.Close();
            _neverSettle = null;
            EggLayer.Visibility = Visibility.Collapsed;
            ComposerBox.Focus(FocusState.Programmatic);
        }

        private void EggLayer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CloseNeverSettle();
        }

        private void ShowRunDuck()
        {
            RunDuckLayer.Visibility = Visibility.Visible;

            if (_runDuck == null)
            {
                _runDuck = new RunDuckGame(RunDuckImage, RunDuckScore, RunDuckOver, DispatcherQueue);
            }

            _runDuck.Open();
        }

        private void CloseRunDuck()
        {
            _runDuck?.Close();
            RunDuckLayer.Visibility = Visibility.Collapsed;
            ComposerBox.Focus(FocusState.Programmatic);
        }

        private void RunDuckClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRunDuck();
        }

        private void RunDuckImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _runDuck?.Tap();
        }

        /// <summary>The About card keeps RunDuck behind two left clicks and then a right click.</summary>
        private void AboutCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _aboutLeftClicks++;
        }

        private void AboutCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            bool ready = _aboutLeftClicks >= 2;
            _aboutLeftClicks = 0;

            if (ready)
            {
                ShowRunDuck();
            }
        }

        /// <summary>While an egg is on screen it owns the keyboard, so nothing leaks into the chat.</summary>
        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_neverSettle != null)
            {
                if (e.Key == VirtualKey.Escape)
                {
                    CloseNeverSettle();
                }

                e.Handled = true;
                return;
            }

            // 跑着的这一轮可以按 Esc 叫停
            if (_agentBusy && _runDuck == null && e.Key == VirtualKey.Escape)
            {
                _agentRun?.Cancel();
                HintText.Text = "正在叫停…";
                e.Handled = true;
                return;
            }

            if (_runDuck != null)
            {
                if (e.Key == VirtualKey.Escape)
                {
                    CloseRunDuck();
                }
                else
                {
                    _runDuck.HandleKeyDown(e.Key);
                }

                e.Handled = true;
            }
        }

        private void RootGrid_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            _runDuck?.HandleKeyUp(e.Key);
        }

        // ------------------------------------------------------------------ workspace

        private void ApplyWorkspaceToShell()
        {
            string path = _settings.Workspace;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                WorkspaceNameText.Text = FolderLabel(path);
                WorkspacePathText.Text = path;
                WorkspaceHintText.Text = "展开文件夹查看内容，点文件可看完整路径。";
                BuildWorkspaceTree(path);
            }
            else
            {
                WorkspaceNameText.Text = "未选择工作区";
                WorkspacePathText.Text = "选择一个文件夹作为 Agent 的工作目录";
                WorkspaceHintText.Text = "还没有工作区。先选一个文件夹吧。";
                WorkspaceTree.RootNodes.Clear();
            }
        }

        private async void PickWorkspace_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Windows.Storage.Pickers.FolderPicker picker = new Windows.Storage.Pickers.FolderPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

                Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    _settings.Workspace = folder.Path;
                    SettingsStore.Save();
                    ApplyWorkspaceToShell();
                }
            }
            catch (Exception ex)
            {
                WorkspaceHintText.Text = "选择文件夹失败：" + ex.Message;
            }
        }

        private void RefreshWorkspace_Click(object sender, RoutedEventArgs e)
        {
            ApplyWorkspaceToShell();
        }

        private void BuildWorkspaceTree(string root)
        {
            WorkspaceTree.RootNodes.Clear();

            try
            {
                TreeViewNode node = FolderNode(root);
                node.IsExpanded = true;
                WorkspaceTree.RootNodes.Add(node);
            }
            catch (Exception ex)
            {
                WorkspaceHintText.Text = "无法读取这个文件夹：" + ex.Message;
            }
        }

        private TreeViewNode FolderNode(string path)
        {
            return new TreeViewNode
            {
                Content = new TreeNodeInfo(path, FolderLabel(path), true),
                // the chevron shows only when there is something to open
                HasUnrealizedChildren = ListChildren(path).Count > 0,
            };
        }

        private static TreeViewNode FileNode(string path)
        {
            return new TreeViewNode
            {
                Content = new TreeNodeInfo(path, Path.GetFileName(path), false),
            };
        }

        /// <summary>从 Content 里取回节点信息。</summary>
        private static TreeNodeInfo? InfoOf(object? content)
        {
            if (content is TreeNodeInfo info)
            {
                return info;
            }

            return (content as FrameworkElement)?.Tag as TreeNodeInfo;
        }

        /// <summary>Children are materialised the first time a folder is opened.</summary>
        private void WorkspaceTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            TreeViewNode node = args.Node;
            if (!node.HasUnrealizedChildren)
            {
                return;
            }

            node.HasUnrealizedChildren = false;

            TreeNodeInfo? info = InfoOf(node.Content);
            if (info == null || !info.IsFolder)
            {
                return;
            }

            try
            {
                foreach (string path in ListChildren(info.FullPath))
                {
                    node.Children.Add(info.IsFolder && Directory.Exists(path) ? FolderNode(path) : FileNode(path));
                }
            }
            catch (Exception ex)
            {
                WorkspaceHintText.Text = "无法读取 " + info.Name + "：" + ex.Message;
            }
        }

        private void WorkspaceTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            TreeViewNode? node = args.InvokedItem as TreeViewNode;
            TreeNodeInfo? info = InfoOf(node?.Content);
            if (info != null)
            {
                WorkspaceHintText.Text = info.IsFolder ? "文件夹：" + info.FullPath : info.FullPath;
            }
        }

        /// <summary>Directories first, then files, each alphabetically, with dot-entries skipped.</summary>
        private static List<string> ListChildren(string dir)
        {
            List<string> children = new List<string>();

            try
            {
                foreach (string entry in Directory.GetDirectories(dir))
                {
                    if (!Path.GetFileName(entry).StartsWith("."))
                    {
                        children.Add(entry);
                    }
                }

                children.Sort(StringComparer.OrdinalIgnoreCase);

                List<string> files = new List<string>();
                foreach (string entry in Directory.GetFiles(dir))
                {
                    if (!Path.GetFileName(entry).StartsWith("."))
                    {
                        files.Add(entry);
                    }
                }

                files.Sort(StringComparer.OrdinalIgnoreCase);

                int room = Math.Max(0, MaxFolderEntries - children.Count);
                if (files.Count > room)
                {
                    files.RemoveRange(room, files.Count - room);
                }

                children.AddRange(files);
            }
            catch
            {
                // an unreadable folder simply shows as empty
            }

            return children;
        }

        private static string FolderLabel(string path)
        {
            string name = new DirectoryInfo(path).Name;
            return name.Length == 0 ? path : name;
        }

        // ------------------------------------------------------------------ settings page

        private void RefreshSettingsFields()
        {
            BuildPresetPicker();
            BuildProviderPicker();
            LoadProviderEditor(_settings.ActiveProvider);
            RefreshPersonaFields();
            RefreshVoiceFields();
            RefreshOrbFields();
            RefreshBackgroundFields();
            RefreshPermissionFields();
            RefreshGeneralFields();
        }

        // ---- 服务商

        private void BuildPresetPicker()
        {
            PresetPicker.DisplayMemberPath = "Name";
            PresetPicker.ItemsSource = Providers.All;
            PresetPicker.SelectedIndex = -1;
        }

        private void BuildProviderPicker()
        {
            _loading = true;
            ProviderPicker.DisplayMemberPath = "Name";
            ProviderPicker.ItemsSource = null;
            ProviderPicker.ItemsSource = _settings.Providers;
            ProviderPicker.SelectedIndex = _settings.Providers.Count == 0
                ? -1
                : Math.Clamp(_settings.ActiveProvider, 0, _settings.Providers.Count - 1);
            _loading = false;
        }

        private void ProviderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading == false && ProviderPicker.SelectedIndex >= 0)
            {
                LoadProviderEditor(ProviderPicker.SelectedIndex);
                _ = RefreshModelListAsync(false);
            }
        }

        private void ProviderExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
        {
            // 展开就意味着要看这一家，顺手把它的模型列表刷新一遍
            _ = RefreshModelListAsync(false);
        }

        private void ProviderProtoBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            FillModelList(SelectedProto(), CachedModels(CurrentEditorId()), ProviderModelBox.Text ?? string.Empty);
            _ = RefreshModelListAsync(false);
        }

        private void ProviderBaseBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _ = RefreshModelListAsync(false);
        }

        private void ProviderKeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _ = RefreshModelListAsync(false);
        }

        private void RefreshModels_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshModelListAsync(true);
        }

        // ---- 模型列表

        private static List<string> ModelHintsFor(string protocol)
        {
            return Providers.ModelHints.TryGetValue(protocol ?? string.Empty, out string[]? hints)
                ? new List<string>(hints)
                : new List<string>();
        }

        private List<string> CachedModels(string id)
        {
            return id.Length > 0 && _modelCache.TryGetValue(id, out List<string>? cached)
                ? cached
                : new List<string>();
        }

        /// <summary>当前编辑器里那家服务商的 Id，用来给缓存和请求定位。</summary>
        private string CurrentEditorId()
        {
            int index = ProviderPicker.SelectedIndex;
            return index >= 0 && index < _settings.Providers.Count ? _settings.Providers[index].Id : string.Empty;
        }

        /// <summary>
        /// 把模型下拉填满：先放这家协议的建议，再放真的拉回来的，最后保证当前模型一定在列表里。
        /// </summary>
        private void FillModelList(string protocol, List<string> fetched, string keep)
        {
            List<string> items = ModelHintsFor(protocol);
            foreach (string model in fetched)
            {
                if (!items.Contains(model, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(model);
                }
            }

            if (keep.Length > 0 && !items.Contains(keep, StringComparer.OrdinalIgnoreCase))
            {
                items.Insert(0, keep);
            }

            bool wasLoading = _loading;
            _loading = true;
            ProviderModelBox.ItemsSource = items;
            ProviderModelBox.Text = keep;
            _loading = wasLoading;
        }

        /// <summary>
        /// 按编辑器里现在填的地址和密钥去问这家服务商有哪些模型。0 结果也照样填进去，
        /// 这样用户至少还能从建议列表里挑，或者直接手打。
        /// </summary>
        private async Task RefreshModelListAsync(bool announce)
        {
            if (_loading)
            {
                return;
            }

            string protocol = SelectedProto();
            string baseUrl = ProviderBaseBox.Text.Trim();
            string keep = ProviderModelBox.Text ?? string.Empty;

            // 切页面、走来走去地移焦点都会走到这里来；「这一家 + 地址 + 钥匙」没变过就别再打一次网络，
            // 真想强制刷新的话上面那个「刷新」按钮会走 announce=true。
            string signature = CurrentEditorId() + "\n" + baseUrl + "\n" + ProviderKeyBox.Password;
            if (!announce && signature == _lastProbeSignature)
            {
                return;
            }

            _lastProbeSignature = signature;

            _modelsRequest?.Cancel();
            _modelsRequest?.Dispose();
            _modelsRequest = null;

            if (baseUrl.Length == 0)
            {
                FillModelList(protocol, new List<string>(), keep);
                if (announce)
                {
                    SettingsStatus.Text = "先填接口地址，模型列表得照它去拉。";
                }

                return;
            }

            CancellationTokenSource request = new CancellationTokenSource();
            _modelsRequest = request;

            ProviderProfile probe = new ProviderProfile
            {
                Id = CurrentEditorId(),
                Name = ProviderNameBox.Text.Trim(),
                Protocol = protocol,
                BaseUrl = baseUrl,
                Model = keep,
                ApiKey = ProviderKeyBox.Password,
            };

            List<string> models;
            try
            {
                models = await ChatApi.ListModelsAsync(probe, request.Token);
            }
            catch
            {
                models = new List<string>();
            }

            if (request.IsCancellationRequested)
            {
                return;
            }

            FillModelList(protocol, models, keep);

            if (probe.Id.Length > 0)
            {
                _modelCache[probe.Id] = models;
            }

            if (announce)
            {
                if (models.Count == 0)
                {
                    SettingsStatus.Text = "这家没有返回模型列表（地址或密钥不对时就是这样），可以直接手打模型名。";
                }
                else
                {
                    string preview = string.Join("、", models.GetRange(0, Math.Min(6, models.Count)));
                    SettingsStatus.Text = "拉到 " + models.Count + " 个模型：" + preview
                                          + (models.Count > 6 ? " …" : string.Empty);
                }
            }
        }

        private void LoadProviderEditor(int index)
        {
            if (index < 0 || index >= _settings.Providers.Count)
            {
                return;
            }

            ProviderProfile provider = _settings.Providers[index];

            _loading = true;
            ProviderNameBox.Text = provider.Name;
            ProviderBaseBox.Text = provider.BaseUrl;
            FillModelList(provider.Protocol, CachedModels(provider.Id), provider.Model);
            ProviderKeyBox.Password = provider.ApiKey;
            ProviderTempBox.Value = provider.Temperature;
            ProviderMaxBox.Value = provider.MaxTokens;
            SelectProto(provider.Protocol);
            _loading = false;
        }

        private void SelectProto(string proto)
        {
            for (int i = 0; i < ProviderProtoBox.Items.Count; i++)
            {
                ComboBoxItem? item = ProviderProtoBox.Items[i] as ComboBoxItem;
                string tag = item?.Tag as string ?? string.Empty;
                if (string.Equals(tag, proto, StringComparison.OrdinalIgnoreCase))
                {
                    ProviderProtoBox.SelectedIndex = i;
                    return;
                }
            }

            ProviderProtoBox.SelectedIndex = 0;
        }

        private string SelectedProto()
        {
            ComboBoxItem? item = ProviderProtoBox.SelectedItem as ComboBoxItem;
            string? tag = item?.Tag as string;
            return string.IsNullOrEmpty(tag) ? "openai" : tag!;
        }

        private void CommitProviderEditor()
        {
            int index = ProviderPicker.SelectedIndex;
            if (index < 0 || index >= _settings.Providers.Count)
            {
                return;
            }

            ProviderProfile provider = _settings.Providers[index];

            string name = ProviderNameBox.Text.Trim();
            if (name.Length > 0)
            {
                provider.Name = name;
            }

            provider.BaseUrl = ProviderBaseBox.Text.Trim();
            provider.Model = ProviderModelBox.Text.Trim();
            provider.ApiKey = ProviderKeyBox.Password;
            provider.Protocol = SelectedProto();

            if (!double.IsNaN(ProviderTempBox.Value))
            {
                provider.Temperature = Math.Clamp(ProviderTempBox.Value, 0, 2);
            }

            if (!double.IsNaN(ProviderMaxBox.Value))
            {
                provider.MaxTokens = Math.Max(0, (int)ProviderMaxBox.Value);
            }

            SettingsStore.Save();
        }

        private void SaveProvider_Click(object sender, RoutedEventArgs e)
        {
            CommitProviderEditor();
            BuildProviderPicker();
            ApplyProviderToShell();
            SettingsStatus.Text = "已保存 " + _settings.Active().Name + "。";
        }

        private void AddProvider_Click(object sender, RoutedEventArgs e)
        {
            ProviderPreset? preset = PresetPicker.SelectedItem as ProviderPreset;
            if (preset == null)
            {
                SettingsStatus.Text = "先在预设下拉里挑一家。";
                return;
            }

            CommitProviderEditor();

            string keepKey = string.Empty;
            foreach (ProviderProfile existing in _settings.Providers)
            {
                if (string.Equals(existing.Id, preset.Id, StringComparison.OrdinalIgnoreCase))
                {
                    keepKey = existing.ApiKey;
                    break;
                }
            }

            _settings.Providers.Add(Providers.FromPreset(preset, keepKey));
            _settings.ActiveProvider = _settings.Providers.Count - 1;
            SettingsStore.Save();

            BuildProviderPicker();
            LoadProviderEditor(_settings.ActiveProvider);
            ApplyProviderToShell();
            SettingsStatus.Text = "已添加 " + preset.Name + "，并设为当前服务商。";
        }

        /// <summary>
        /// Adds a blank provider for the user to fill in by hand: any OpenAI-compatible endpoint.
        /// </summary>
        private void AddCustomProvider_Click(object sender, RoutedEventArgs e)
        {
            CommitProviderEditor();

            _settings.Providers.Add(new ProviderProfile
            {
                Id = Providers.CustomId(string.Empty),
                Name = "Custom API",
                Protocol = "openai",
                BaseUrl = string.Empty,
                Model = string.Empty,
                SystemPrompt = Persona.DefaultSys,
                Temperature = 0.7,
            });

            _settings.ActiveProvider = _settings.Providers.Count - 1;
            SettingsStore.Save();

            BuildProviderPicker();
            LoadProviderEditor(_settings.ActiveProvider);
            ApplyProviderToShell();
            SettingsStatus.Text = "已添加自定义 API，填好接口地址和模型就能用。";
        }

        private void SetActiveProvider_Click(object sender, RoutedEventArgs e)
        {
            CommitProviderEditor();

            if (ProviderPicker.SelectedIndex < 0)
            {
                SettingsStatus.Text = "先选中一家服务商。";
                return;
            }

            _settings.ActiveProvider = ProviderPicker.SelectedIndex;
            SettingsStore.Save();

            ApplyProviderToShell();
            RefreshPersonaFields();
            SettingsStatus.Text = "已把 " + _settings.Active().Name + " 设为当前服务商。";
        }

        private void DeleteProvider_Click(object sender, RoutedEventArgs e)
        {
            int index = ProviderPicker.SelectedIndex;
            if (index < 0)
            {
                SettingsStatus.Text = "先选中一家服务商。";
                return;
            }

            if (_settings.Providers.Count <= 1)
            {
                SettingsStatus.Text = "至少要留一家服务商。";
                return;
            }

            string name = _settings.Providers[index].Name;
            _settings.Providers.RemoveAt(index);
            if (_settings.ActiveProvider >= _settings.Providers.Count)
            {
                _settings.ActiveProvider = _settings.Providers.Count - 1;
            }

            SettingsStore.Save();

            BuildProviderPicker();
            LoadProviderEditor(_settings.ActiveProvider);
            ApplyProviderToShell();
            SettingsStatus.Text = "已删除 " + name + "。";
        }

        // ---- 角色

        private void RefreshPersonaFields()
        {
            ProviderProfile provider = _settings.Active();
            PersonaBox.Text = string.IsNullOrEmpty(provider.SystemPrompt) ? Persona.DefaultSys : provider.SystemPrompt;
        }

        private void ResetPersona_Click(object sender, RoutedEventArgs e)
        {
            PersonaBox.Text = Persona.DefaultSys;
            SettingsStatus.Text = "已恢复默认人设，别忘了点「应用到当前服务商」。";
        }

        private void ApplyPersona_Click(object sender, RoutedEventArgs e)
        {
            ProviderProfile provider = _settings.Active();
            provider.SystemPrompt = PersonaBox.Text;
            SettingsStore.Save();
            SettingsStatus.Text = "角色提示词已应用到 " + provider.Name + "。";
        }

        // ---- 权限

        private void RefreshPermissionFields()
        {
            _loading = true;
            ScopeRadios.SelectedIndex = IsMachineScope() ? 1 : 0;
            AutoApproveSwitch.IsOn = _settings.Agent.AutoApprove;
            _loading = false;

            UpdateAutoApproveHint();
        }

        private void UpdateAutoApproveHint()
        {
            AutoApproveHint.Text = AutoApproveSwitch.IsOn
                ? "危险操作会直接执行，不再弹确认卡片。"
                : "危险操作执行前会先弹一张确认卡片，等你点头。";
        }

        private void ScopeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Agent.Scope = ScopeRadios.SelectedIndex == 1 ? "machine" : "workspace";
            SettingsStore.Save();
            RefreshPermissionChip();
        }

        private void AutoApproveSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Agent.AutoApprove = AutoApproveSwitch.IsOn;
            SettingsStore.Save();
            UpdateAutoApproveHint();
            RefreshPermissionChip();
        }

        // ---- 外观 / 通用

        private void RefreshGeneralFields()
        {
            _loading = true;

            if (_theme == DomeThemeMode.Light)
            {
                ThemeCombo.SelectedIndex = 1;
            }
            else if (_theme == DomeThemeMode.Dark)
            {
                ThemeCombo.SelectedIndex = 2;
            }
            else
            {
                ThemeCombo.SelectedIndex = 0;
            }

            if (LanguageCombo.ItemsSource == null)
            {
                LanguageCombo.ItemsSource = LanguageCatalog.All;
            }

            LanguageCombo.SelectedIndex = LanguageCatalog.IndexOf(_settings.Language);

            SidebarWidthBox.Value = _settings.Ui.SidebarWidth;
            HistoryBox.Value = _settings.HistoryLimit;
            NotificationsSwitch.IsOn = _settings.Notifications;
            RunAtLoginSwitch.IsOn = _settings.RunAtLogin;

            _loading = false;
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            if (ThemeCombo.SelectedIndex == 1)
            {
                _theme = DomeThemeMode.Light;
            }
            else if (ThemeCombo.SelectedIndex == 2)
            {
                _theme = DomeThemeMode.Dark;
            }
            else
            {
                _theme = DomeThemeMode.System;
            }

            ApplyTheme();
            _settings.Theme = ThemeName(_theme);
            SettingsStore.Save();
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            string tag = LanguageCombo.SelectedItem is LanguageOption option
                ? option.Tag
                : LanguageCatalog.AutoTag;

            _settings.Language = tag;
            SettingsStore.Save();

            // 选完立刻把整个界面换过去
            ApplyLanguageToUi();

            _loading = true;
            LanguageCombo.ItemsSource = LanguageCatalog.All;
            LanguageCombo.SelectedIndex = LanguageCatalog.IndexOf(tag);
            _loading = false;
        }

        private void SidebarWidthBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_loading || double.IsNaN(args.NewValue))
            {
                return;
            }

            int width = (int)Math.Clamp(args.NewValue, 190, 560);
            _settings.Ui.SidebarWidth = width;
            NavView.OpenPaneLength = width;
            SettingsStore.Save();
        }

        private void HistoryBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_loading || double.IsNaN(args.NewValue))
            {
                return;
            }

            _settings.HistoryLimit = (int)Math.Clamp(args.NewValue, 2, 200);
            SettingsStore.Save();
        }

        private void NotificationsSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Notifications = NotificationsSwitch.IsOn;
            SettingsStore.Save();
        }

        private void RunAtLoginSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.RunAtLogin = RunAtLoginSwitch.IsOn;
            SettingsStore.Save();
            SettingsStatus.Text = "开机自启的注册表项会在周边阶段接上。";
        }

        // ---- 语音

        private void RefreshVoiceFields()
        {
            _loading = true;

            TtsSettings tts = _settings.Tts;
            VoiceEngineCombo.SelectedIndex = tts.Engine switch
            {
                "mimo" => 1,
                "microsoft" => 2,
                "vocaloid" => 3,
                _ => 0,
            };

            MimoKeyBox.Password = tts.MimoKey;
            MimoBaseBox.Text = tts.MimoBase;
            MimoModelBox.Text = tts.MimoModel;
            MimoVoiceBox.Text = tts.MimoVoice;
            MimoStyleBox.Text = tts.MimoStyle;

            WindowsVoiceCombo.DisplayMemberPath = "DisplayName";
            WindowsVoiceCombo.ItemsSource = Speech.Voices;
            WindowsVoiceCombo.SelectedIndex = VoiceIndexOf(tts.WindowsVoice);

            WindowsRateBox.Value = tts.WindowsRate;

            // 虚拟歌姬那一块
            VocaloidPresetCombo.SelectedIndex = tts.VocaloidPreset switch
            {
                "miku" => 1,
                "custom" => 2,
                _ => 0,
            };

            List<string> sapi = Speech.NarratorVoices();
            VocaloidVoiceCombo.ItemsSource = sapi;
            VocaloidVoiceCombo.SelectedIndex = sapi.FindIndex(name =>
                string.Equals(name, tts.VocaloidVoice, StringComparison.OrdinalIgnoreCase));

            (int pitch, int rate) = Speech.VocaloidTuning(tts);
            VocaloidPitchSlider.Value = tts.VocaloidPreset == "custom" ? tts.VocaloidPitch : pitch;
            VocaloidRateBox.Value = tts.VocaloidPreset == "custom" ? tts.VocaloidRate : rate;
            VocaloidPitchLabel.Text = (tts.VocaloidPreset == "custom" ? tts.VocaloidPitch : pitch) >= 0
                ? "+" + (tts.VocaloidPreset == "custom" ? tts.VocaloidPitch : pitch) + "%"
                : (tts.VocaloidPreset == "custom" ? tts.VocaloidPitch : pitch) + "%";

            VocaloidAppCombo.SelectedIndex = tts.VocaloidAppMode switch
            {
                "xstudio" => 1,
                "custom" => 2,
                _ => 0,
            };

            VocaloidAppBox.Text = tts.VocaloidApp;

            AutoReadSwitch.IsOn = tts.AutoRead;

            VoiceHintText.Text = "本机可用语音 " + Speech.Voices.Count + " 个，讲述人声库 " + sapi.Count + " 个。";

            _loading = false;

            RefreshVocaloidPanel();
        }

        private static int VoiceIndexOf(string voice)
        {
            IReadOnlyList<VoiceInformation> voices = Speech.Voices;
            string wanted = (voice ?? string.Empty).Trim();
            if (wanted.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < voices.Count; i++)
            {
                if (string.Equals(voices[i].Id, wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(voices[i].DisplayName, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void CommitVoiceFields()
        {
            TtsSettings tts = _settings.Tts;

            tts.Engine = SelectedTag(VoiceEngineCombo, "auto");
            tts.MimoKey = MimoKeyBox.Password;
            tts.MimoBase = MimoBaseBox.Text.Trim();
            tts.MimoModel = MimoModelBox.Text.Trim();
            tts.MimoVoice = MimoVoiceBox.Text.Trim();
            tts.MimoStyle = MimoStyleBox.Text.Trim();

            if (!double.IsNaN(WindowsRateBox.Value))
            {
                tts.WindowsRate = (int)Math.Clamp(WindowsRateBox.Value, -10, 10);
            }

            if (WindowsVoiceCombo.SelectedItem is VoiceInformation voice)
            {
                tts.WindowsVoice = voice.Id;
            }

            tts.VocaloidPreset = SelectedTag(VocaloidPresetCombo, "luotianyi");
            tts.VocaloidVoice = VocaloidVoiceCombo.SelectedItem as string ?? string.Empty;
            tts.VocaloidAppMode = SelectedTag(VocaloidAppCombo, "builtin");
            tts.VocaloidApp = VocaloidAppBox.Text.Trim();

            if (!double.IsNaN(VocaloidPitchSlider.Value))
            {
                tts.VocaloidPitch = (int)Math.Clamp(VocaloidPitchSlider.Value, -50, 60);
            }

            if (!double.IsNaN(VocaloidRateBox.Value))
            {
                tts.VocaloidRate = (int)Math.Clamp(VocaloidRateBox.Value, -10, 10);
            }

            tts.AutoRead = AutoReadSwitch.IsOn;
            SettingsStore.Save();
        }

        /// <summary>引擎不是「虚拟歌姬」时，那一整块设置收起来；MiMo 的字段也跟着引擎走。</summary>
        private void RefreshVocaloidPanel()
        {
            string engine = SelectedTag(VoiceEngineCombo, "auto");
            bool vocaloid = engine == "vocaloid";
            VocaloidPanel.Visibility = vocaloid ? Visibility.Visible : Visibility.Collapsed;

            // Microsoft 语音和虚拟歌姬都用不上 MiMo 的 Key、地址和模型
            MimoPanel.Visibility = engine == "microsoft" || engine == "vocaloid"
                ? Visibility.Collapsed
                : Visibility.Visible;

            // 预设不是「自定义」时，音高和语速跟着预设走，控件只是把它显示出来
            bool custom = SelectedTag(VocaloidPresetCombo, "luotianyi") == "custom";
            VocaloidPitchSlider.IsEnabled = custom;
            VocaloidRateBox.IsEnabled = custom;

            string mode = SelectedTag(VocaloidAppCombo, "builtin");
            VocaloidAppRow.Visibility = mode == "builtin" ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>X Studio 默认装在 Program Files，选了它就把路径直接填好。</summary>
        private const string XStudioPath =
            @"C:\Program Files\NetEase Cloud Music XStudio\NetEase Cloud Music XStudio.exe";

        private void VocaloidAppCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            string mode = SelectedTag(VocaloidAppCombo, "builtin");
            if (mode == "xstudio"
                && VocaloidAppBox.Text.Trim().Length == 0
                && File.Exists(XStudioPath))
            {
                VocaloidAppBox.Text = XStudioPath;
            }

            CommitVoiceFields();
            RefreshVocaloidPanel();
        }

        private async void BrowseVocaloidApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Windows.Storage.Pickers.FileOpenPicker picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

                Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
                if (file == null)
                {
                    return;
                }

                VocaloidAppBox.Text = file.Path;
                CommitVoiceFields();
                RefreshVocaloidPanel();
            }
            catch
            {
                // 选择器打不开就手填路径
            }
        }

        private void VocaloidPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            TtsSettings tts = _settings.Tts;
            tts.VocaloidPreset = SelectedTag(VocaloidPresetCombo, "luotianyi");

            (int pitch, int rate) = Speech.VocaloidTuning(tts);
            _loading = true;
            VocaloidPitchSlider.Value = pitch;
            VocaloidRateBox.Value = rate;
            VocaloidPitchLabel.Text = pitch >= 0 ? "+" + pitch + "%" : pitch + "%";
            _loading = false;

            CommitVoiceFields();
            RefreshVocaloidPanel();
        }

        private void VocaloidVoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading == false)
            {
                CommitVoiceFields();
            }
        }

        private void VocaloidPitchSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            int pitch = (int)Math.Clamp(e.NewValue, -50, 60);
            VocaloidPitchLabel.Text = pitch >= 0 ? "+" + pitch + "%" : pitch + "%";
            CommitVoiceFields();
        }

        private void VocaloidRateBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_loading)
            {
                return;
            }

            CommitVoiceFields();
        }

        private static string SelectedTag(ComboBox combo, string fallback)
        {
            ComboBoxItem? item = combo.SelectedItem as ComboBoxItem;
            string? tag = item?.Tag as string;
            return string.IsNullOrEmpty(tag) ? fallback : tag!;
        }

        private void VoiceEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading == false)
            {
                CommitVoiceFields();
                RefreshVocaloidPanel();
            }
        }

        private void AutoReadSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading == false)
            {
                CommitVoiceFields();
            }
        }

        private void SaveVoice_Click(object sender, RoutedEventArgs e)
        {
            CommitVoiceFields();
            SettingsStatus.Text = "语音设置已保存。";
            VoiceHintText.Text = "语音设置已保存。";
        }

        private void VoicePreview_Click(object sender, RoutedEventArgs e)
        {
            CommitVoiceFields();
            _ = SpeakAsync("你好主人，本小姐的嗓子已经准备好了。", string.Empty);
        }

        // ---- AI Agent Orb

        private static readonly string[] OrbPalette =
        {
            "4A90E2", "3B5BDB", "00B4D8", "2E8B57",
            "E0459B", "F2B705", "7C5CFF", "FF6B35",
        };

        private static readonly Color OrbDefaultPrimary = Color.FromArgb(255, 0x4A, 0x90, 0xE2);
        private static readonly Color OrbDefaultSecondary = Color.FromArgb(255, 0xFF, 0xC9, 0x0E);

        private void RefreshOrbFields()
        {
            _loading = true;

            OrbSettings orb = _settings.Orb;
            OrbEnabledSwitch.IsOn = orb.Enabled;
            OrbPrimaryBox.Text = orb.Primary;
            OrbSecondaryBox.Text = orb.Secondary;
            OrbSizeSlider.Value = orb.Size;
            OrbBarSizeSlider.Value = orb.BarSize;
            OrbSizeLabel.Text = "主窗口尺寸 " + orb.Size + "px";
            OrbBarSizeLabel.Text = "指令栏尺寸 " + orb.BarSize + "px";

            BuildOrbSwatches(OrbPrimarySwatches, OrbPrimaryBox);
            BuildOrbSwatches(OrbSecondarySwatches, OrbSecondaryBox);

            _loading = false;

            ApplyOrb();
        }

        private void BuildOrbSwatches(StackPanel host, TextBox target)
        {
            host.Children.Clear();

            foreach (string hex in OrbPalette)
            {
                Border swatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(ParseHex(hex, Colors.Gray)),
                };

                TextBox field = target;
                string value = hex;
                swatch.Tapped += (object sender, TappedRoutedEventArgs e) => field.Text = value;
                ToolTipService.SetToolTip(swatch, "#" + hex);
                host.Children.Add(swatch);
            }
        }

        /// <summary>Everything the orb needs, applied at once: the preview, the window, its tint.</summary>
        private void ApplyOrb()
        {
            OrbSettings orb = _settings.Orb;
            Color primary = ParseHex(orb.Primary, OrbDefaultPrimary);
            Color secondary = ParseHex(orb.Secondary, OrbDefaultSecondary);
            bool active = Speech.IsSpeaking;

            if (_orbPreview == null)
            {
                _orbPreview = new OrbVisual(orb.Size, primary, secondary);
                OrbPreviewHost.Children.Add(_orbPreview.Root);
            }
            else
            {
                _orbPreview.Resize(orb.Size);
                _orbPreview.SetColors(primary, secondary);
            }

            _orbPreview.Active = active;

            if (orb.Enabled)
            {
                if (_orbWindow == null)
                {
                    _orbWindow = new OrbWindow();
                    _orbWindow.Clicked += OrbWindow_Clicked;
                    _orbWindow.Apply(orb.Size, primary, secondary, active);
                    _orbWindow.Activate();
                    _orbWindow.PlaceBottomRight();
                }
                else
                {
                    _orbWindow.Apply(orb.Size, primary, secondary, active);
                }
            }
            else if (_orbWindow != null)
            {
                _orbWindow.Close();
                _orbWindow = null;

                if (_quickBar != null)
                {
                    _quickBar.Close();
                    _quickBar = null;
                }
            }
        }

        private void OrbWindow_Clicked(object? sender, EventArgs e)
        {
            ShowQuickBar();
        }

        /// <summary>
        /// A click on the orb pops the command bar out beside it - the same gesture a desktop
        /// assistant's ball uses - rather than dragging the whole window forward.
        /// </summary>
        private void ShowQuickBar()
        {
            if (_orbWindow == null)
            {
                Activate();
                return;
            }

            if (_quickBar == null)
            {
                _quickBar = new QuickBarWindow();
                _quickBar.Submitted += (object? sender, string text) => SendFromQuickBar(text);
            }

            _quickBar.OpenNear(_orbWindow.Position, _orbWindow.Size, _settings.Orb.BarSize, _settings.Language, true);
        }

        private void StartOrbPreview()
        {
            if (_orbPreviewTimer != null)
            {
                return;
            }

            _orbPreviewTimer = DispatcherQueue.CreateTimer();
            // 预览和桌面上的小球用同一个节奏，没必要为了预览多烧一倍的重排
            _orbPreviewTimer.Interval = TimeSpan.FromMilliseconds(33);
            _orbPreviewTimer.IsRepeating = true;
            _orbPreviewTimer.Tick += (DispatcherQueueTimer sender, object args) => _orbPreview?.Advance(33);
            _orbPreviewTimer.Start();
        }

        private void StopOrbPreview()
        {
            _orbPreviewTimer?.Stop();
            _orbPreviewTimer = null;
        }

        private void OrbEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Orb.Enabled = OrbEnabledSwitch.IsOn;
            SettingsStore.Save();
            ApplyOrb();
        }

        private void OrbColorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Orb.Primary = OrbPrimaryBox.Text.Trim();
            _settings.Orb.Secondary = OrbSecondaryBox.Text.Trim();
            SettingsStore.Save();
            ApplyOrb();
        }

        private void OrbSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Orb.Size = (int)Math.Clamp(e.NewValue, 16, 64);
            OrbSizeLabel.Text = "主窗口尺寸 " + _settings.Orb.Size + "px";
            SettingsStore.Save();
            ApplyOrb();
        }

        private void OrbBarSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            _settings.Orb.BarSize = (int)Math.Clamp(e.NewValue, 40, 180);
            OrbBarSizeLabel.Text = "指令栏尺寸 " + _settings.Orb.BarSize + "px";
            SettingsStore.Save();
        }

        /// <summary>Reads a bare hex colour such as "4A90E2"; anything else falls back.</summary>
        private static Color ParseHex(string value, Color fallback)
        {
            string text = (value ?? string.Empty).Trim().TrimStart('#');

            if (text.Length == 6
                && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
            {
                return Color.FromArgb(255,
                                      (byte)((rgb >> 16) & 0xFF),
                                      (byte)((rgb >> 8) & 0xFF),
                                      (byte)(rgb & 0xFF));
            }

            return fallback;
        }
    }

    /// <summary>What a row in the workspace tree stands for.</summary>
    public sealed class TreeNodeInfo
    {
        public TreeNodeInfo(string fullPath, string name, bool isFolder)
        {
            FullPath = fullPath;
            Name = name;
            IsFolder = isFolder;
        }

        public string FullPath { get; }

        public string Name { get; }

        public bool IsFolder { get; }

        public string Glyph => IsFolder ? "\uE8B7" : "\uE7C3";
    }
}
