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
using System.Globalization;
using System.Windows.Forms;

namespace SAM.Game
{
    /// <summary>
    /// Progress window for a running <see cref="PacingSession"/>.
    /// Shows what has been unlocked, what is next, and how long the wait is.
    /// </summary>
    internal class PacingScheduleForm : Form
    {
        private readonly ListView _ListView;
        private readonly Label _SummaryLabel;
        private readonly Label _NextLabel;
        private readonly ProgressBar _ProgressBar;
        private readonly Button _StopButton;

        /// <summary>Raised when the user asks to stop the session.</summary>
        public event EventHandler StopRequested;

        /// <summary>True while a session is running and the window may not be closed silently.</summary>
        public bool IsRunning { get; set; } = true;

        private bool _StopRequested;

        public PacingScheduleForm(string gameName)
        {
            this.Text = "按解锁率节奏解锁中";
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(660, 420);
            this.MinimumSize = new Size(560, 320);
            this.ShowInTaskbar = true;
            this.MinimizeBox = true;

            var title = new Label()
            {
                Text = string.IsNullOrEmpty(gameName) == true
                    ? "正在按 Steam 全球解锁率的顺序，带随机空闲间隔地依次解锁成就。"
                    : $"正在按 Steam 全球解锁率的顺序，带随机空闲间隔地依次解锁《{gameName}》的成就。",
                Location = new Point(12, 12),
                Size = new Size(636, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            this._ProgressBar = new ProgressBar()
            {
                Location = new Point(12, 50),
                Size = new Size(636, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            this._NextLabel = new Label()
            {
                Location = new Point(12, 74),
                Size = new Size(636, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(0, 96, 0),
            };

            this._ListView = new ListView()
            {
                Location = new Point(12, 112),
                Size = new Size(636, 258),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };
            this._ListView.Columns.Add("#", 36);
            this._ListView.Columns.Add("成就", 250);
            this._ListView.Columns.Add("全球解锁率", 90, HorizontalAlignment.Right);
            this._ListView.Columns.Add("计划时间", 90, HorizontalAlignment.Right);
            this._ListView.Columns.Add("状态", 140);

            this._SummaryLabel = new Label()
            {
                Location = new Point(12, 376),
                Size = new Size(540, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };

            this._StopButton = new Button()
            {
                Text = "停止",
                Location = new Point(566, 376),
                Size = new Size(82, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            };
            this._StopButton.Click += this.OnStopButtonClick;

            this.Controls.AddRange(new Control[]
            {
                title, this._ProgressBar, this._NextLabel, this._ListView,
                this._SummaryLabel, this._StopButton,
            });

            this.FormClosing += this.OnFormClosing;
        }

        private void OnStopButtonClick(object sender, EventArgs e)
        {
            if (this.IsRunning == true)
            {
                this.RequestStop();
                return;
            }

            this.Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.IsRunning == false || this._StopRequested == true)
            {
                return;
            }

            // Closing mid-session means "stop"; the window stays until the session really stops.
            this.RequestStop();
            e.Cancel = true;
        }

        private void RequestStop()
        {
            if (this._StopRequested == true)
            {
                return;
            }

            this._StopRequested = true;
            this._StopButton.Enabled = false;
            this._StopButton.Text = "正在停止…";
            this.StopRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Fills the list with the planned steps.</summary>
        public void LoadSteps(PacingSession session)
        {
            this._ListView.BeginUpdate();
            this._ListView.Items.Clear();

            var steps = session.Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var item = new ListViewItem((i + 1).ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(step.Achievement.Name ?? step.Achievement.Id);
                item.SubItems.Add(step.Achievement.GlobalPercent.HasValue == true
                    ? step.Achievement.GlobalPercent.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%"
                    : "未知");
                item.SubItems.Add(TimeSpan.FromSeconds(step.OffsetSeconds).ToString(@"hh\:mm\:ss"));
                item.SubItems.Add("等待中");
                this._ListView.Items.Add(item);
            }

            this._ListView.EndUpdate();

            this._ProgressBar.Minimum = 0;
            this._ProgressBar.Maximum = Math.Max(1, steps.Count);
            this._ProgressBar.Value = 0;
        }

        /// <summary>Recomputes the countdown displayed for the next step.</summary>
        public void UpdateProgress(PacingSession session, DateTime startTime)
        {
            int done = session.Index;
            this._ProgressBar.Value = Math.Min(this._ProgressBar.Maximum, done);

            var steps = session.Steps;
            for (int i = 0; i < this._ListView.Items.Count && i < steps.Count; i++)
            {
                var item = this._ListView.Items[i];
                if (i < done)
                {
                    if (item.SubItems[4].Text != "已解锁")
                    {
                        item.SubItems[4].Text = "已解锁";
                        item.ForeColor = Color.FromArgb(0, 96, 0);
                    }
                }
                else
                {
                    item.SubItems[4].Text = i == done ? "下一个" : "等待中";
                }
            }

            var current = session.Current;
            if (current == null)
            {
                this._NextLabel.Text = "全部完成。";
                this._SummaryLabel.Text = $"已解锁 {done} / {steps.Count} 个成就。";
                return;
            }

            double remaining = session.SecondsUntilNextStep(startTime);
            string countdown = remaining <= 0.5
                ? "即将解锁"
                : $"还需等待 {FormatDuration(remaining)}";

            this._NextLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "进度 {0} / {1}　下一个：{2}（全球解锁率 {3}）　{4}",
                done,
                steps.Count,
                current.Achievement.Name ?? current.Achievement.Id,
                current.Achievement.GlobalPercent.HasValue == true
                    ? current.Achievement.GlobalPercent.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%"
                    : "未知",
                countdown);

            this._SummaryLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "已解锁 {0} / {1}，预计完成时间 {2}",
                done,
                steps.Count,
                startTime.AddSeconds(steps[steps.Count - 1].OffsetSeconds).ToString("g", CultureInfo.CurrentCulture));
        }

        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 小时 {1} 分 {2} 秒",
                    (int)span.TotalHours,
                    span.Minutes,
                    span.Seconds);
            }

            if (span.TotalMinutes >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 分 {1} 秒",
                    span.Minutes,
                    span.Seconds);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0} 秒", span.Seconds);
        }

        /// <summary>Called when the session ended (finished, stopped or failed).</summary>
        public void MarkStopped(string reason)
        {
            this.IsRunning = false;
            this._ProgressBar.Value = Math.Min(this._ProgressBar.Maximum, this._ProgressBar.Maximum);
            this._NextLabel.Text = reason;
            this._StopButton.Enabled = true;
            this._StopButton.Text = "关闭";
            this.Text = "按解锁率节奏解锁 — 已结束";
        }
    }
}
