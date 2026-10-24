using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DomeOSAgentDev
{
    /// <summary>
    /// 把模型吐出来的 Markdown 变成能看的控件树。
    ///
    /// 只做聊天里真正常见的那一档：标题、列表、引用、代码块、行内代码、粗体、斜体、链接、分隔线。
    /// 表格之类的先当普通段落排，不为了它把整个渲染器写复杂。
    /// </summary>
    internal static class Markdown
    {
        private static readonly FontFamily MonoFont = new FontFamily("Consolas, Cascadia Mono, Courier New");

        /// <summary>一段 Markdown 变成一列控件，调用方直接塞进面板里。</summary>
        public static StackPanel Build(string source)
        {
            StackPanel host = new StackPanel { Spacing = 8 };
            string[] lines = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            int index = 0;
            while (index < lines.Length)
            {
                string line = lines[index];

                if (line.Trim().Length == 0)
                {
                    index++;
                    continue;
                }

                // 围栏代码块
                Match fence = Regex.Match(line, "^\\s*```\\s*([\\w+#.-]*)\\s*$");
                if (fence.Success)
                {
                    List<string> code = new List<string>();
                    index++;
                    while (index < lines.Length && !Regex.IsMatch(lines[index], "^\\s*```\\s*$"))
                    {
                        code.Add(lines[index]);
                        index++;
                    }

                    index++;
                    host.Children.Add(CodeBlock(string.Join("\n", code)));
                    continue;
                }

                // 分隔线
                if (Regex.IsMatch(line, "^\\s*([-*_])\\s*(\\1\\s*){2,}$"))
                {
                    host.Children.Add(new Rectangle
                    {
                        Height = 1,
                        Margin = new Thickness(0, 6, 0, 6),
                        Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    });

                    index++;
                    continue;
                }

                // 标题
                Match heading = Regex.Match(line, "^\\s*(#{1,6})\\s+(.*)$");
                if (heading.Success)
                {
                    int level = heading.Groups[1].Value.Length;
                    TextBlock title = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = level <= 2 ? 19.0 : (level == 3 ? 16.5 : 14.5),
                        Margin = new Thickness(0, 4, 0, 0),
                    };

                    Inline(heading.Groups[2].Value, title.Inlines);
                    host.Children.Add(title);
                    index++;
                    continue;
                }

                // 引用
                if (Regex.IsMatch(line, "^\\s*>\\s?"))
                {
                    List<string> quoted = new List<string>();
                    while (index < lines.Length && Regex.IsMatch(lines[index], "^\\s*>\\s?"))
                    {
                        quoted.Add(Regex.Replace(lines[index], "^\\s*>\\s?", string.Empty));
                        index++;
                    }

                    host.Children.Add(Quote(quoted));
                    continue;
                }

                // 列表（有序 / 无序），允许缩进一层
                Match item = Regex.Match(line, "^(\\s*)([-*+]|\\d+[.)])\\s+(.*)$");
                if (item.Success)
                {
                    host.Children.Add(ListBlock(lines, ref index));
                    continue;
                }

                // 普通段落：连续的几行合成一段
                List<string> paragraph = new List<string>();
                while (index < lines.Length
                       && lines[index].Trim().Length > 0
                       && !Regex.IsMatch(lines[index], "^\\s*```")
                       && !Regex.IsMatch(lines[index], "^\\s*#{1,6}\\s")
                       && !Regex.IsMatch(lines[index], "^\\s*>")
                       && !Regex.IsMatch(lines[index], "^(\\s*)([-*+]|\\d+[.)])\\s+"))
                {
                    paragraph.Add(lines[index].Trim());
                    index++;
                }

                TextBlock block = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                };

                for (int i = 0; i < paragraph.Count; i++)
                {
                    if (i > 0)
                    {
                        block.Inlines.Add(new LineBreak());
                    }

                    Inline(paragraph[i], block.Inlines);
                }

                host.Children.Add(block);
            }

            if (host.Children.Count == 0)
            {
                host.Children.Add(new TextBlock { Text = source ?? string.Empty, TextWrapping = TextWrapping.Wrap });
            }

            return host;
        }

        // ------------------------------------------------------------------ 块

        private static FrameworkElement CodeBlock(string code)
        {
            Border frame = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            frame.Child = new TextBlock
            {
                Text = code,
                FontFamily = MonoFont,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };

            return frame;
        }

        private static FrameworkElement Quote(List<string> lines)
        {
            StackPanel body = new StackPanel { Spacing = 4 };
            foreach (string line in lines)
            {
                TextBlock block = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                };

                Inline(line, block.Inlines);
                body.Children.Add(block);
            }

            Grid host = new Grid();
            host.Children.Add(new Border
            {
                Width = 3,
                Margin = new Thickness(0, 2, 0, 2),
                CornerRadius = new CornerRadius(2),
                Background = (Brush)Application.Current.Resources["AppAccentBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            Border content = new Border
            {
                Margin = new Thickness(12, 0, 0, 0),
                Child = body,
            };

            host.Children.Add(content);
            return host;
        }

        private static FrameworkElement ListBlock(string[] lines, ref int index)
        {
            StackPanel list = new StackPanel { Spacing = 3 };
            int ordered = 0;

            while (index < lines.Length)
            {
                Match item = Regex.Match(lines[index], "^(\\s*)([-*+]|\\d+[.)])\\s+(.*)$");
                if (!item.Success)
                {
                    break;
                }

                bool isOrdered = char.IsDigit(item.Groups[2].Value[0]);
                if (isOrdered)
                {
                    ordered++;
                }

                int indent = item.Groups[1].Value.Length;
                string marker = isOrdered ? ordered + "." : "•";

                Grid row = new Grid { Margin = new Thickness(indent > 0 ? 16 : 0, 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                TextBlock bullet = new TextBlock
                {
                    Text = marker,
                    Margin = new Thickness(0, 0, 8, 0),
                    MinWidth = 14,
                    Opacity = 0.8,
                };

                TextBlock text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                Inline(item.Groups[3].Value, text.Inlines);

                Grid.SetColumn(bullet, 0);
                Grid.SetColumn(text, 1);
                row.Children.Add(bullet);
                row.Children.Add(text);

                list.Children.Add(row);
                index++;
            }

            return list;
        }

        // ------------------------------------------------------------------ 行内

        /// <summary>把一行里的粗体、斜体、行内代码和链接摊成 Inline。</summary>
        private static void Inline(string text, InlineCollection target)
        {
            int i = 0;
            while (i < text.Length)
            {
                // 行内代码
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        target.Add(new Run
                        {
                            Text = text.Substring(i + 1, end - i - 1),
                            FontFamily = MonoFont,
                            FontSize = 12.5,
                        });

                        i = end + 1;
                        continue;
                    }
                }

                // 链接 [文字](地址)
                if (text[i] == '[')
                {
                    Match link = Regex.Match(text.Substring(i), "^\\[([^\\]]*)\\]\\(([^)\\s]+)\\)");
                    if (link.Success)
                    {
                        Hyperlink hyperlink = new Hyperlink();
                        try
                        {
                            hyperlink.NavigateUri = new Uri(link.Groups[2].Value);
                        }
                        catch
                        {
                            // 地址不合法就当普通文字
                            hyperlink.Inlines.Add(new Run { Text = link.Groups[1].Value });
                            target.Add(hyperlink);
                            i += link.Length;
                            continue;
                        }

                        hyperlink.Inlines.Add(new Run { Text = link.Groups[1].Value });
                        target.Add(hyperlink);
                        i += link.Length;
                        continue;
                    }
                }

                // 粗体 **x** / __x__
                if (i + 1 < text.Length && (text[i] == '*' || text[i] == '_')
                    && text[i + 1] == text[i])
                {
                    string mark = text.Substring(i, 2);
                    int end = text.IndexOf(mark, i + 2, StringComparison.Ordinal);
                    if (end > i + 1)
                    {
                        Bold bold = new Bold();
                        Inline(text.Substring(i + 2, end - i - 2), bold.Inlines);
                        target.Add(bold);
                        i = end + 2;
                        continue;
                    }
                }

                // 斜体 *x* / _x_（不跨行，也不吃掉单词里的下划线）
                if (text[i] == '*' || (text[i] == '_' && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))))
                {
                    int end = text.IndexOf(text[i], i + 1);
                    if (end > i + 1)
                    {
                        Italic italic = new Italic();
                        Inline(text.Substring(i + 1, end - i - 1), italic.Inlines);
                        target.Add(italic);
                        i = end + 1;
                        continue;
                    }
                }

                // 普通文字：一路吃到下一个特殊字符
                int next = i + 1;
                while (next < text.Length && text[next] != '`' && text[next] != '[' && text[next] != '*'
                       && text[next] != '_')
                {
                    next++;
                }

                target.Add(new Run { Text = text.Substring(i, next - i) });
                i = next;
            }
        }
    }
}
