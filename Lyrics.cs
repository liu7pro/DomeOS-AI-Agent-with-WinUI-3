using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DomeOSAgentDev
{
    /// <summary>一首彩蛋歌的歌词；Times 为空表示还没校准，只能按总时长均分。</summary>
    internal sealed class LyricTrack
    {
        public string[] Lines { get; init; } = Array.Empty<string>();
        public double[]? Times { get; init; }
    }

    /// <summary>
    /// 彩蛋那几首歌的歌词。先找主人校准过的 LRC（存在配置目录里），
    /// 再找 Assets\lyrics 下随程序带的文本；文本里夹带的网页小广告顺手清掉。
    /// </summary>
    internal static class Lyrics
    {
        private static readonly Regex Stamp =
            new Regex(@"^\s*\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]\s*(.*)$");

        private static readonly Regex Tag =
            new Regex(@"^\s*\[[a-zA-Z]{1,4}:[^\]]*\]\s*$");

        public static LyricTrack Load(string fileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);

            // 校准过的时间轴优先
            LyricTrack? timed = TryLoadLrc(Path.Combine(SettingsStore.DataDir, "lyrics", baseName + ".lrc"));
            if (timed != null)
            {
                return timed;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "lyrics", fileName);
            timed = TryLoadLrc(path);
            if (timed != null)
            {
                return timed;
            }

            string[] lines = LoadPlain(path);
            return new LyricTrack { Lines = lines, Times = null };
        }

        /// <summary>把主人跟着听时记下的时间点写成标准 LRC，下次播放就按它走。</summary>
        public static bool SaveLrc(string fileName, string[] lines, double[] times)
        {
            try
            {
                string dir = Path.Combine(SettingsStore.DataDir, "lyrics");
                Directory.CreateDirectory(dir);

                StringBuilder text = new StringBuilder();
                int count = Math.Min(lines.Length, times.Length);
                for (int i = 0; i < count; i++)
                {
                    double at = Math.Max(0, times[i]);
                    int minute = (int)(at / 60);
                    double second = at - minute * 60;
                    text.Append('[')
                        .Append(minute.ToString("00", CultureInfo.InvariantCulture))
                        .Append(':')
                        .Append(second.ToString("00.00", CultureInfo.InvariantCulture))
                        .Append(']')
                        .Append(lines[i])
                        .AppendLine();
                }

                string path = Path.Combine(dir, Path.GetFileNameWithoutExtension(fileName) + ".lrc");
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>有校准时间就按时间点找最后一句；没有就按总时长均分。</summary>
        public static int IndexAt(double seconds, double[]? times, TimeSpan duration, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (times != null && times.Length == count)
            {
                int index = 0;
                for (int i = 0; i < times.Length; i++)
                {
                    if (times[i] <= seconds + 0.05)
                    {
                        index = i;
                    }
                    else
                    {
                        break;
                    }
                }

                return index;
            }

            double total = Math.Max(1, duration.TotalSeconds);
            double progress = Math.Clamp(seconds / total, 0, 0.9999);
            return Math.Min(count - 1, (int)(progress * count));
        }

        private static LyricTrack? TryLoadLrc(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                List<string> lines = new List<string>();
                List<double> times = new List<double>();

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = Clean(raw);
                    if (line.Length == 0 || Tag.IsMatch(line))
                    {
                        continue;
                    }

                    Match match = Stamp.Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }

                    double at = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                                + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

                    if (match.Groups[3].Success)
                    {
                        string fraction = match.Groups[3].Value;
                        at += double.Parse("0." + fraction, CultureInfo.InvariantCulture);
                    }

                    string text = match.Groups[4].Value.Trim();
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    lines.Add(text);
                    times.Add(at);
                }

                if (lines.Count == 0)
                {
                    return null;
                }

                return new LyricTrack { Lines = lines.ToArray(), Times = times.ToArray() };
            }
            catch
            {
                return null;
            }
        }

        private static string[] LoadPlain(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return Array.Empty<string>();
                }

                List<string> lines = new List<string>();
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = Clean(raw);
                    if (line.Length > 0 && !Stamp.IsMatch(line) && !Tag.IsMatch(line))
                    {
                        lines.Add(line);
                    }
                }

                return lines.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string Clean(string raw)
        {
            return (raw ?? string.Empty)
                .Trim()
                .Replace("查找吉他谱", string.Empty)
                .Replace("探索韩流音乐", string.Empty)
                .Replace("聆听灵魂乐", string.Empty)
                .Trim();
        }
    }
}
