using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace DomeOSAgentDev
{
    /// <summary>
    /// 任务栏通知区里的那个小图标。
    ///
    /// WinUI3 自己没有托盘控件，所以这里直接走 Win32 的 Shell_NotifyIcon：建一个永不显示的
    /// 消息窗口收通知，右键弹一个原生菜单，左键把主窗口叫回来。菜单项通过回调交回主窗口去执行。
    /// </summary>
    internal sealed class TrayIcon
    {
        private const string ClassName = "DomeOSAgentTrayWindow";

        private const uint WmDestroy = 0x0002;
        private const uint WmNull = 0x0000;
        private const uint WmContextMenu = 0x007B;
        private const uint WmLButtonUp = 0x0202;
        private const uint WmRButtonUp = 0x0205;
        private const uint WmLButtonDblClk = 0x0203;
        private const uint WmApp = 0x8000;

        private const uint NinSelect = 0x0400;
        private const uint NinKeySelect = 0x0401;

        private const uint NimAdd = 0;
        private const uint NimDelete = 2;
        private const uint NimSetVersion = 4;

        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const uint NifShowTip = 0x00000080;

        private const int ImageIcon = 1;
        private const uint LrLoadFromFile = 0x00000010;
        private const uint LrDefaultSize = 0x00000040;

        private const uint MfString = 0x00000000;
        private const uint MfSeparator = 0x00000800;

        private const uint TpmLeftAlign = 0x0000;
        private const uint TpmBottomAlign = 0x0020;
        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCmd = 0x0100;

        private const int IdShow = 1001;
        private const int IdChat = 1002;
        private const int IdWorkspace = 1003;
        private const int IdSettings = 1004;
        private const int IdExit = 1005;

        private static readonly WndProcDelegate Handler = WindowProc;
        private static TrayIcon? _live;
        private static ushort _classHandle;

        private readonly Action<string> _action;
        private readonly DispatcherQueue? _queue;
        private readonly uint _callbackMessage = WmApp + 21;

        private IntPtr _handle;
        private IntPtr _icon;
        private uint _taskbarCreated;
        private bool _added;

        /// <summary><paramref name="action"/> 收到 "show" / "chat" / "workspace" / "settings" / "exit"。</summary>
        public TrayIcon(Action<string> action)
        {
            _action = action;
            _queue = DispatcherQueue.GetForCurrentThread();

            Create();
        }

        // ------------------------------------------------------------------ 生命周期

        public void Show()
        {
            AddIcon();
        }

        public void Hide()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            if (_added)
            {
                NotifyIconData data = Data();
                Shell_NotifyIcon(NimDelete, ref data);
                _added = false;
            }
        }

        public void Dispose()
        {
            Hide();

            if (_icon != IntPtr.Zero)
            {
                DestroyIcon(_icon);
                _icon = IntPtr.Zero;
            }

            if (_handle != IntPtr.Zero)
            {
                DestroyWindow(_handle);
                _handle = IntPtr.Zero;
            }
        }

        // ------------------------------------------------------------------ 窗口

        private void Create()
        {
            EnsureClass();

            _handle = CreateWindowEx(0, ClassName, "DomeOS Agent Tray", 0 /* not visible */,
                                     0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_handle == IntPtr.Zero)
            {
                Log("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
                return;
            }

            // 资源管理器重启之后要把图标重新挂上去，它会广播这个注册消息
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

            _icon = LoadTrayIcon();
            _live = this;
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
            if (_classHandle == 0)
            {
                Log("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
            }

            Marshal.FreeHGlobal(name);
        }

        /// <summary>托盘起不来时留一行线索，不然界面上什么都看不到。</summary>
        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"),
                                   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [tray] " + message + "\r\n");
            }
            catch
            {
                // 写不了就算了
            }
        }

        private static IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
        {
            TrayIcon? self = _live;
            if (self == null || self._handle == IntPtr.Zero)
            {
                return DefWindowProc(handle, message, wParam, lParam);
            }

            if (message == self._callbackMessage)
            {
                // 版本 4 的通知：lParam 低字是事件，高字是图标 id
                uint evt = (uint)lParam & 0xFFFF;

                if (evt == WmRButtonUp || evt == WmContextMenu)
                {
                    self.ShowMenu();
                }
                else if (evt == WmLButtonUp || evt == WmLButtonDblClk || evt == NinSelect || evt == NinKeySelect)
                {
                    self.Invoke("show");
                }

                return IntPtr.Zero;
            }

            if (message == self._taskbarCreated)
            {
                self._added = false;
                self.AddIcon();
                return IntPtr.Zero;
            }

            if (message == WmDestroy)
            {
                return IntPtr.Zero;
            }

            return DefWindowProc(handle, message, wParam, lParam);
        }

        // ------------------------------------------------------------------ 图标

        private static IntPtr LoadTrayIcon()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (File.Exists(path))
                {
                    IntPtr loaded = LoadImage(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
                    if (loaded != IntPtr.Zero)
                    {
                        return loaded;
                    }
                }
            }
            catch
            {
                // 读不到就用系统默认图标顶上
            }

            return LoadIcon(IntPtr.Zero, (IntPtr)32512);
        }

        private void AddIcon()
        {
            if (_handle == IntPtr.Zero || _added)
            {
                return;
            }

            NotifyIconData data = Data();
            if (!Shell_NotifyIcon(NimAdd, ref data))
            {
                Log("Shell_NotifyIcon(NIM_ADD) failed: " + Marshal.GetLastWin32Error());
                return;
            }

            data.uVersion = 4;
            Shell_NotifyIcon(NimSetVersion, ref data);
            _added = true;
        }

        private NotifyIconData Data()
        {
            return new NotifyIconData
            {
                cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
                hWnd = _handle,
                uID = 1,
                uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
                uCallbackMessage = _callbackMessage,
                hIcon = _icon,
                szTip = AppInfo.DisplayName,
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };
        }

        // ------------------------------------------------------------------ 菜单

        private void ShowMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return;
            }

            try
            {
                AppendMenu(menu, MfString, IdShow, "打开 " + AppInfo.DisplayName);
                AppendMenu(menu, MfSeparator, 0, null);
                AppendMenu(menu, MfString, IdChat, "对话");
                AppendMenu(menu, MfString, IdWorkspace, "工作区");
                AppendMenu(menu, MfString, IdSettings, "设置");
                AppendMenu(menu, MfSeparator, 0, null);
                AppendMenu(menu, MfString, IdExit, "退出");

                SetMenuDefaultItem(menu, IdShow, 0);

                GetCursorPos(out CursorPoint cursor);

                // 弹菜单前必须先把我们的窗口设成前台，否则点菜单外面它不会消失
                SetForegroundWindow(_handle);
                uint picked = TrackPopupMenu(menu,
                                             TpmReturnCmd | TpmRightButton | TpmBottomAlign | TpmLeftAlign,
                                             cursor.X, cursor.Y, 0, _handle, IntPtr.Zero);
                PostMessage(_handle, WmNull, IntPtr.Zero, IntPtr.Zero);

                switch ((int)picked)
                {
                    case IdShow:
                        Invoke("show");
                        break;
                    case IdChat:
                        Invoke("chat");
                        break;
                    case IdWorkspace:
                        Invoke("workspace");
                        break;
                    case IdSettings:
                        Invoke("settings");
                        break;
                    case IdExit:
                        Invoke("exit");
                        break;
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private void Invoke(string key)
        {
            if (_queue != null)
            {
                _queue.TryEnqueue(() => _action(key));
            }
            else
            {
                _action(key);
            }
        }

        // ------------------------------------------------------------------ interop

        private delegate IntPtr WndProcDelegate(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int cx, int cy, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);

        [DllImport("user32.dll")]
        private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y,
                                                  int reserved, IntPtr window, IntPtr rect);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);
    }
}
