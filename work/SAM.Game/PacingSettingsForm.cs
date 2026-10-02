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
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SAM.Game.Stats;

namespace SAM.Game
{
    /// <summary>
    /// Configuration for "按 Steam 全球解锁率顺序、带随机空闲间隔地依次解锁成就".
    /// </summary>
    internal class PacingSettingsForm : Form
    {
        private readonly List<AchievementInfo> _Achievements;

        private ComboBox _OrderBox;
        private NumericUpDown _MinimumPercentBox;
        private NumericUpDown _MaximumPercentBox;
        private ComboBox _IntervalModeBox;
        private NumericUpDown _MinimumIntervalBox;
        private NumericUpDown _MaximumIntervalBox;
        private NumericUpDown _InitialDelayBox;
        private CheckBox _SkipUnlockedCheckBox;
        private Label _SummaryLabel;
        private ListView _PreviewList;
        private Button _StartButton;
        private Button _CancelButton;

        public PacingSettings Settings { get; private set; }

        public PacingSettingsForm(PacingSettings initial, IEnumerable<AchievementInfo> achievements)
        {
            this._Achievements = achievements.ToList();
            this.Settings = initial ?? new PacingSettings();

            this.BuildUi();
            this.LoadSettings(this.Settings);
            this.UpdatePreview();
        }

        private void BuildUi()
        {
            this.Text = "按解锁率节奏解锁";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(640, 470);
            this.ShowInTaskbar = false;

            var orderLabel = new Label()
            {
                Text = "解锁顺序：",
                Location = new Point(14, 17),
                Size = new Size(110, 20),
            };
            this._OrderBox = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(130, 14),
                Size = new Size(280, 22),
            };
            this._OrderBox.Items.AddRange(new object[]
            {
                "全球解锁率 从高到低（先拿大众成就）",
                "全球解锁率 从低到高（先拿稀有成就）",
                "完全随机顺序",
            });

            var percentLabel = new Label()
            {
                Text = "只处理解锁率区间：",
                Location = new Point(14, 49),
                Size = new Size(110, 20),
            };
            this._MinimumPercentBox = new NumericUpDown()
            {
                Location = new Point(130, 46),
                Size = new Size(70, 22),
                DecimalPlaces = 2,
                Minimum = 0,
                Maximum = 100,
                Increment = 0.5M,
            };
            var percentTo = new Label()
            {
                Text = "～",
                Location = new Point(206, 49),
                Size = new Size(18, 20),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            this._MaximumPercentBox = new NumericUpDown()
            {
                Location = new Point(228, 46),
                Size = new Size(70, 22),
                DecimalPlaces = 2,
                Minimum = 0,
                Maximum = 100,
                Increment = 0.5M,
            };
            var percentUnit = new Label()
            {
                Text = "%（100 = 不限制）",
                Location = new Point(304, 49),
                Size = new Size(160, 20),
            };

            var modeLabel = new Label()
            {
                Text = "间隔节奏：",
                Location = new Point(14, 81),
                Size = new Size(110, 20),
            };
            this._IntervalModeBox = new ComboBox()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(130, 78),
                Size = new Size(280, 22),
            };
            this._IntervalModeBox.Items.AddRange(new object[]
            {
                "均匀随机（min～max 之间随机）",
                "真人节奏（多为短间隔，偶尔长时间挂机）",
            });

            var intervalLabel = new Label()
            {
                Text = "两个成就之间空闲：",
                Location = new Point(14, 113),
                Size = new Size(110, 20),
            };
            this._MinimumIntervalBox = new NumericUpDown()
            {
                Location = new Point(130, 110),
                Size = new Size(70, 22),
                DecimalPlaces = 0,
                Minimum = 0,
                Maximum = 86400,
                Increment = 5,
            };
            var intervalTo = new Label()
            {
                Text = "～",
                Location = new Point(206, 113),
                Size = new Size(18, 20),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            this._MaximumIntervalBox = new NumericUpDown()
            {
                Location = new Point(228, 110),
                Size = new Size(70, 22),
                DecimalPlaces = 0,
                Minimum = 0,
                Maximum = 86400,
                Increment = 5,
            };
            var intervalUnit = new Label()
            {
                Text = "秒（随机取值）",
                Location = new Point(304, 113),
                Size = new Size(160, 20),
            };

            var delayLabel = new Label()
            {
                Text = "开始前先等：",
                Location = new Point(14, 145),
                Size = new Size(110, 20),
            };
            this._InitialDelayBox = new NumericUpDown()
            {
                Location = new Point(130, 142),
                Size = new Size(70, 22),
                DecimalPlaces = 0,
                Minimum = 0,
                Maximum = 86400,
                Increment = 5,
            };
            var delayUnit = new Label()
            {
                Text = "秒（0 = 立即开始）",
                Location = new Point(206, 145),
                Size = new Size(180, 20),
            };

            this._SkipUnlockedCheckBox = new CheckBox()
            {
                Text = "跳过已经解锁的成就（推荐）",
                Location = new Point(130, 172),
                Size = new Size(300, 22),
                Checked = true,
            };

            this._SummaryLabel = new Label()
            {
                Location = new Point(14, 226),
                Size = new Size(612, 34),
                ForeColor = Color.FromArgb(192, 0, 0),
            };

            this._PreviewList = new ListView()
            {
                Location = new Point(14, 264),
                Size = new Size(612, 158),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            this._PreviewList.Columns.Add("#", 36);
            this._PreviewList.Columns.Add("成就", 260);
            this._PreviewList.Columns.Add("全球解锁率", 90, HorizontalAlignment.Right);
            this._PreviewList.Columns.Add("距开始", 90, HorizontalAlignment.Right);
            this._PreviewList.Columns.Add("距上一个", 90, HorizontalAlignment.Right);

            this._StartButton = new Button()
            {
                Text = "开始按节奏解锁",
                Location = new Point(444, 430),
                Size = new Size(120, 28),
            };
            this._CancelButton = new Button()
            {
                Text = "取消",
                Location = new Point(570, 430),
                Size = new Size(56, 28),
                DialogResult = DialogResult.Cancel,
            };

            this._StartButton.Click += (sender, args) =>
            {
                if (this.ValidateSettings() == false)
                {
                    return;
                }

                this.Settings = this.ReadSettings();
                // 显式声明"确认"，否则 ShowDialog 的返回值会是 Cancel，
                // 调用方会把它当成"用户取消了"
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            this.Controls.AddRange(new Control[]
            {
                orderLabel, this._OrderBox,
                percentLabel, this._MinimumPercentBox, percentTo, this._MaximumPercentBox, percentUnit,
                modeLabel, this._IntervalModeBox,
                intervalLabel, this._MinimumIntervalBox, intervalTo, this._MaximumIntervalBox, intervalUnit,
                delayLabel, this._InitialDelayBox, delayUnit,
                this._SkipUnlockedCheckBox,
                this._SummaryLabel, this._PreviewList,
                this._StartButton, this._CancelButton,
            });

            this.AcceptButton = this._StartButton;
            this.CancelButton = this._CancelButton;

            void update(object sender, EventArgs args) => this.UpdatePreview();
            this._OrderBox.SelectedIndexChanged += update;
            this._IntervalModeBox.SelectedIndexChanged += update;
            this._MinimumPercentBox.ValueChanged += update;
            this._MaximumPercentBox.ValueChanged += update;
            this._MinimumIntervalBox.ValueChanged += update;
            this._MaximumIntervalBox.ValueChanged += update;
            this._InitialDelayBox.ValueChanged += update;
            this._SkipUnlockedCheckBox.CheckedChanged += update;
        }

        private void LoadSettings(PacingSettings settings)
        {
            this._OrderBox.SelectedIndex = (int)settings.Order;
            this._IntervalModeBox.SelectedIndex = (int)settings.IntervalMode;
            this._MinimumPercentBox.Value = Clamp((decimal)settings.MinimumPercent, 0, 100);
            this._MaximumPercentBox.Value = Clamp((decimal)settings.MaximumPercent, 0, 100);
            this._MinimumIntervalBox.Value = Clamp((decimal)settings.MinimumIntervalSeconds, 0, 86400);
            this._MaximumIntervalBox.Value = Clamp((decimal)settings.MaximumIntervalSeconds, 0, 86400);
            this._InitialDelayBox.Value = Clamp((decimal)settings.InitialDelaySeconds, 0, 86400);
            this._SkipUnlockedCheckBox.Checked = settings.SkipAlreadyUnlocked;
        }

        private static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }

        private PacingSettings ReadSettings()
        {
            return new PacingSettings()
            {
                Order = (PacingOrder)this._OrderBox.SelectedIndex,
                IntervalMode = (PacingIntervalMode)this._IntervalModeBox.SelectedIndex,
                MinimumPercent = (double)this._MinimumPercentBox.Value,
                MaximumPercent = (double)this._MaximumPercentBox.Value,
                MinimumIntervalSeconds = (double)this._MinimumIntervalBox.Value,
                MaximumIntervalSeconds = (double)this._MaximumIntervalBox.Value,
                InitialDelaySeconds = (double)this._InitialDelayBox.Value,
                SkipAlreadyUnlocked = this._SkipUnlockedCheckBox.Checked,
            };
        }

        private bool ValidateSettings()
        {
            if (this._MinimumPercentBox.Value > this._MaximumPercentBox.Value)
            {
                MessageBox.Show(
                    this,
                    "解锁率区间的下限不能大于上限。",
                    "参数不对",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            if (this._MinimumIntervalBox.Value > this._MaximumIntervalBox.Value)
            {
                MessageBox.Show(
                    this,
                    "空闲间隔的下限不能大于上限。",
                    "参数不对",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            if (this._MaximumIntervalBox.Value + this._MinimumIntervalBox.Value == 0)
            {
                // 必须用 "!= Yes"：点 X 关闭返回 Cancel，用 "== No" 会当成"确认继续"
                if (MessageBox.Show(
                    this,
                    "间隔设置为 0 秒，所有成就会在瞬间解锁（完全不像真人在玩）。\n\n确定要继续吗？",
                    "确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return false;
                }
            }

            return true;
        }

        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalDays >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 天 {1} 小时 {2} 分",
                    (int)span.TotalDays,
                    span.Hours,
                    span.Minutes);
            }

            if (span.TotalHours >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 小时 {1} 分",
                    (int)span.TotalHours,
                    span.Minutes);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0} 分 {1} 秒", span.Minutes, span.Seconds);
        }

        private void UpdatePreview()
        {
            if (this._OrderBox.SelectedIndex < 0 || this._IntervalModeBox.SelectedIndex < 0)
            {
                return;
            }

            var settings = this.ReadSettings();
            var session = new PacingSession(null, settings);
            session.Build(this._Achievements);

            this._PreviewList.BeginUpdate();
            this._PreviewList.Items.Clear();

            var steps = session.Steps;
            double previousOffset = 0.0;
            int shown = 0;
            foreach (var step in steps)
            {
                if (shown >= 8)
                {
                    break;
                }

                var item = new ListViewItem((shown + 1).ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(step.Achievement.Name ?? step.Achievement.Id);
                item.SubItems.Add(step.Achievement.GlobalPercent.HasValue == true
                    ? step.Achievement.GlobalPercent.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%"
                    : "未知");
                item.SubItems.Add(FormatDuration(step.OffsetSeconds));

                double gap = shown == 0 ? step.OffsetSeconds : step.OffsetSeconds - previousOffset;
                item.SubItems.Add(FormatDuration(gap));

                this._PreviewList.Items.Add(item);
                previousOffset = step.OffsetSeconds;
                shown++;
            }

            this._PreviewList.EndUpdate();

            if (steps.Count == 0)
            {
                this._SummaryLabel.Text = "按当前条件没有可解锁的成就，请放宽解锁率区间，或先刷新数据。";
                return;
            }

            double total = steps[steps.Count - 1].OffsetSeconds;

            int unknownPercent = steps.Count(s => s.Achievement.GlobalPercent.HasValue == false);
            int protectedCount = this._Achievements.Count(a => (a.Permission & 3) != 0);
            int missing = this._Achievements.Count(a =>
                a.GlobalPercent.HasValue == false && (a.Permission & 3) == 0);

            string message = string.Format(
                CultureInfo.CurrentCulture,
                "将解锁 {0} 个成就，预计总耗时 {1}（最后一个成就之后不再等待）。",
                steps.Count,
                FormatDuration(total));

            if (missing > 0 && settings.Order != PacingOrder.Random)
            {
                message += string.Format(
                    CultureInfo.CurrentCulture,
                    " 其中 {0} 个没有全球解锁率数据，会排在最后。",
                    unknownPercent);
            }

            if (protectedCount > 0)
            {
                message += string.Format(
                    CultureInfo.CurrentCulture,
                    " 另有 {0} 个受保护成就会被跳过。",
                    protectedCount);
            }

            this._SummaryLabel.Text = message;
        }
    }
}
