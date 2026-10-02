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
using System.Drawing;
using System.Windows.Forms;
using SAM.I18n;

namespace SAM.Game
{
    /// <summary>
    /// 启动时的语言选择窗口。
    /// 会记住选择（sam-mod-config.txt），下次直接使用、不再询问。
    /// </summary>
    internal class LanguagePickerForm : Form
    {
        private readonly ComboBox _LanguageBox;

        /// <summary>用户选定的语言代码。</summary>
        public string SelectedLanguage { get; private set; }

        public LanguagePickerForm(bool allowCancel)
            : this(allowCancel, null, null)
        {
        }

        /// <param name="detectedCode">自动探测到的语言代码，作为默认选中项。</param>
        /// <param name="detectedFrom">探测来源（"steam" / "system"），仅用于提示文案。</param>
        public LanguagePickerForm(bool allowCancel, string detectedCode, string detectedFrom)
        {
            this.Text = "界面语言 / Language";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(392, 186);
            this.ShowInTaskbar = true;

            var title = new Label()
            {
                Text = "请选择界面语言 / Choose language",
                Location = new Point(18, 14),
                Size = new Size(356, 22),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            };

            var sourceHint = detectedFrom == "steam"
                ? "已按你的 Steam 客户端语言预选。"
                : (detectedFrom == "system" ? "已按系统语言预选。" : "");

            var hint = new Label()
            {
                Text = "成就的名称与描述跟随 Steam 客户端语言，不受这里影响。\n" +
                       "Achievement names and descriptions follow the Steam client language.\n" +
                       sourceHint,
                Location = new Point(18, 40),
                Size = new Size(356, 56),
                ForeColor = Color.FromArgb(96, 96, 96),
            };

            this._LanguageBox = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(18, 102),
                Size = new Size(356, 24),
            };
            foreach (var language in Localization.Available)
            {
                this._LanguageBox.Items.Add(language);
            }

            this._LanguageBox.SelectedIndex = 0;

            var okButton = new Button()
            {
                Text = "确定 / OK",
                Location = new Point(208, 142),
                Size = new Size(78, 28),
                DialogResult = DialogResult.OK,
            };
            var cancelButton = new Button()
            {
                Text = "取消 / Cancel",
                Location = new Point(296, 142),
                Size = new Size(78, 28),
                DialogResult = DialogResult.Cancel,
                Visible = allowCancel,
            };

            this.Controls.AddRange(new Control[] { title, hint, this._LanguageBox, okButton, cancelButton });
            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            okButton.Click += (sender, args) =>
            {
                var selected = this._LanguageBox.SelectedItem as LanguageInfo;
                this.SelectedLanguage = selected != null ? selected.Code : Localization.DefaultLanguage;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            // 默认选中：探测结果 → 其次是当前语言
            var preferred = string.IsNullOrEmpty(detectedCode) == true ? Localization.Current : detectedCode;
            for (var i = 0; i < this._LanguageBox.Items.Count; i++)
            {
                var language = this._LanguageBox.Items[i] as LanguageInfo;
                if (language != null && language.Code == preferred)
                {
                    this._LanguageBox.SelectedIndex = i;
                    break;
                }
            }
        }

        /// <summary>
        /// 询问用户选择语言。返回 null 表示用户取消。
        /// </summary>
        public static string Ask(bool allowCancel)
        {
            return Ask(allowCancel, null, null);
        }

        /// <summary>
        /// 询问用户选择语言，并预选自动探测到的语言。
        /// </summary>
        public static string Ask(bool allowCancel, string detectedCode, string detectedFrom)
        {
            using (var form = new LanguagePickerForm(allowCancel, detectedCode, detectedFrom))
            {
                if (form.ShowDialog() != DialogResult.OK)
                {
                    return null;
                }

                return form.SelectedLanguage;
            }
        }
    }
}
