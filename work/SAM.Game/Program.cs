/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Diagnostics;
using System.Windows.Forms;

using SAM.I18n;

namespace SAM.Game
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            long appId;

            // 魔改：这两个设置必须在**创建任何控件之前**调用，
            // 否则会抛 InvalidOperationException（语言选择窗口本身也是控件）。
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 魔改：先确定界面语言
            //   --lang <code>   直接指定（zh-Hans / en / zh-Hant）并记住，然后退出
            //   没有 --lang 且从未选择过语言时，弹一次语言选择窗口
            if (TryHandleLanguageArgument(args) == true)
            {
                return;
            }

            if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal) == true)
            {
                Process.Start("SAM.Picker.exe");
                return;
            }

            if (long.TryParse(args[0], out appId) == false)
            {
                MessageBox.Show(
                    Localization.T("Could not parse application ID from command line argument."),
                    Localization.T("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (API.Steam.GetInstallPath() == Application.StartupPath)
            {
                MessageBox.Show(
                    Localization.T("This tool declines to being run from the Steam directory."),
                    Localization.T("Error"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            using (API.Client client = new())
            {
                try
                {
                    client.Initialize(appId);
                }
                catch (API.ClientInitializeException e)
                {
                    if (e.Failure == API.ClientInitializeFailure.ConnectToGlobalUser)
                    {
                        MessageBox.Show(
                            Localization.T("Steam is not running. Please start Steam then run this tool again.\n\n") +
                            Localization.T("If you have the game through Family Share, the game may be locked due to\nthe Family Share account actively playing a game.\n\n") +
                            "(" + e.Message + ")",
                            Localization.T("Error"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else if (string.IsNullOrEmpty(e.Message) == false)
                    {
                        MessageBox.Show(
                            Localization.T("Steam is not running. Please start Steam then run this tool again.\n\n") +
                            "(" + e.Message + ")",
                            Localization.T("Error"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else
                    {
                        MessageBox.Show(
                            Localization.T("Steam is not running. Please start Steam then run this tool again."),
                            Localization.T("Error"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    return;
                }
                catch (DllNotFoundException)
                {
                    MessageBox.Show(
                        Localization.T("You've caused an exceptional error!"),
                        Localization.T("Error"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Application.Run(new Manager(appId, client));
            }
        }

        /// <summary>
        /// 处理 --lang 参数；没有参数且从未保存过语言时，弹一次语言选择窗口。
        /// 返回 true 表示命令行已经把语言处理完了，调用方应当直接退出。
        /// </summary>
        private static bool TryHandleLanguageArgument(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--lang", StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                var code = i + 1 < args.Length ? args[i + 1] : null;
                if (string.IsNullOrEmpty(code) == true)
                {
                    // 没给语言代码，就弹选择窗口
                    var picked = LanguagePickerForm.Ask(true);
                    if (string.IsNullOrEmpty(picked) == true)
                    {
                        return true;
                    }

                    Localization.SetLanguage(picked);
                    Localization.SaveLanguage(Localization.Current);
                    return true;
                }

                Localization.SetLanguage(code);
                Localization.SaveLanguage(Localization.Current);
                return true;
            }

            var saved = Localization.LoadSavedLanguage();
            if (string.IsNullOrEmpty(saved) == true)
            {
                // 首次运行：先自动探测（Steam 客户端语言 → 系统语言），预选好再让用户确认
                var detected = Localization.DetectLanguage();
                var from = Localization.DescribeDetection();
                Localization.SetLanguage(detected);

                var picked = LanguagePickerForm.Ask(false, detected, from);
                if (string.IsNullOrEmpty(picked) == false)
                {
                    Localization.SetLanguage(picked);
                    Localization.SaveLanguage(Localization.Current);
                }
            }
            else
            {
                Localization.SetLanguage(saved);
            }

            return false;
        }
    }
}
