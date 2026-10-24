using System;
using Microsoft.Win32;

namespace DomeOSAgentDev
{
    /// <summary>
    /// 应用对外的名字。机器上还装着另一个「DomeOS AI」（Electron 版）时，
    /// 名字后面加「（WinUI 3）」，免得两个程序在任务栏、开始菜单和通知区里重名。
    /// </summary>
    public static class AppInfo
    {
        public const string BaseName = "DomeOS AI";
        public const string WinUiSuffix = "（WinUI 3）";

        /// <summary>本程序自己的卸载项键名，找别的 DomeOS AI 时要先把自己摘出去。</summary>
        private const string OurUninstallKey = "d41f6e2a-9c73-4b18-8f5d-2a6c4e90317b";

        private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        private static string? _displayName;

        /// <summary>「DomeOS AI」，装了另一个版本时是「DomeOS AI（WinUI 3）」。</summary>
        public static string DisplayName => _displayName ??= DetectOtherInstall() ? BaseName + WinUiSuffix : BaseName;

        /// <summary>卸装列表里有没有另一个名字正好是 DomeOS AI 的程序。</summary>
        private static bool DetectOtherInstall()
        {
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    try
                    {
                        using RegistryKey? root = RegistryKey.OpenBaseKey(hive, view);
                        using RegistryKey? uninstall = root?.OpenSubKey(UninstallPath);
                        if (uninstall == null)
                        {
                            continue;
                        }

                        foreach (string name in uninstall.GetSubKeyNames())
                        {
                            if (string.Equals(name, OurUninstallKey, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            using RegistryKey? entry = uninstall.OpenSubKey(name);
                            if (entry?.GetValue("DisplayName") is string display
                                && string.Equals(display.Trim(), BaseName, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 这一路注册表读不到就当它没有装
                    }
                }
            }

            return false;
        }
    }
}
