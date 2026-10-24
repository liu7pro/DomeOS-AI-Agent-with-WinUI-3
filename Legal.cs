using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DomeOSAgentDev
{
    /// <summary>
    /// The legal documents and the first-run consent screen. Both are plain files shipped in
    /// Assets\legal, so the wording can be corrected without touching code.
    /// </summary>
    internal static class Legal
    {
        /// <summary>What the first run asks you to read, and what the About card links to.</summary>
        public static readonly (string Label, string File)[] Documents =
        {
            ("隐私政策", "privacy.md"),
            ("用户协议", "terms.md"),
            ("开源声明", "opensource.md"),
        };

        /// <summary>鸣谢名单：每个人的主页地址。空字符串表示他没给主页，点了只好提示一句。</summary>
        private static readonly (string Handle, string Url)[] CreditsDevelopers =
        {
            ("wslaoliu7a", "https://space.bilibili.com/1834614293"),
            ("VMwareLY14", "https://space.bilibili.com/1227511340"),
            ("CangQiong01", "https://space.bilibili.com/1995157657"),
            ("follower", ""),
            ("LegspCpd", "https://space.bilibili.com/1614911881"),
            ("WeilanLH", "https://space.bilibili.com/1369640788"),
            ("Fantasy-XY808", "https://github.com/Fantasy-XY808"),
        };

        private const string NoticeTitle = "这些条款会实际影响你的电脑";

        private const string NoticeBody =
            "DomeOS AI 可以在你的电脑上读写文件、删除内容并执行命令，其权限等同于你的 Windows 账户。"
            + "被覆盖或删除的数据无法通过本软件恢复。请在授权前确认你理解这一点。";

        public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "legal");

        public static string Read(string fileName)
        {
            try
            {
                string path = Path.Combine(Folder, fileName);
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch
            {
                // fall through to the placeholder
            }

            return "（这份文件没有随安装包一起发布，请重新安装或在项目仓库中查看。）";
        }

        /// <summary>Shows one document in a scrollable dialog.</summary>
        public static async Task ShowDocumentAsync(XamlRoot root, string label, string fileName)
        {
            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = label,
                Content = new ScrollViewer
                {
                    MaxHeight = 460,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = Read(fileName),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12.5,
                    },
                },
                CloseButtonText = "关闭",
            };

            await dialog.ShowAsync();
        }

        /// <summary>
        /// 鸣谢，与 Electron 版的对话框一致：先是「开发者名单」，然后是 DomeOS Team 和排成两列的
        /// 名字，每个名字点开都是它的搜索结果页。名单放在代码里而不是文件里，和那边一样。
        /// </summary>
        public static async Task ShowCreditsAsync(XamlRoot root)
        {
            StackPanel body = new StackPanel { Spacing = 6 };

            body.Children.Add(new TextBlock
            {
                Text = "开发者名单",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });

            body.Children.Add(new TextBlock
            {
                Text = "DomeOS Team",
                FontSize = 19,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6),
            });

            Grid grid = new Grid { ColumnSpacing = 12, RowSpacing = 9 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            List<(string Handle, string Url)> people = new List<(string, string)>(CreditsDevelopers);
            int rows = (people.Count + 1) / 2;
            for (int i = 0; i < rows; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            // 名单下面那一行提示。没留主页的名字点了就在这里说一声——不能在对话框上面再开一个
            // 对话框，WinUI 一次只允许一个 ContentDialog 开着（那样会直接崩）。
            TextBlock note = new TextBlock
            {
                Text = "点名字就打开他的主页。",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            };

            for (int i = 0; i < people.Count; i++)
            {
                (string handle, string url) = people[i];
                HyperlinkButton link = new HyperlinkButton
                {
                    Content = "@" + handle,
                    Padding = new Thickness(0, 2, 0, 2),
                };

                if (url.Length > 0)
                {
                    link.NavigateUri = new Uri(url);
                }
                else
                {
                    string name = handle;
                    link.Click += (object sender, RoutedEventArgs e) =>
                        note.Text = "未提供：@" + name + " 没有留可以跳转的个人主页。";
                }

                Grid.SetRow(link, i / 2);
                Grid.SetColumn(link, i % 2);
                grid.Children.Add(link);
            }

            body.Children.Add(grid);
            body.Children.Add(note);

            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = "鸣谢",
                Content = body,
                CloseButtonText = "关闭",
            };

            await dialog.ShowAsync();
        }

        /// <summary>
        /// The first-run gate: the warning, the three documents, and an agreement box that only
        /// unlocks once every document has been opened. Returns true when the user agreed.
        /// </summary>
        public static async Task<bool> ShowOnboardingAsync(XamlRoot root)
        {
            HashSet<string> opened = new HashSet<string>();

            CheckBox agree = new CheckBox
            {
                Content = "我已阅读并同意以上全部条款",
                IsEnabled = false,
            };

            TextBlock gate = new TextBlock
            {
                Text = "请阅读全部三个文件后勾选",
            };
            gate.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];

            StackPanel consent = new StackPanel { Spacing = 10 };
            consent.Children.Add(new TextBlock
            {
                Text = NoticeTitle,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            consent.Children.Add(new TextBlock { Text = NoticeBody, TextWrapping = TextWrapping.Wrap });

            TextBlock documentText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            ScrollViewer documentView = new ScrollViewer
            {
                MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = documentText,
                Visibility = Visibility.Collapsed,
            };

            FontIcon back = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets"),
                Glyph = "\uE72B",
            };
            Button backButton = new Button { Content = back, Visibility = Visibility.Collapsed };
            ToolTipService.SetToolTip(backButton, "返回");

            StackPanel body = new StackPanel { Spacing = 10 };
            body.Children.Add(consent);

            // 返回按钮必须在 consent 之外：打开文档时 consent 会被折叠，
            // 放在里面的话用户读完就回不来了，勾选框也永远露不出来。
            body.Children.Add(backButton);

            body.Children.Add(documentView);

            ContentDialog dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = "请先阅读并同意以下文件",
                Content = body,
                PrimaryButtonText = "同意并开始使用",
                CloseButtonText = "不同意",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
            };

            agree.Checked += (object sender, RoutedEventArgs e) => dialog.IsPrimaryButtonEnabled = true;
            agree.Unchecked += (object sender, RoutedEventArgs e) => dialog.IsPrimaryButtonEnabled = false;

            foreach ((string label, string fileName) in Documents)
            {
                Button open = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
                string capturedLabel = label;
                string capturedFile = fileName;

                open.Click += (object sender, RoutedEventArgs e) =>
                {
                    opened.Add(capturedFile);
                    open.Content = "✓ " + capturedLabel;

                    // the box stays locked until every document has actually been opened
                    if (opened.Count >= 3)
                    {
                        agree.IsEnabled = true;
                        gate.Text = "勾选后才能开始使用";
                    }
                    else
                    {
                        gate.Text = "请阅读全部三个文件后勾选（已读 " + opened.Count + "/3）";
                    }

                    documentText.Text = Read(capturedFile);
                    documentView.Visibility = Visibility.Visible;
                    consent.Visibility = Visibility.Collapsed;
                    backButton.Visibility = Visibility.Visible;
                };

                consent.Children.Add(open);
            }

            backButton.Click += (object sender, RoutedEventArgs e) =>
            {
                documentView.Visibility = Visibility.Collapsed;
                consent.Visibility = Visibility.Visible;
                backButton.Visibility = Visibility.Collapsed;
            };

            consent.Children.Add(agree);
            consent.Children.Add(gate);

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
