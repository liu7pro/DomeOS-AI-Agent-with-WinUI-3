using System;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace DomeOSAgentDev
{
    /// <summary>
    /// 语音听写：把主人说的话交给系统的语音识别变成文字。单击球体弹出的指令栏会直接开始听，
    /// 说完一句就把文字交出去发掉。识别不了（没有语音包、麦克风被系统关掉）时报一句人话，
    /// 绝不把异常扔到界面上。
    /// </summary>
    internal sealed class Dictation : IDisposable
    {
        private SpeechRecognizer? _recognizer;

        private bool _running;

        /// <summary>一旦确认这台机器听不了，就记下原因，别再反复试、反复报同一句话。</summary>
        private string _blocked = string.Empty;

        /// <summary>边说边出的中间结果。</summary>
        public event Action<string>? Partial;

        /// <summary>一整句说完了的最终结果。</summary>
        public event Action<string>? Final;

        /// <summary>起不来、听不清、被打断时给的交代。</summary>
        public event Action<string>? Notice;

        public bool IsListening => _running;

        /// <summary>
        /// 先看看能不能用语音：能就返回空串，不能就把原因（人话）返回给界面。
        /// 单击球体时会先问一遍，不能听的时候就别每点一次都报一次错。
        /// </summary>
        public string Prepare(string languageTag)
        {
            if (_blocked.Length > 0)
            {
                return _blocked;
            }

            try
            {
                if (_recognizer == null)
                {
                    _recognizer = Create(languageTag);
                    _recognizer.HypothesisGenerated += OnHypothesis;
                }

                return string.Empty;
            }
            catch (Exception error)
            {
                _recognizer = null;
                _blocked = HintFor(error);
                return _blocked;
            }
        }

        /// <summary>开始听一句。languageTag 为空就用系统默认的识别语言。</summary>
        public async void Listen(string languageTag)
        {
            if (_running)
            {
                return;
            }

            string problem = Prepare(languageTag);
            if (problem.Length > 0)
            {
                Notice?.Invoke(problem);
                return;
            }

            _running = true;

            try
            {
                SpeechRecognitionResult result = await _recognizer!.RecognizeAsync();
                _running = false;

                string text = (result.Text ?? string.Empty).Trim();
                if (result.Status == SpeechRecognitionResultStatus.Success && text.Length > 0)
                {
                    Final?.Invoke(text);
                    return;
                }

                if (result.Status == SpeechRecognitionResultStatus.Success)
                {
                    Notice?.Invoke("没听清，再说一遍吧。");
                    return;
                }

                // 语言包缺失、麦克风被关掉这类问题再试也没用，记下来别反复试
                if (result.Status == SpeechRecognitionResultStatus.TopicLanguageNotSupported
                    || result.Status == SpeechRecognitionResultStatus.MicrophoneUnavailable)
                {
                    _blocked = Describe(result.Status);
                }

                Notice?.Invoke(Describe(result.Status));
            }
            catch (Exception error)
            {
                _running = false;
                _blocked = HintFor(error);
                Notice?.Invoke(_blocked);
            }
        }

        /// <summary>不等了，收工。</summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            try
            {
                _ = _recognizer?.StopRecognitionAsync();
            }
            catch
            {
                // 已经停了
            }
        }

        private void OnHypothesis(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            string text = (args.Hypothesis?.Text ?? string.Empty).Trim();
            if (text.Length > 0)
            {
                Partial?.Invoke(text);
            }
        }

        private static SpeechRecognizer Create(string languageTag)
        {
            // 先按界面语言来，那台机器上没有这个语言的语音包就退回系统默认
            if (!string.IsNullOrEmpty(languageTag))
            {
                try
                {
                    return new SpeechRecognizer(new Language(languageTag));
                }
                catch
                {
                    // 换系统默认的语言继续
                }
            }

            return new SpeechRecognizer();
        }

        private static string Describe(SpeechRecognitionResultStatus status)
        {
            return status switch
            {
                SpeechRecognitionResultStatus.MicrophoneUnavailable =>
                    "识别不到麦克风：去「设置 → 隐私和安全性 → 麦克风」允许桌面应用使用麦克风。",
                SpeechRecognitionResultStatus.AudioQualityFailure => "听得不太清楚，再说一次吧。",
                SpeechRecognitionResultStatus.TimeoutExceeded => "等太久没听到声音，先停了。",
                SpeechRecognitionResultStatus.UserCanceled => string.Empty,
                SpeechRecognitionResultStatus.NetworkFailure => "语音识别需要联网，检查一下网络。",
                SpeechRecognitionResultStatus.TopicLanguageNotSupported =>
                    "这台电脑没装这个语言的语音识别包，换一种语言或者直接打字吧。",
                _ => "这次没听出来，直接打字也行。",
            };
        }

        private static string HintFor(Exception error)
        {
            // 0x80045509 / 0x8004550C 一类的 HRESULT 都是「没有可用的识别引擎」
            int code = error.HResult;
            if (code == unchecked((int)0x80045509) || code == unchecked((int)0x8004550C) || code == unchecked((int)0x80045510))
            {
                return "这台电脑还没有可以用的语音识别语言包，先在「设置 → 时间和语言 → 语音」里装一个。";
            }

            return "语音识别起不来：" + error.Message;
        }

        public void Dispose()
        {
            if (_recognizer != null)
            {
                try
                {
                    _recognizer.HypothesisGenerated -= OnHypothesis;
                    _recognizer.Dispose();
                }
                catch
                {
                    // 关不干净也不影响退出
                }

                _recognizer = null;
            }
        }
    }
}
