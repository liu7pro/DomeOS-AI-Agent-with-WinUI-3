using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace DomeOSAgentDev
{
    /// <summary>
    /// The floating Agent Orb.
    ///
    /// It is deliberately not a XAML window. A WinUI window cannot show per-pixel transparency: the
    /// usual frame-extension trick only produces the system's dark glass, which is why the first
    /// attempt looked like a mask sitting over the desktop. A layered Win32 window driven by
    /// UpdateLayeredWindow paints the sphere itself, pixel by pixel, with real alpha - so the ball
    /// sits on the desktop with nothing around it.
    ///
    /// Dragging is done from the screen cursor and the window's position when the drag started, so the
    /// window's own motion never feeds back into the next reading.
    /// </summary>
    internal sealed class OrbWindow
    {
        private const string ClassName = "DomeOSAgentOrbWindow";

        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTopmost = 0x00000008;
        private const int WsExNoActivate = 0x08000000;

        private const int SwpNoSize = 0x0001;
        private const int SwpNoZOrder = 0x0004;
        private const int SwpNoActivate = 0x0010;

        private const int WmDestroy = 0x0002;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmMouseMove = 0x0200;
        private const int WmRButtonUp = 0x0205;

        private const int UlwAlpha = 0x00000002;
        private const uint AcSrcOver = 0;
        private const byte AcSrcAlpha = 1;
        private static readonly WndProcDelegate Handler = WindowProc;
        private static ushort _classHandle;

        private readonly DispatcherQueueTimer? _timer;

        private IntPtr _handle;
        private IntPtr _memoryDc;
        private IntPtr _bitmap;
        private IntPtr _bitmapBits;

        private byte[] _pixels = Array.Empty<byte>();
        private int _box = 59;
        private double _dpi = 1.0;

        private Color _primary = Color.FromArgb(255, 0x4A, 0x90, 0xE2);
        private Color _secondary = Color.FromArgb(255, 0xFF, 0xC9, 0x0E);
        private double _size = 34;
        private bool _active;
        private double _phase;

        private bool _dragging;
        private bool _moved;
        private CursorPoint _grabCursor;
        private int _grabX;
        private int _grabY;

        /// <summary>Raised when the orb is clicked rather than dragged.</summary>
        public event EventHandler? Clicked;

        public OrbWindow()
        {
            // the window procedure is static, so the instance it speaks for is parked here
            _live = this;

            Create();

            DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
            if (queue != null)
            {
                _timer = queue.CreateTimer();
                // 空闲时它只是慢慢起伏，33ms 一帧就够顺了；干活时才提到 16ms
                _timer.Interval = TimeSpan.FromMilliseconds(33);
                _timer.IsRepeating = true;
                _timer.Tick += (DispatcherQueueTimer sender, object args) => Frame();
                _timer.Start();
            }
        }

        public Windows.Graphics.PointInt32 Position
        {
            get
            {
                return GetWindowRect(_handle, out Rect rect)
                    ? new Windows.Graphics.PointInt32(rect.Left, rect.Top)
                    : new Windows.Graphics.PointInt32(0, 0);
            }
        }

        public Windows.Graphics.SizeInt32 Size => new Windows.Graphics.SizeInt32(_box, _box);

        // ------------------------------------------------------------------ public surface

        public void Activate()
        {
            if (_handle != IntPtr.Zero)
            {
                ShowWindow(_handle, 4);
            }
        }

        public void Apply(double size, Color primary, Color secondary, bool active)
        {
            _size = Math.Max(8, size);
            _primary = primary;
            _secondary = secondary;
            _active = active;

            // 说话 / 干活时才需要 16ms 的呼吸；闲着的时候把重绘降到 33ms 一帧，省一半 CPU
            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(active ? 16 : 33);
            }

            int box = (int)Math.Ceiling(_size * 1.5) + 8;
            if (box != _box)
            {
                _box = box;
                BuildSurface();

                if (_handle != IntPtr.Zero)
                {
                    SetWindowPos(_handle, IntPtr.Zero, 0, 0, _box, _box,
                                 SwpNoZOrder | SwpNoActivate | 0x0002 /* SWP_NOMOVE */);
                }
            }

            Render();
            Present();
        }

        public void PlaceBottomRight()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            Rect work;
            if (!SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, out work, 0))
            {
                work = new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
            }

            SetWindowPos(_handle, IntPtr.Zero,
                         work.Right - _box - 24, work.Bottom - _box - 24, 0, 0,
                         SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        public void Close()
        {
            _timer?.Stop();

            if (_handle != IntPtr.Zero)
            {
                DestroyWindow(_handle);
                _handle = IntPtr.Zero;
            }

            if (_memoryDc != IntPtr.Zero)
            {
                DeleteDC(_memoryDc);
                _memoryDc = IntPtr.Zero;
            }

            if (_bitmap != IntPtr.Zero)
            {
                DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
                _bitmapBits = IntPtr.Zero;
            }
        }

        // ------------------------------------------------------------------ window

        private void Create()
        {
            EnsureClass();

            _dpi = 1.0;

            _handle = CreateWindowEx(
                WsExLayered | WsExToolWindow | WsExTopmost | WsExNoActivate,
                ClassName,
                "Agent Orb",
                WsPopup,
                0, 0, _box, _box,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            if (_handle == IntPtr.Zero)
            {
                return;
            }

            uint dpi = GetDpiForWindow(_handle);
            if (dpi > 0)
            {
                _dpi = dpi / 96.0;
            }

            BuildSurface();
            Render();
            Present();
        }

        private static void EnsureClass()
        {
            if (_classHandle != 0)
            {
                return;
            }

            IntPtr name = Marshal.StringToHGlobalUni(ClassName);
            WndClassEx template = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Handler),
                hInstance = GetModuleHandle(null),
                lpszClassName = name,
            };

            _classHandle = RegisterClassEx(ref template);
            Marshal.FreeHGlobal(name);
        }

        private static IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
        {
            OrbWindow? self = _live;

            switch (message)
            {
                case WmLButtonDown:
                    self?.StartDrag();
                    return IntPtr.Zero;

                case WmMouseMove:
                    self?.Drag();
                    return IntPtr.Zero;

                case WmLButtonUp:
                    self?.EndDrag();
                    return IntPtr.Zero;

                case WmRButtonUp:
                    self?.Raise();
                    return IntPtr.Zero;

                case WmDestroy:
                    return IntPtr.Zero;
            }

            return DefWindowProc(handle, message, wParam, lParam);
        }

        // the window procedure is static, so the instance it belongs to is kept here
        private static OrbWindow? _live;

        private void Raise()
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }

        private void StartDrag()
        {
            _dragging = true;
            _moved = false;
            SetCapture(_handle);
            GetCursorPos(out _grabCursor);

            if (GetWindowRect(_handle, out Rect rect))
            {
                _grabX = rect.Left;
                _grabY = rect.Top;
            }
        }

        private void Drag()
        {
            if (!_dragging)
            {
                return;
            }

            GetCursorPos(out CursorPoint now);
            int dx = now.X - _grabCursor.X;
            int dy = now.Y - _grabCursor.Y;

            if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            {
                _moved = true;
            }

            SetWindowPos(_handle, IntPtr.Zero, _grabX + dx, _grabY + dy, 0, 0,
                         SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        private void EndDrag()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            ReleaseCapture();

            if (!_moved)
            {
                Raise();
            }
        }

        // ------------------------------------------------------------------ painting

        private void BuildSurface()
        {
            if (_memoryDc != IntPtr.Zero)
            {
                DeleteDC(_memoryDc);
                _memoryDc = IntPtr.Zero;
            }

            if (_bitmap != IntPtr.Zero)
            {
                DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
                _bitmapBits = IntPtr.Zero;
            }

            _pixels = new byte[_box * _box * 4];

            BitmapInfo info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = _box,
                    biHeight = -_box,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            _memoryDc = CreateCompatibleDC(IntPtr.Zero);
            _bitmap = CreateDIBSection(_memoryDc, ref info, 0, out _bitmapBits, IntPtr.Zero, 0);

            if (_bitmap != IntPtr.Zero && _memoryDc != IntPtr.Zero)
            {
                SelectObject(_memoryDc, _bitmap);
            }
        }

        private void Frame()
        {
            _phase += 16.0 / (_active ? 700 : 2200);
            if (_phase > 1)
            {
                _phase -= Math.Floor(_phase);
            }

            Render();
            Present();
        }

        /// <summary>
        /// Draws the sphere and its glow straight into the premultiplied BGRA buffer. The sphere is a
        /// radial gradient with an off-centre highlight, and its rim takes the second colour; the
        /// coverage term is what gives the ball a clean antialiased edge.
        /// </summary>
        private void Render()
        {
            if (_pixels.Length == 0)
            {
                return;
            }

            double wave = 0.5 + 0.5 * Math.Sin(_phase * 2 * Math.PI);

            double center = _box / 2.0;
            double radius = _size * _dpi / 2.0 * (0.94 + 0.10 * wave);
            double glowRadius = radius * 1.5;
            double glowAlpha = ((_active ? 0.55 : 0.32) + 0.35 * wave) * 0.8;

            Color highlight = Mix(_primary, Color.FromArgb(255, 255, 255, 255), 0.62);
            Color rim = Mix(_secondary, Color.FromArgb(255, 0, 0, 0), 0.34);

            double lightX = center - radius * 0.30;
            double lightY = center - radius * 0.34;

            for (int y = 0; y < _box; y++)
            {
                for (int x = 0; x < _box; x++)
                {
                    double px = x + 0.5;
                    double py = y + 0.5;

                    double dx = px - center;
                    double dy = py - center;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    double sphere = Math.Clamp(radius + 0.5 - distance, 0, 1);
                    double glow = 0;

                    if (distance < glowRadius && glowRadius > 0)
                    {
                        double falloff = 1 - distance / glowRadius;
                        glow = falloff * falloff * glowAlpha;
                    }

                    double alpha = sphere + glow * (1 - sphere);
                    int offset = (y * _box + x) * 4;

                    if (alpha <= 0.002)
                    {
                        _pixels[offset] = 0;
                        _pixels[offset + 1] = 0;
                        _pixels[offset + 2] = 0;
                        _pixels[offset + 3] = 0;
                        continue;
                    }

                    double bodyR = _primary.R;
                    double bodyG = _primary.G;
                    double bodyB = _primary.B;

                    if (sphere > 0)
                    {
                        double hx = px - lightX;
                        double hy = py - lightY;
                        double shine = Math.Clamp(Math.Sqrt(hx * hx + hy * hy) / (radius * 1.25), 0, 1);
                        shine = shine * shine * (3 - 2 * shine);

                        bodyR = Lerp(highlight.R, _primary.R, shine);
                        bodyG = Lerp(highlight.G, _primary.G, shine);
                        bodyB = Lerp(highlight.B, _primary.B, shine);

                        double edge = Math.Clamp((distance / radius - 0.80) / 0.20, 0, 1);
                        bodyR = Lerp(bodyR, rim.R, edge);
                        bodyG = Lerp(bodyG, rim.G, edge);
                        bodyB = Lerp(bodyB, rim.B, edge);
                    }

                    // glow sits behind the sphere, so it only shows in the uncovered part
                    double outR = (bodyR * sphere + _primary.R * glow * (1 - sphere)) / alpha;
                    double outG = (bodyG * sphere + _primary.G * glow * (1 - sphere)) / alpha;
                    double outB = (bodyB * sphere + _primary.B * glow * (1 - sphere)) / alpha;

                    // premultiplied
                    _pixels[offset] = ToByte(outB * alpha);
                    _pixels[offset + 1] = ToByte(outG * alpha);
                    _pixels[offset + 2] = ToByte(outR * alpha);
                    _pixels[offset + 3] = ToByte(alpha * 255);
                }
            }
        }

        private void Present()
        {
            if (_handle == IntPtr.Zero || _bitmapBits == IntPtr.Zero || _pixels.Length == 0)
            {
                return;
            }

            Marshal.Copy(_pixels, 0, _bitmapBits, _pixels.Length);

            IntPtr screenDc = GetDC(IntPtr.Zero);

            Point destination = new Point();
            if (GetWindowRect(_handle, out Rect rect))
            {
                destination.X = rect.Left;
                destination.Y = rect.Top;
            }

            PixelSize size;
            size.cx = _box;
            size.cy = _box;

            Point source;
            source.X = 0;
            source.Y = 0;

            BlendFunction blend;
            blend.BlendOp = (byte)AcSrcOver;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = AcSrcAlpha;

            UpdateLayeredWindow(_handle, screenDc, ref destination, ref size, _memoryDc,
                                ref source, 0, ref blend, UlwAlpha);

            ReleaseDC(IntPtr.Zero, screenDc);
        }

        private static byte ToByte(double value)
        {
            return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        }

        private static double Lerp(double from, double to, double k)
        {
            return from + (to - from) * k;
        }

        private static Color Mix(Color from, Color to, double k)
        {
            return Color.FromArgb(
                255,
                (byte)Math.Round(Lerp(from.R, to.R, k)),
                (byte)Math.Round(Lerp(from.G, to.G, k)),
                (byte)Math.Round(Lerp(from.B, to.B, k)));
        }

        // ------------------------------------------------------------------ interop

        private delegate IntPtr WndProcDelegate(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct WndClassEx
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelSize
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public int Colors;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WndClassEx template);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName,
                                                    int style, int x, int y, int width, int height,
                                                    IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height,
                                                int flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, out Rect value, uint winIni);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr handle, IntPtr dc);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr handle, IntPtr screenDc, ref Point destination,
                                                       ref PixelSize size, IntPtr sourceDc, ref Point source,
                                                       int colorKey, ref BlendFunction blend, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage,
                                                     out IntPtr bits, IntPtr section, uint offset);
    }
}
