using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace DomeOSAgentDev
{
    /// <summary>
    /// Reading answers out loud. Two engines: MiMo TTS over its own HTTP API, and the voices that
    /// already live on this machine. MiMo is tried first when a key exists; the Windows voices are
    /// the fallback, so speech only fails when both are unavailable.
    ///
    /// An explicit non-Chinese language never goes through MiMo - that model is Chinese and would
    /// read the text in Chinese - so it goes straight to a Windows voice for that language.
    /// </summary>
    internal static class Speech
    {
        public const int MaxSpeechChars = 8000;

        public const string DefaultStyle = "自然、亲切、清晰的语气，语速适中，像在和朋友聊天。";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private static readonly SpeechSynthesizer Synth = new SpeechSynthesizer();

        private static MediaPlayer? _player;
        private static CancellationTokenSource? _cancel;
        private static DispatcherQueue? _uiQueue;

        /// <summary>Raised whenever speech starts or stops, so the buttons can follow along.</summary>
        public static event EventHandler? StateChanged;

        public static bool IsSpeaking { get; private set; }

        /// <summary>
        /// 外部配音软件那条路的提示：文字已经交出去了，但这边没有真的出声，
        /// 界面把这句显示出来，别让主人以为朗读失败。
        /// </summary>
        public static string LastNotice { get; private set; } = "";

        public static IReadOnlyList<VoiceInformation> Voices => SpeechSynthesizer.AllVoices;

        /// <summary>Stops whatever is playing. Safe to call when nothing is.</summary>
        public static void Stop()
        {
            RememberQueue();

            CancellationTokenSource? cancel = _cancel;
            _cancel = null;
            if (cancel != null)
            {
                try
                {
                    cancel.Cancel();
                    cancel.Dispose();
                }
                catch
                {
                    // a disposed source is nothing to worry about
                }
            }

            MediaPlayer? player = _player;
            _player = null;
            if (player != null)
            {
                try
                {
                    player.Pause();
                    player.Dispose();
                }
                catch
                {
                    // the player may already be gone
                }
            }

            SetSpeaking(false);
        }

        /// <summary>Reads text out. Returns an empty string on success, otherwise the reason.</summary>
        public static Task<string> SpeakAsync(AppSettings settings, string rawText)
        {
            return SpeakAsync(settings, rawText, string.Empty);
        }

        /// <summary>Reads text out, forcing a language (used by the English easter-egg read).</summary>
        public static async Task<string> SpeakAsync(AppSettings settings, string rawText, string languageTag)
        {
            RememberQueue();

            string text = CleanForSpeech(rawText);
            if (text.Length == 0)
            {
                return "没有可朗读的内容";
            }

            TtsSettings tts = settings.Tts;
            Stop();
            LastNotice = string.Empty;

            CancellationTokenSource cancel = new CancellationTokenSource();
            _cancel = cancel;

            string lang = (languageTag ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                if (lang.Length > 0 && !lang.StartsWith("zh", StringComparison.Ordinal))
                {
                    return await SpeakWindowsAsync(text, ResolveVoice(tts, lang), tts.WindowsRate, cancel);
                }

                string engine = tts.Engine == "mimo" || tts.Engine == "microsoft" || tts.Engine == "vocaloid"
                    ? tts.Engine
                    : "auto";
                string warning = string.Empty;

                // 虚拟歌姬走的是本机声库 + 音高，和 MiMo 那条网络链路没有关系，先单独分出去
                if (engine == "vocaloid")
                {
                    // 选了外部配音软件（X Studio 之类）就把文字交给它，不再用本机声库
                    if (tts.VocaloidAppMode != "builtin" && tts.VocaloidApp.Trim().Length > 0)
                    {
                        return HandOffToApp(tts, text);
                    }

                    return await SpeakVocaloidAsync(tts, text, cancel);
                }

                if (engine != "microsoft" && tts.MimoKey.Trim().Length > 0)
                {
                    string mimoError = await SpeakMimoAsync(tts, text, cancel);

                    // stopping means stopping: a cancelled run never falls through to another engine
                    if (cancel.IsCancellationRequested)
                    {
                        return mimoError.Length > 0 ? mimoError : "已停止";
                    }

                    if (mimoError.Length == 0)
                    {
                        return string.Empty;
                    }

                    warning = "MiMo 语音：" + mimoError;
                }
                else if (engine == "mimo")
                {
                    warning = "选了 MiMo 语音但没填 API Key";
                }

                if (cancel.IsCancellationRequested)
                {
                    return "已停止";
                }

                string windowsError = await SpeakWindowsAsync(text, ResolveVoice(tts, string.Empty),
                                                              tts.WindowsRate, cancel);
                if (windowsError.Length == 0)
                {
                    return string.Empty;
                }

                return warning.Length > 0 ? warning + " - " + windowsError : windowsError;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ------------------------------------------------------------------ text cleanup

        /// <summary>
        /// Marks the text up for reading aloud: code blocks and markdown syntax would be read as
        /// noise, so they are dropped or unwrapped, and a filesystem path keeps only its last
        /// segment so a long path is not spelled out in full.
        /// </summary>
        public static string CleanForSpeech(string text)
        {
            string s = text ?? string.Empty;

            s = Regex.Replace(s, "```[\\s\\S]*?(?:```|$)", "。");
            s = Regex.Replace(s, "`([^`]*)`", "$1");
            s = Regex.Replace(s, "!\\[[^\\]]*\\]\\([^)]*\\)", string.Empty);
            s = Regex.Replace(s, "\\[([^\\]]*)\\]\\([^)]*\\)", "$1");
            s = Regex.Replace(s, "(?:[A-Za-z]:[\\\\/]|\\\\\\\\)[^`\"'\\r\\n\\u3000-\\u303F\\u4E00-\\u9FFF\\uFF00-\\uFFEF]*",
                              PathTail);
            s = Regex.Replace(s, "^\\s{0,3}#{1,6}\\s*", string.Empty, RegexOptions.Multiline);
            s = Regex.Replace(s, "^\\s{0,3}>\\s?", string.Empty, RegexOptions.Multiline);
            s = Regex.Replace(s, "(\\*\\*|__)(.*?)\\1", "$2");
            s = Regex.Replace(s, "(\\*|_)([^*_\\n]+)\\1", "$2");
            s = Regex.Replace(s, "^\\s{0,3}[-*+]\\s+", string.Empty, RegexOptions.Multiline);
            s = Regex.Replace(s, "^\\s{0,3}\\d+\\.\\s+", string.Empty, RegexOptions.Multiline);
            s = Regex.Replace(s, "^\\s{0,3}[-:|\\s]+$", string.Empty, RegexOptions.Multiline);
            s = s.Replace("|", "，");
            s = Regex.Replace(s, "[ \\t]+", " ");
            s = Regex.Replace(s, "\\s*\\n\\s*", "。");
            s = Regex.Replace(s, "。{2,}", "。");
            s = Regex.Replace(s, "^[。，、\\s]+", string.Empty);
            s = s.Trim();

            if (s.Length > MaxSpeechChars)
            {
                s = s.Substring(0, MaxSpeechChars);
            }

            return s;
        }

        private static string PathTail(Match match)
        {
            string trimmed = Regex.Replace(match.Value, "[\\s.,;:!?)\\]}>]+$", string.Empty);
            string[] parts = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string tail = parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
            return tail + match.Value.Substring(trimmed.Length);
        }

        // ------------------------------------------------------------------ MiMo engine

        /// <summary>Accepts both the base (…/v1) and the full endpoint (…/v1/chat/completions).</summary>
        private static string MimoEndpoint(string baseUrl)
        {
            string trimmed = string.IsNullOrWhiteSpace(baseUrl)
                ? "https://api.xiaomimimo.com/v1"
                : baseUrl.Trim().TrimEnd('/');

            return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : trimmed + "/chat/completions";
        }

        /// <summary>Returns an empty string on success, otherwise the reason it failed.</summary>
        private static async Task<string> SpeakMimoAsync(TtsSettings tts, string text, CancellationTokenSource cancel)
        {
            try
            {
                MimoRequest payload = new MimoRequest
                {
                    Model = string.IsNullOrWhiteSpace(tts.MimoModel) ? "mimo-v2.5-tts" : tts.MimoModel,
                    Messages = new[]
                    {
                        new MimoMessage
                        {
                            Role = "user",
                            Content = string.IsNullOrWhiteSpace(tts.MimoStyle) ? DefaultStyle : tts.MimoStyle,
                        },
                        new MimoMessage { Role = "assistant", Content = text },
                    },
                    Audio = new MimoAudio
                    {
                        Format = "wav",
                        Voice = string.IsNullOrWhiteSpace(tts.MimoVoice) ? "mimo_default" : tts.MimoVoice,
                    },
                };

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, MimoEndpoint(tts.MimoBase));
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + tts.MimoKey.Trim());
                request.Content = new StringContent(JsonSerializer.Serialize(payload, MimoJson), Encoding.UTF8,
                                                   "application/json");

                using HttpResponseMessage response = await Http.SendAsync(request, cancel.Token);
                string body = await response.Content.ReadAsStringAsync(cancel.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string detail = body.Length > 300 ? body.Substring(0, 300) : body;
                    return "HTTP " + (int)response.StatusCode + (detail.Length > 0 ? " " + detail : string.Empty);
                }

                string? data = ExtractAudio(body);
                if (string.IsNullOrEmpty(data))
                {
                    return "响应里没有音频";
                }

                byte[] bytes = Convert.FromBase64String(data);
                InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);

                return await PlayAsync(stream, "audio/wav", cancel);
            }
            catch (OperationCanceledException)
            {
                return "已停止";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static readonly JsonSerializerOptions MimoJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        private static string? ExtractAudio(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("choices", out JsonElement choices)
                    || choices.GetArrayLength() == 0)
                {
                    return null;
                }

                JsonElement first = choices[0];
                if (!first.TryGetProperty("message", out JsonElement message)
                    || !message.TryGetProperty("audio", out JsonElement audio))
                {
                    return null;
                }

                if (audio.ValueKind == JsonValueKind.String)
                {
                    return audio.GetString();
                }

                if (audio.ValueKind == JsonValueKind.Object
                    && audio.TryGetProperty("data", out JsonElement data)
                    && data.ValueKind == JsonValueKind.String)
                {
                    return data.GetString();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ Windows engine

        private static VoiceInformation? ResolveVoice(TtsSettings tts, string languageTag)
        {
            string wanted = (tts.WindowsVoice ?? string.Empty).Trim();

            if (wanted.Length > 0)
            {
                foreach (VoiceInformation voice in SpeechSynthesizer.AllVoices)
                {
                    if (string.Equals(voice.Id, wanted, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(voice.DisplayName, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return voice;
                    }
                }
            }

            if (languageTag.Length > 0)
            {
                foreach (VoiceInformation voice in SpeechSynthesizer.AllVoices)
                {
                    if (voice.Language.StartsWith(languageTag, StringComparison.OrdinalIgnoreCase))
                    {
                        return voice;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 走讲述人那条路：SAPI 的语音栈。Windows 的「讲述人」用的就是这套语音（包括装了自然语音
        /// 之后那些），语速也比 WinRT 那套慢得多、更像人话，0 就是正常语速。
        /// </summary>
        private static Task<string> SpeakWindowsAsync(string text, VoiceInformation? voice, int rate,
                                                       CancellationTokenSource cancel)
        {
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();
            System.Speech.Synthesis.SpeechSynthesizer synth = new System.Speech.Synthesis.SpeechSynthesizer();

            try
            {
                // SAPI 的速率就是 -10 到 10，0 是正常
                synth.Rate = Math.Clamp(rate, -10, 10);
                synth.Volume = 100;

                string wanted = (voice?.DisplayName ?? string.Empty).Trim();
                if (wanted.Length > 0)
                {
                    foreach (System.Speech.Synthesis.InstalledVoice installed in synth.GetInstalledVoices())
                    {
                        string name = installed.VoiceInfo.Name;
                        if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                            || name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                            || wanted.Contains(name, StringComparison.OrdinalIgnoreCase))
                        {
                            synth.SelectVoice(name);
                            break;
                        }
                    }
                }

                synth.SpeakCompleted += (object? sender, System.Speech.Synthesis.SpeakCompletedEventArgs args) =>
                {
                    try
                    {
                        synth.Dispose();
                    }
                    catch
                    {
                        // 关不干净也不影响
                    }

                    done.TrySetResult(string.Empty);
                };

                cancel.Token.Register(() =>
                {
                    try
                    {
                        synth.SpeakAsyncCancelAll();
                        synth.Dispose();
                    }
                    catch
                    {
                        // 已经停了
                    }

                    done.TrySetResult(string.Empty);
                });

                synth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                try
                {
                    synth.Dispose();
                }
                catch
                {
                    // 忽略
                }

                return Task.FromResult(ex.Message);
            }

            return done.Task;
        }

        /// <summary>这台电脑上讲述人能用的语音名单。</summary>
        public static List<string> NarratorVoices()
        {
            List<string> names = new List<string>();

            try
            {
                using System.Speech.Synthesis.SpeechSynthesizer synth = new System.Speech.Synthesis.SpeechSynthesizer();
                foreach (System.Speech.Synthesis.InstalledVoice installed in synth.GetInstalledVoices())
                {
                    if (installed.Enabled)
                    {
                        names.Add(installed.VoiceInfo.Name);
                    }
                }
            }
            catch
            {
                // 没有就返回空名单
            }

            return names;
        }

        // ------------------------------------------------------------------ 虚拟歌姬

        /// <summary>歌姬预设的音高（百分比）和语速（SAPI -10..10）。</summary>
        public static (int Pitch, int Rate) VocaloidTuning(TtsSettings tts)
        {
            switch (tts.VocaloidPreset)
            {
                case "miku":
                    return (35, 2);
                case "custom":
                    return (Math.Clamp(tts.VocaloidPitch, -50, 60), Math.Clamp(tts.VocaloidRate, -10, 10));
                default:
                    return (20, 1);
            }
        }

        /// <summary>
        /// 把文字交给外部配音软件。X Studio 没有公开接口，能做的是：把文字写成临时 txt、
        /// 复制到剪贴板，再把软件带到前台，主人直接往里粘。
        /// </summary>
        private static string HandOffToApp(TtsSettings tts, string text)
        {
            try
            {
                string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DomeOS-Speak.txt");
                System.IO.File.WriteAllText(file, text, new UTF8Encoding(false));

                CopyToClipboard(text);

                string template = string.IsNullOrWhiteSpace(tts.VocaloidAppArgs)
                    ? "{file}"
                    : tts.VocaloidAppArgs;

                string args = template
                    .Replace("{file}", "\"" + file + "\"")
                    .Replace("{text}", text);

                ProcessStartInfo info = new ProcessStartInfo(tts.VocaloidApp.Trim())
                {
                    UseShellExecute = true,
                    Arguments = args,
                };

                Process.Start(info);

                LastNotice = "已把这段文字复制到剪贴板，并交给 "
                             + System.IO.Path.GetFileNameWithoutExtension(tts.VocaloidApp.Trim())
                             + "（文本也存到了 " + file + "）。";
                return string.Empty;
            }
            catch (Exception ex)
            {
                return "调用配音软件失败：" + ex.Message;
            }
        }

        private static void CopyToClipboard(string text)
        {
            try
            {
                Windows.ApplicationModel.DataTransfer.DataPackage package =
                    new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            catch
            {
                // 剪贴板被别的程序占着就跳过，临时 txt 还在
            }
        }

        /// <summary>
        /// 虚拟歌姬的嗓子。本机 SAPI 声库配上 SSML 的音高偏移，把正常朗读往「二次元歌姬」
        /// 那个方向抬上去；如果主人装了真正的歌姬声库（例如洛天依、初音未来的 SAPI 版本），
        /// 声库下拉里会直接列出来，选中之后放的就是那个嗓子本身。
        ///
        /// 音高用百分比而不是绝对赫兹：不同声库的基频差得远，相对值才不会把一声库抬破音。
        /// </summary>
        private static Task<string> SpeakVocaloidAsync(TtsSettings tts, string text, CancellationTokenSource cancel)
        {
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();
            System.Speech.Synthesis.SpeechSynthesizer synth = new System.Speech.Synthesis.SpeechSynthesizer();

            try
            {
                (int pitch, int rate) = VocaloidTuning(tts);

                synth.Rate = Math.Clamp(rate, -10, 10);
                synth.Volume = 100;

                SelectSapiVoice(synth, tts.VocaloidVoice);

                string ssml =
                    "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"zh-CN\">"
                    + "<prosody pitch=\"" + (pitch >= 0 ? "+" : string.Empty) + pitch + "%\">"
                    + SecurityEscape(text)
                    + "</prosody></speak>";

                synth.SpeakCompleted += (object? sender, System.Speech.Synthesis.SpeakCompletedEventArgs args) =>
                {
                    try
                    {
                        synth.Dispose();
                    }
                    catch
                    {
                        // 关不干净也不影响
                    }

                    done.TrySetResult(string.Empty);
                };

                cancel.Token.Register(() =>
                {
                    try
                    {
                        synth.SpeakAsyncCancelAll();
                        synth.Dispose();
                    }
                    catch
                    {
                        // 已经停了
                    }

                    done.TrySetResult(string.Empty);
                });

                synth.SpeakSsmlAsync(ssml);
            }
            catch (Exception ex)
            {
                try
                {
                    synth.Dispose();
                }
                catch
                {
                    // 忽略
                }

                return Task.FromResult(ex.Message);
            }

            return done.Task;
        }

        /// <summary>按名字挑一个 SAPI 声库；名字对不上就留系统默认。</summary>
        private static void SelectSapiVoice(System.Speech.Synthesis.SpeechSynthesizer synth, string wanted)
        {
            string name = (wanted ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return;
            }

            try
            {
                foreach (System.Speech.Synthesis.InstalledVoice installed in synth.GetInstalledVoices())
                {
                    string candidate = installed.VoiceInfo.Name;
                    if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase)
                        || candidate.Contains(name, StringComparison.OrdinalIgnoreCase)
                        || name.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        synth.SelectVoice(candidate);
                        return;
                    }
                }
            }
            catch
            {
                // 挑不动就用默认的
            }
        }

        /// <summary>SSML 里的正文要把 &amp; &lt; &gt; 这些转义掉，不然整段念不出来。</summary>
        private static string SecurityEscape(string text)
        {
            return (text ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        // ------------------------------------------------------------------ playback

        private static Task<string> PlayAsync(IRandomAccessStream stream, string contentType,
                                              CancellationTokenSource cancel)
        {
            TaskCompletionSource<string> done = new TaskCompletionSource<string>();

            MediaPlayer player = new MediaPlayer();
            player.Source = MediaSource.CreateFromStream(stream, contentType);

            player.MediaEnded += (MediaPlayer sender, object args) => Complete(done, string.Empty);
            player.MediaFailed += (MediaPlayer sender, MediaPlayerFailedEventArgs args) => Complete(done, args.ErrorMessage);

            // cancelling must not leave the finished handler dangling on a disposed player
            cancel.Token.Register(() =>
            {
                Complete(done, "已停止");
                try
                {
                    player.Dispose();
                }
                catch
                {
                    // already gone
                }
            });

            MediaPlayer? previous = _player;
            _player = player;
            if (previous != null)
            {
                try
                {
                    previous.Dispose();
                }
                catch
                {
                    // already gone
                }
            }

            SetSpeaking(true);
            player.Play();

            return done.Task;
        }

        private static void Complete(TaskCompletionSource<string> done, string result)
        {
            if (!done.TrySetResult(result))
            {
                return;
            }

            SetSpeaking(false);
        }

        private static void RememberQueue()
        {
            DispatcherQueue? current = DispatcherQueue.GetForCurrentThread();
            if (current != null)
            {
                _uiQueue = current;
            }
        }

        private static void SetSpeaking(bool value)
        {
            IsSpeaking = value;

            DispatcherQueue? queue = _uiQueue;
            if (queue != null)
            {
                queue.TryEnqueue(() => StateChanged?.Invoke(null, EventArgs.Empty));
            }
        }

        // ------------------------------------------------------------------ MiMo request shape

        private sealed class MimoRequest
        {
            public string Model { get; set; } = string.Empty;

            public MimoMessage[] Messages { get; set; } = Array.Empty<MimoMessage>();

            public MimoAudio Audio { get; set; } = new MimoAudio();
        }

        private sealed class MimoMessage
        {
            public string Role { get; set; } = string.Empty;

            public string Content { get; set; } = string.Empty;
        }

        private sealed class MimoAudio
        {
            public string Format { get; set; } = "wav";

            public string Voice { get; set; } = "mimo_default";
        }
    }
}
