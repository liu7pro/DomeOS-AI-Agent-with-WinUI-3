using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DomeOSAgentDev
{
    /// <summary>
    /// The command bar that pops out when the orb is clicked: a small always-on-top input strip
    /// beside the orb. Enter hands the text over and hides the bar; Esc, or clicking away, does the
    /// same without sending. It also carries a microphone, so one click on the orb is enough to
    /// dictate a message instead of typing it.
    /// </summary>
    public sealed partial class QuickBarWindow : Window
    {
        private const int DwmwaBorderColor = 34;
        private const int DwmwaSystemBackdropType = 38;
        private const int DwmsbtNone = 1;

        private const int BarWidth = 460;

        private readonly Dictation _dictation = new Dictation();

        private string _languageTag = string.Empty;

        /// <summary>Raised with the text the user submitted.</summary>
        public event EventHandler<string>? Submitted;

        public QuickBarWindow()
        {
            InitializeComponent();
            Title = "指令栏";
            SetupWindow();

            _dictation.Partial += text => OnUi(() =>
            {
                BarBox.Text = text;
                BarBox.SelectionStart = BarBox.Text.Length;
                BarStatus.Text = "本小姐在听…";
            });

            _dictation.Final += text => OnUi(() =>
            {
                BarBox.Text = text;
                BarStatus.Text = "听清楚了，发出去了。";
                Submit();
            });

            _dictation.Notice += text => OnUi(() =>
            {
                BarStatus.Text = text;
                SetListeningVisual(false);
            });

            BarRoot.LostFocus += (object sender, RoutedEventArgs e) => Hide();
            Activated += QuickBarWindow_Activated;
        }

        /// <summary>Opens the bar just above the orb and puts the caret in it.</summary>
        public void OpenNear(Windows.Graphics.PointInt32 orbPosition, Windows.Graphics.SizeInt32 orbSize,
                             int height, string languageTag, bool listen)
        {
            int barHeight = Math.Clamp(height, 64, 200);
            _languageTag = languageTag ?? string.Empty;

            try
            {
                int x = orbPosition.X + orbSize.Width - BarWidth;
                int y = orbPosition.Y - barHeight - 12;

                if (y < 0)
                {
                    y = orbPosition.Y + orbSize.Height + 12;
                }

                if (x < 0)
                {
                    x = 0;
                }

                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, BarWidth, barHeight));
                AppWindow.Show();
                Activate();
            }
            catch
            {
                // leaving it where it is still works
            }

            BarBox.Text = string.Empty;
            BarBox.Focus(FocusState.Programmatic);
            SetListeningVisual(false);

            // 单击球体就直接开听，说完自动发出去；这台机器听不了的话就说明原因，别装作在听
            string problem = listen ? _dictation.Prepare(_languageTag) : string.Empty;
            if (listen && problem.Length == 0)
            {
                BarStatus.Text = "本小姐在听…";
                StartDictation();
            }
            else if (listen)
            {
                BarStatus.Text = problem + " 先打字也行。";
            }
            else
            {
                BarStatus.Text = "回车发送 · Esc 收起 · 麦克风可以说话";
            }
        }

        public void Hide()
        {
            _dictation.Stop();
            SetListeningVisual(false);

            try
            {
                AppWindow.Hide();
            }
            catch
            {
                // already hidden
            }
        }

        /// <summary>识别结果是从后台线程回来的，落到界面上之前先回到 UI 线程。</summary>
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

        // ------------------------------------------------------------------ 语音

        private void BarMic_Click(object sender, RoutedEventArgs e)
        {
            if (_dictation.IsListening)
            {
                _dictation.Stop();
                BarStatus.Text = "先不听了。";
                SetListeningVisual(false);
                return;
            }

            StartDictation();
        }

        private void StartDictation()
        {
            SetListeningVisual(true);
            _dictation.Listen(_languageTag);

            if (!_dictation.IsListening)
            {
                // 起不来的话 Notice 会把原因写出来
                SetListeningVisual(false);
            }
        }

        private void SetListeningVisual(bool listening)
        {
            BarMicIcon.Glyph = listening ? "\uE721" : "\uE720";
            BarStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                listening ? "AppAccentBrush" : "TextFillColorSecondaryBrush"];
        }

        // ------------------------------------------------------------------ window setup

        private void SetupWindow()
        {
            try
            {
                AppWindow.IsShownInSwitchers = false;
            }
            catch
            {
                // not fatal
            }

            try
            {
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.SetBorderAndTitleBar(false, false);
                    presenter.IsAlwaysOnTop = true;
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                }
            }
            catch
            {
                // not fatal
            }

            try
            {
                IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

                Margins margins;
                margins.Left = -1;
                margins.Top = -1;
                margins.Right = -1;
                margins.Bottom = -1;
                DwmExtendFrameIntoClientArea(handle, ref margins);

                int noBackdrop = DwmsbtNone;
                DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref noBackdrop, sizeof(int));

                int noBorder = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(int));
            }
            catch
            {
                // not fatal
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // ------------------------------------------------------------------ input

        private void QuickBarWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // clicking anywhere else puts the bar away, the way a popover behaves
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                Hide();
            }
        }

        private void BarBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Enter)
            {
                Submit();
                e.Handled = true;
            }
        }

        private void BarSend_Click(object sender, RoutedEventArgs e)
        {
            Submit();
        }

        private void Submit()
        {
            string text = (BarBox.Text ?? string.Empty).Trim();
            BarBox.Text = string.Empty;
            Hide();

            if (text.Length > 0)
            {
                Submitted?.Invoke(this, text);
            }
        }
    }
}
