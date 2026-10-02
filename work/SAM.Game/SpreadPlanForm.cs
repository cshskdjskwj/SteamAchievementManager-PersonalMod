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
 *    in a product, an acknowledgment in the product documentation would be
 *    appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SAM.Game.Stats;

using SAM.I18n;

namespace SAM.Game
{
    /// <summary>
    /// 「长时间铺开」的配置窗口。
    /// 设定起止日期、每日数量、每天的时间窗口，然后预览整个计划。
    /// </summary>
    internal class SpreadPlanForm : Form
    {
        private readonly System.Collections.Generic.List<AchievementInfo> _Achievements;

        private DateTimePicker _StartPicker;
        private DateTimePicker _EndPicker;
        private NumericUpDown _MinimumPerDayBox;
        private NumericUpDown _MaximumPerDayBox;
        private NumericUpDown _WindowStartBox;
        private NumericUpDown _WindowEndBox;
        private CheckBox _CatchUpCheckBox;
        private Label _SummaryLabel;
        private ListView _PreviewList;
        private Button _StartButton;
        private Button _CancelButton;

        public SpreadConfig Config { get; private set; }

        /// <summary>用户在已有进度的情况下选择了重新开始。</summary>
        public bool ResetExistingPlan { get; private set; }

        public SpreadPlanForm(
            SpreadConfig initial,
            System.Collections.Generic.List<AchievementInfo> achievements,
            string existingProgressText)
        {
            this._Achievements = achievements;
            this.Config = initial ?? new SpreadConfig();

            this.BuildUi();
            Localization.ApplyTo(this);
            this.LoadConfig(this.Config);

            if (string.IsNullOrEmpty(existingProgressText) == false)
            {
                var answer = MessageBox.Show(
                    this,
                    existingProgressText + "\n\n是否继续使用已有进度？（选「否」会重新排一份新计划）",
                    "发现已有计划",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                // 只有明确点「否」才重排新计划；
                // 点 X 关闭这个提示返回 Cancel，应当按"继续用已有进度"处理
                if (answer == DialogResult.No)
                {
                    this.ResetExistingPlan = true;
                }
            }

            this.UpdatePreview();
        }

        private void BuildUi()
        {
            this.Text = "长时间铺开（按天解锁）";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(660, 520);
            this.ShowInTaskbar = false;

            var note = new Label()
            {
                Text = "让成就的解锁时间自然地分散到很多天里。\n" +
                       "注意：解锁时间由 Steam 服务端盖章，所以必须真的在这些天里运行本程序，\n" +
                       "程序会按计划到点解锁，中途关掉也没关系，下次打开会自动接着跑。",
                Location = new Point(14, 12),
                Size = new Size(632, 52),
            };

            var startLabel = new Label()
            {
                Text = "开始日期：",
                Location = new Point(14, 74),
                Size = new Size(110, 20),
            };
            this._StartPicker = new DateTimePicker()
            {
                Location = new Point(130, 71),
                Size = new Size(130, 22),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd",
            };

            var endLabel = new Label()
            {
                Text = "结束日期：",
                Location = new Point(276, 74),
                Size = new Size(80, 20),
            };
            this._EndPicker = new DateTimePicker()
            {
                Location = new Point(358, 71),
                Size = new Size(130, 22),
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd",
            };

            var quotaLabel = new Label()
            {
                Text = "每天解锁：",
                Location = new Point(14, 106),
                Size = new Size(110, 20),
            };
            this._MinimumPerDayBox = new NumericUpDown()
            {
                Location = new Point(130, 103),
                Size = new Size(60, 22),
                Minimum = 1,
                Maximum = 200,
                Value = 3,
            };
            var quotaTo = new Label()
            {
                Text = "～",
                Location = new Point(196, 106),
                Size = new Size(18, 20),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            this._MaximumPerDayBox = new NumericUpDown()
            {
                Location = new Point(218, 103),
                Size = new Size(60, 22),
                Minimum = 1,
                Maximum = 200,
                Value = 8,
            };
            var quotaUnit = new Label()
            {
                Text = "个（每天在这个区间里随机）",
                Location = new Point(284, 106),
                Size = new Size(240, 20),
            };

            var windowLabel = new Label()
            {
                Text = "每天的时间窗口：",
                Location = new Point(14, 138),
                Size = new Size(110, 20),
            };
            this._WindowStartBox = new NumericUpDown()
            {
                Location = new Point(130, 135),
                Size = new Size(60, 22),
                Minimum = 0,
                Maximum = 23,
                Value = 19,
            };
            var windowTo = new Label()
            {
                Text = "点 ～",
                Location = new Point(196, 138),
                Size = new Size(40, 20),
            };
            this._WindowEndBox = new NumericUpDown()
            {
                Location = new Point(240, 135),
                Size = new Size(60, 22),
                Minimum = 1,
                Maximum = 24,
                Value = 23,
            };
            var windowUnit = new Label()
            {
                Text = "点（成就只会落在这个时间段内）",
                Location = new Point(306, 138),
                Size = new Size(260, 20),
            };

            this._CatchUpCheckBox = new CheckBox()
            {
                Text = "错过时间窗口时立刻补做（不勾就是跳过那天，第二天继续）",
                Location = new Point(130, 164),
                Size = new Size(460, 22),
                Checked = true,
            };

            var explain = new Label()
            {
                Text = "提示：想尽量像真人，建议窗口设在你平时真会玩游戏的时段，每天 3~8 个。",
                Location = new Point(130, 188),
                Size = new Size(480, 20),
                ForeColor = Color.FromArgb(96, 96, 96),
            };

            this._SummaryLabel = new Label()
            {
                Location = new Point(14, 214),
                Size = new Size(632, 40),
                ForeColor = Color.FromArgb(192, 0, 0),
            };

            this._PreviewList = new ListView()
            {
                Location = new Point(14, 258),
                Size = new Size(632, 200),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            this._PreviewList.Columns.Add("日期", 90);
            this._PreviewList.Columns.Add("当天数量", 70, HorizontalAlignment.Right);
            this._PreviewList.Columns.Add("第一个时刻", 90);
            this._PreviewList.Columns.Add("最后一个时刻", 90);
            this._PreviewList.Columns.Add("当天累计", 80, HorizontalAlignment.Right);
            this._PreviewList.Columns.Add("界面显示的游戏时间跨度", 180);

            this._StartButton = new Button()
            {
                Text = "开始铺开",
                Location = new Point(464, 470),
                Size = new Size(100, 28),
            };
            this._CancelButton = new Button()
            {
                Text = "取消",
                Location = new Point(572, 470),
                Size = new Size(74, 28),
                DialogResult = DialogResult.Cancel,
            };

            this._StartButton.Click += (sender, args) =>
            {
                if (this.ValidateConfig() == false)
                {
                    return;
                }

                this.Config = this.ReadConfig();
                // 显式声明"确认"，否则 ShowDialog 的返回值会是 Cancel
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            this.Controls.AddRange(new Control[]
            {
                note,
                startLabel, this._StartPicker,
                endLabel, this._EndPicker,
                quotaLabel, this._MinimumPerDayBox, quotaTo, this._MaximumPerDayBox, quotaUnit,
                windowLabel, this._WindowStartBox, windowTo, this._WindowEndBox, windowUnit,
                this._CatchUpCheckBox, explain,
                this._SummaryLabel, this._PreviewList,
                this._StartButton, this._CancelButton,
            });

            this.AcceptButton = this._StartButton;
            this.CancelButton = this._CancelButton;

            void update(object sender, EventArgs args) => this.UpdatePreview();
            this._StartPicker.ValueChanged += update;
            this._EndPicker.ValueChanged += update;
            this._MinimumPerDayBox.ValueChanged += update;
            this._MaximumPerDayBox.ValueChanged += update;
            this._WindowStartBox.ValueChanged += update;
            this._WindowEndBox.ValueChanged += update;
        }

        private void LoadConfig(SpreadConfig config)
        {
            DateTime start;
            DateTime end;
            if (config.TryGetDates(out start, out end) == false)
            {
                start = DateTime.Today;
                end = DateTime.Today.AddDays(89);
            }

            this._StartPicker.Value = start;
            this._EndPicker.Value = end;
            this._MinimumPerDayBox.Value = Clamp(config.MinimumPerDay, 1, 200);
            this._MaximumPerDayBox.Value = Clamp(config.MaximumPerDay, 1, 200);
            this._WindowStartBox.Value = Clamp(config.WindowStartHour, 0, 23);
            this._WindowEndBox.Value = Clamp(config.WindowEndHour, 1, 24);
            this._CatchUpCheckBox.Checked = config.CatchUp;
        }

        private static decimal Clamp(int value, int minimum, int maximum)
        {
            return value < minimum ? minimum : (value > maximum ? maximum : value);
        }

        private SpreadConfig ReadConfig()
        {
            return new SpreadConfig()
            {
                StartDate = this._StartPicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = this._EndPicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MinimumPerDay = (int)this._MinimumPerDayBox.Value,
                MaximumPerDay = (int)this._MaximumPerDayBox.Value,
                WindowStartHour = (int)this._WindowStartBox.Value,
                WindowEndHour = (int)this._WindowEndBox.Value,
                CatchUp = this._CatchUpCheckBox.Checked,
            };
        }

        private bool ValidateConfig()
        {
            if (this._EndPicker.Value.Date < this._StartPicker.Value.Date)
            {
                MessageBox.Show(this, "结束日期不能早于开始日期。", "参数不对", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (this._WindowEndBox.Value <= this._WindowStartBox.Value)
            {
                MessageBox.Show(this, "时间窗口的结束要晚于开始。", "参数不对", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (this._MinimumPerDayBox.Value > this._MaximumPerDayBox.Value)
            {
                MessageBox.Show(this, "每日数量的下限不能大于上限。", "参数不对", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void UpdatePreview()
        {
            var config = this.ReadConfig();

            var pending = this._Achievements
                .Where(a => (a.Permission & 3) == 0 && a.IsAchieved == false)
                .ToList();

            this._PreviewList.BeginUpdate();
            this._PreviewList.Items.Clear();

            if (pending.Count == 0)
            {
                this._PreviewList.EndUpdate();
                this._SummaryLabel.Text = "当前没有待解锁的成就。";
                return;
            }

            SpreadSchedule schedule;
            try
            {
                schedule = new SpreadSchedule(config, pending.Count);
            }
            catch (Exception e)
            {
                this._PreviewList.EndUpdate();
                this._SummaryLabel.Text = "参数有问题：" + e.Message;
                return;
            }

            int cumulative = 0;
            int shown = 0;
            int dayIndex = 0;
            for (int day = 0; day < schedule.TotalDays; day++)
            {
                int quota = schedule.DailyQuota[day];
                if (quota <= 0)
                {
                    continue;
                }

                cumulative += quota;

                if (shown < 12)
                {
                    var dayDate = this._StartPicker.Value.Date.AddDays(day);
                    var entries = schedule.Entries.Skip(dayIndex).Take(quota).ToList();
                    var first = entries.Count > 0 ? entries[0].When : dayDate;
                    var last = entries.Count > 0 ? entries[entries.Count - 1].When : dayDate;

                    var item = new ListViewItem(dayDate.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture));
                    item.SubItems.Add(quota.ToString(CultureInfo.CurrentCulture));
                    item.SubItems.Add(first.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
                    item.SubItems.Add(last.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
                    item.SubItems.Add(cumulative.ToString(CultureInfo.CurrentCulture));
                    item.SubItems.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "{0:0} 分钟",
                        (last - first).TotalMinutes));
                    this._PreviewList.Items.Add(item);
                    shown++;
                }

                dayIndex += quota;
            }

            this._PreviewList.EndUpdate();

            int spanDays = schedule.EstimatedSpanDays();
            double average = spanDays > 0 ? (double)pending.Count / spanDays : 0;

            var text = string.Format(
                CultureInfo.CurrentCulture,
                "待解锁 {0} 个成就，计划用 {1} 天铺完（平均每天 {2:0.0} 个），大约 {3:yyyy-MM-dd} 结束。",
                pending.Count,
                spanDays,
                average,
                this._StartPicker.Value.Date.AddDays(Math.Max(0, spanDays - 1)));

            if (string.IsNullOrEmpty(schedule.Warning) == false)
            {
                text += "\n" + schedule.Warning;
            }

            this._SummaryLabel.Text = text;
        }
    }
}
