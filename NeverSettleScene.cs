using System;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace DomeOSAgentDev
{
    /// <summary>
    /// The 「1+」 egg: the whole window turns into the Never Settle animation. The backdrop walks
    /// black → blue → cyan → orange → red, a thin outline of the wordmarks is sketched first, then
    /// the solid wordmarks are written on left to right, and two small pen marks land at the end.
    ///
    /// Timings and colours are the ones the original animation used. It is driven frame by frame from
    /// code because the "write on" pass needs per-frame control over a clip rectangle, and the clock
    /// is a real stopwatch so a slow frame does not stretch the scene.
    /// </summary>
    internal sealed class NeverSettleScene
    {
        private const double DurationMs = 5600;

        /// <summary>How far the outline copies are pushed out from the glyph edge, in pixels.</summary>
        private const double OutlineOffset = 1.7;

        private readonly Grid _host;
        private readonly Border _backdrop = new Border();

        /// <summary>
        /// 整窗那块底色用的画刷，只建一次，之后每帧只改颜色。
        /// 以前是每帧 new 一个 SolidColorBrush，60 帧/秒地扔对象，XAML 每次都要重新解析，
        /// 屏幕上就是一片闪。
        /// </summary>
        private readonly SolidColorBrush _backdropBrush = new SolidColorBrush(ColorAt(0));

        private readonly Grid _art = new Grid();
        private readonly StackPanel _outlineLayer = new StackPanel();
        private readonly StackPanel _solidLayer = new StackPanel();
        private readonly Canvas _marks = new Canvas();
        private readonly Rectangle _vignette = new Rectangle();
        private readonly TextBlock _tip = new TextBlock();
        private readonly FrameworkElement[] _outline = new FrameworkElement[2];
        private readonly TextBlock[] _solid = new TextBlock[2];
        private readonly RectangleGeometry[] _revealClips = new RectangleGeometry[4];
        private readonly Rectangle[] _ticks = new Rectangle[2];
        private readonly ScaleTransform[] _tickScales = new ScaleTransform[2];

        private readonly Stopwatch _clock = new Stopwatch();

        private DispatcherQueueTimer? _timer;
        private double _elapsed;
        private double _fontSize = 140;
        private double _shade = -1;
        private bool _anchored;
        private bool _closed;

        public NeverSettleScene(Grid host, DispatcherQueue queue)
        {
            _host = host;

            double width = _host.ActualWidth > 0 ? _host.ActualWidth : 1100;
            _fontSize = Math.Clamp(0.124 * width, 58, 168);

            Build();

            _clock.Start();
            Start(queue);
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }

            _clock.Stop();
            _host.Children.Clear();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            _host.Children.Clear();

            _backdrop.Background = _backdropBrush;

            string[] words = { "Never", "Settle" };

            _outlineLayer.Orientation = Orientation.Vertical;
            _outlineLayer.HorizontalAlignment = HorizontalAlignment.Center;
            _outlineLayer.VerticalAlignment = VerticalAlignment.Center;
            _outlineLayer.IsHitTestVisible = false;

            _solidLayer.Orientation = Orientation.Vertical;
            _solidLayer.HorizontalAlignment = HorizontalAlignment.Center;
            _solidLayer.VerticalAlignment = VerticalAlignment.Center;
            _solidLayer.IsHitTestVisible = false;

            for (int i = 0; i < 2; i++)
            {
                _outline[i] = MakeOutlineWord(words[i]);
                _solid[i] = MakeGlyph(words[i], Colors.White);

                _outlineLayer.Children.Add(_outline[i]);
                _solidLayer.Children.Add(_solid[i]);
            }

            // 四个「写字」动画各自持有一个裁切矩形，每帧只改 Rect，不再新建对象
            for (int i = 0; i < _revealClips.Length; i++)
            {
                _revealClips[i] = new RectangleGeometry { Rect = new Rect(0, 0, 0, _fontSize * 1.05) };
            }

            _outline[0].Clip = _revealClips[0];
            _outline[1].Clip = _revealClips[1];
            _solid[0].Clip = _revealClips[2];
            _solid[1].Clip = _revealClips[3];

            _art.Children.Add(_outlineLayer);
            _art.Children.Add(_solidLayer);
            _art.Children.Add(_marks);

            for (int i = 0; i < 2; i++)
            {
                Rectangle tick = new Rectangle
                {
                    Fill = new SolidColorBrush(Colors.White),
                    RadiusX = 2,
                    RadiusY = 2,
                    Opacity = 0,
                    IsHitTestVisible = false,
                };

                _tickScales[i] = new ScaleTransform();
                TransformGroup group = new TransformGroup();
                group.Children.Add(_tickScales[i]);
                group.Children.Add(new RotateTransform { Angle = i == 0 ? -26 : 30 });
                tick.RenderTransform = group;

                _ticks[i] = tick;
                _marks.Children.Add(tick);
            }

            // the panel's inner shadow, which deepens as the colour reaches the red end
            RadialGradientBrush shade = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                Center = new Point(0.5, 0.5),
                RadiusX = 0.78,
                RadiusY = 0.92,
            };
            shade.GradientStops.Add(new GradientStop { Offset = 0.52, Color = Colors.Transparent });
            shade.GradientStops.Add(new GradientStop { Offset = 1.0, Color = Color.FromArgb(150, 0, 0, 0) });
            _vignette.Fill = shade;
            _vignette.Opacity = 0.5;
            _vignette.IsHitTestVisible = false;

            _tip.Text = "按 Esc 或点击空白处返回";
            _tip.FontSize = 12.5;
            _tip.Foreground = new SolidColorBrush(Colors.White);
            _tip.HorizontalAlignment = HorizontalAlignment.Left;
            _tip.VerticalAlignment = VerticalAlignment.Bottom;
            _tip.Margin = new Thickness(22, 0, 0, 16);
            _tip.Opacity = 0;
            _tip.IsHitTestVisible = false;

            _host.Children.Add(_backdrop);
            _host.Children.Add(_art);
            _host.Children.Add(_vignette);
            _host.Children.Add(_tip);
        }

        private TextBlock MakeGlyph(string text, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Century Gothic, Segoe UI Variable Display, Segoe UI"),
                FontSize = _fontSize,
                FontWeight = FontWeights.Light,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                LineHeight = _fontSize * 0.92,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                IsHitTestVisible = false,
            };
        }

        /// <summary>
        /// The first pass is a sketch: an outline, not a filled glyph. There is no text stroke in
        /// XAML, so the outline is faked by four slightly offset copies of the word sitting around
        /// an empty middle, which reads as a thin pen line.
        /// </summary>
        private FrameworkElement MakeOutlineWord(string word)
        {
            Grid group = new Grid { IsHitTestVisible = false };

            double[,] offsets =
            {
                { -OutlineOffset, 0 },
                { OutlineOffset, 0 },
                { 0, -OutlineOffset },
                { 0, OutlineOffset },
            };

            for (int i = 0; i < offsets.GetLength(0); i++)
            {
                TextBlock copy = MakeGlyph(word, Colors.White);
                copy.Opacity = 0.66;
                copy.RenderTransform = new TranslateTransform
                {
                    X = offsets[i, 0],
                    Y = offsets[i, 1],
                };

                group.Children.Add(copy);
            }

            return group;
        }

        // ------------------------------------------------------------------ animation

        private void Start(DispatcherQueue queue)
        {
            _timer = queue.CreateTimer();
            // 30 帧够顺了。这块是整个窗口的底色，60 帧一秒地重画，慢一点的机器上就是一片闪。
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _timer.IsRepeating = true;
            _timer.Tick += (DispatcherQueueTimer sender, object args) => Tick();
            _timer.Start();
        }

        private void Tick()
        {
            if (_closed)
            {
                return;
            }

            _elapsed = _clock.Elapsed.TotalMilliseconds;
            double t = Math.Min(1.0, _elapsed / DurationMs);

            _backdropBrush.Color = ColorAt(t);

            // 内阴影只在真的变了才写：每帧都写一遍等于每帧都让合成器重算一次
            double shade = t < 0.20 ? 0.5 : (t < 0.62 ? 0.5 + (t - 0.20) / 0.42 * 0.5 : 1.0);
            shade = Math.Round(shade, 2);
            if (Math.Abs(shade - _shade) > 0.001)
            {
                _shade = shade;
                _vignette.Opacity = shade;
            }

            // the outline holds, then fades out once the solid wordmarks are written
            _outlineLayer.Opacity = t < 0.60 ? 1.0 : (t > 0.76 ? 0.0 : 1.0 - (t - 0.60) / 0.16);

            Reveal(0, _outline[0], 0.15, 1.65);
            Reveal(1, _outline[1], 0.52, 2.02);
            Reveal(2, _solid[0], 2.72, 4.22);
            Reveal(3, _solid[1], 3.18, 4.68);

            UpdateMarks();

            _tip.Opacity = Ease(Progress(5.0, 5.5));

            if (_elapsed >= DurationMs)
            {
                // the scene is static from here, so stop burning frames on it
                _timer?.Stop();
            }
        }

        /// <summary>
        /// The words are revealed by growing a clip rectangle from the left. The clip belongs to the
        /// element from the first frame - at zero width - so the finished word can never flash on
        /// screen before the animation starts, and the geometry object is reused instead of rebuilt.
        /// </summary>
        private void Reveal(int slot, FrameworkElement element, double from, double to)
        {
            double width = element.ActualWidth > 0 ? element.ActualWidth : 0;
            double height = element.ActualHeight > 0 ? element.ActualHeight : _fontSize * 1.05;
            double p = Ease(Progress(from, to));

            _revealClips[slot].Rect = new Rect(0, 0, width * p, height);
        }

        private void UpdateMarks()
        {
            if (!_anchored && _solid[0].ActualWidth > 0 && _solid[1].ActualWidth > 0)
            {
                _anchored = true;

                double tick1Height = Math.Max(3, _fontSize * 0.38);
                double tick2Height = Math.Max(3, _fontSize * 0.26);

                Point first = _solid[0].TransformToVisual(_marks).TransformPoint(new Point(0, 0));
                Point second = _solid[1].TransformToVisual(_marks)
                    .TransformPoint(new Point(_solid[1].ActualWidth, _solid[1].ActualHeight));

                Place(_ticks[0], _tickScales[0], first.X - 0.14 * _fontSize, first.Y + 0.20 * _fontSize,
                      tick1Height, true);
                Place(_ticks[1], _tickScales[1], second.X - 0.18 * _fontSize,
                      second.Y - 0.10 * _fontSize - tick2Height, tick2Height, false);
            }

            double tick1 = Progress(3.62, 3.94);
            double tick2 = Progress(3.94, 4.26);

            _ticks[0].Opacity = tick1;
            _ticks[1].Opacity = tick2;
            _tickScales[0].ScaleY = Ease(tick1);
            _tickScales[1].ScaleY = Ease(tick2);
        }

        /// <summary>Anchors a pen mark; both grow away from the centre of the stroke they belong to.</summary>
        private static void Place(Rectangle tick, ScaleTransform scale, double left, double top,
                                  double height, bool topOrigin)
        {
            tick.Width = 2.6;
            tick.Height = height;
            Canvas.SetLeft(tick, left);
            Canvas.SetTop(tick, top);

            double centerY = topOrigin ? 0 : height;
            scale.CenterX = 1.3;
            scale.CenterY = centerY;

            if (tick.RenderTransform is TransformGroup group && group.Children[1] is RotateTransform rotate)
            {
                rotate.CenterX = 1.3;
                rotate.CenterY = centerY;
            }
        }

        private double Progress(double fromSeconds, double toSeconds)
        {
            double now = _elapsed / 1000.0;
            if (toSeconds <= fromSeconds)
            {
                return now >= toSeconds ? 1.0 : 0.0;
            }

            return Math.Clamp((now - fromSeconds) / (toSeconds - fromSeconds), 0.0, 1.0);
        }

        private static double Ease(double p)
        {
            return p * p * (3 - 2 * p);
        }

        /// <summary>
        /// 整窗底色的走色。冷色段（黑 → 蓝 → 青）先自己暗回黑，再从黑升到暖色段（橙 → 红），
        /// 每一段内部用 smoothstep 过渡。
        ///
        /// 以前是青直接插值到橙：这两个颜色在色轮上是反的，中间那几帧必然经过一片发绿的灰
        /// （(8,168,216) 和 (248,152,40) 各半就是 (128,160,128)），满屏一闪就是主人说的「爆闪」。
        /// 现在冷色收进黑里再换暖色，全程亮度单调、不经过任何杂色，读起来是「慢慢暗下去又亮起来」。
        /// </summary>
        private static Color ColorAt(double t)
        {
            double[] stops = { 0.00, 0.10, 0.22, 0.30, 0.34, 0.42, 0.54, 0.62, 1.00 };
            uint[] colors =
            {
                0x181818, // 近黑
                0x0A98F8, // 蓝
                0x08A8D8, // 青
                0x0A5A78, // 深青，冷色段开始收
                0x141414, // 近黑：冷色交棒给暖色的那一瞬
                0xF89828, // 橙
                0xF86828, // 深橙
                0xE84828, // 红
                0xE84828, // 红，收尾静止
            };

            for (int i = 1; i < stops.Length; i++)
            {
                if (t <= stops[i])
                {
                    double span = stops[i] - stops[i - 1];
                    double k = span <= 0 ? 1.0 : (t - stops[i - 1]) / span;
                    return Mix(colors[i - 1], colors[i], Smoothstep(k));
                }
            }

            return FromRgb(colors[colors.Length - 1]);
        }

        /// <summary>两端速度为零的三次平滑：段与段交界处不会「一顿」，看着才不像跳帧。</summary>
        private static double Smoothstep(double k)
        {
            double p = Math.Clamp(k, 0.0, 1.0);
            return p * p * (3 - 2 * p);
        }

        /// <summary>
        /// 两个颜色的插值。分量必须先转成 double 再相减：这几个数是 uint，某个分量从大变小的时候
        /// （黑 0x18 → 蓝 0x0A 就是）uint 减法会下溢成 42 亿，乘上 k 再被 (byte) 一截，
        /// 出来的就是完全无关的颜色。之前满屏乱闪的绿和紫就是从这里来的。
        /// </summary>
        private static Color Mix(uint from, uint to, double k)
        {
            double r = (from >> 16) & 0xFF;
            double g = (from >> 8) & 0xFF;
            double b = from & 0xFF;

            double tr = (to >> 16) & 0xFF;
            double tg = (to >> 8) & 0xFF;
            double tb = to & 0xFF;

            return Color.FromArgb(255,
                                  ByteAt(r + (tr - r) * k),
                                  ByteAt(g + (tg - g) * k),
                                  ByteAt(b + (tb - b) * k));
        }

        /// <summary>夹到 0-255 再转 byte，不靠 (byte) 强转去「截断」。</summary>
        private static byte ByteAt(double value)
        {
            return (byte)Math.Clamp(Math.Round(value), 0, 255);
        }

        private static Color FromRgb(uint rgb)
        {
            return Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }
    }
}
