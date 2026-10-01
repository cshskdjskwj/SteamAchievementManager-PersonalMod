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
using System.Linq;
using SAM.Game.Stats;

namespace SAM.Game
{
    internal enum PacingOrder
    {
        /// <summary>Most commonly unlocked achievements first (SteamDB 从高到低).</summary>
        MostCommonFirst = 0,

        /// <summary>Rarest achievements first.</summary>
        RarestFirst = 1,

        /// <summary>Random order, ignoring global rates.</summary>
        Random = 2,
    }

    /// <summary>
    /// How the idle gap between two unlocks is picked.
    /// </summary>
    internal enum PacingIntervalMode
    {
        /// <summary>Uniform random between Minimum and Maximum.</summary>
        Uniform = 0,

        /// <summary>A value close to Average, with occasional long breaks.</summary>
        RealisticBurst = 1,
    }

    internal class PacingSettings
    {
        /// <summary>Only achievements with a global rate in [MinimumPercent, MaximumPercent] are used.</summary>
        public double MinimumPercent = 0.0;

        public double MaximumPercent = 100.0;

        /// <summary>How long to wait before the first unlock, in seconds.</summary>
        public double InitialDelaySeconds = 0;

        /// <summary>Shortest idle gap between two unlocks, in seconds.</summary>
        public double MinimumIntervalSeconds = 60;

        /// <summary>Longest idle gap between two unlocks, in seconds.</summary>
        public double MaximumIntervalSeconds = 600;

        public PacingIntervalMode IntervalMode = PacingIntervalMode.Uniform;

        public PacingOrder Order = PacingOrder.MostCommonFirst;

        /// <summary>Skip achievements that are already unlocked.</summary>
        public bool SkipAlreadyUnlocked = true;
    }

    /// <summary>
    /// One planned unlock: an achievement plus when it is supposed to happen
    /// (seconds from the start of the session).
    /// </summary>
    internal class PacingStep
    {
        public AchievementInfo Achievement;
        public double OffsetSeconds;
    }

    /// <summary>
    /// Builds and runs a "pace" session: achievements are unlocked one by one,
    /// slowest-to-fastest (or whatever order was configured), with a randomized idle
    /// gap in between so the unlock history looks like a real play session.
    /// </summary>
    internal class PacingSession
    {
        private static readonly Random _Random = new();

        private readonly API.Client _Client;
        private readonly PacingSettings _Settings;

        public PacingSession(API.Client client, PacingSettings settings)
        {
            this._Client = client;
            this._Settings = settings;
        }

        public IReadOnlyList<PacingStep> Steps { get; private set; } = new List<PacingStep>();

        /// <summary>Index of the next step to run.</summary>
        public int Index { get; private set; }

        public DateTime? LastStoreTime { get; private set; }

        public bool IsFinished => this.Index >= this.Steps.Count;

        public PacingStep Current => this.IsFinished == true ? null : this.Steps[this.Index];

        /// <summary>
        /// Picks the achievements to unlock and assigns each one a randomized moment in time.
        /// </summary>
        public void Build(IEnumerable<AchievementInfo> achievements)
        {
            var candidates = achievements
                .Where(a => a != null)
                .Where(a => string.IsNullOrEmpty(a.Id) == false)
                // achievements flagged protected cannot be written to at all
                .Where(a => (a.Permission & 3) == 0)
                .ToList();

            if (this._Settings.SkipAlreadyUnlocked == true)
            {
                candidates = candidates.Where(a => a.IsAchieved == false).ToList();
            }

            var unknownPercent = new List<AchievementInfo>();

            if (this._Settings.Order != PacingOrder.Random)
            {
                // An achievement without a known global rate cannot be placed on the
                // "unlock rate" curve; park it at the end instead of dropping it.
                var known = new List<AchievementInfo>();
                foreach (var candidate in candidates)
                {
                    if (candidate.GlobalPercent.HasValue == true)
                    {
                        known.Add(candidate);
                    }
                    else
                    {
                        unknownPercent.Add(candidate);
                    }
                }

                known = this._Settings.Order == PacingOrder.MostCommonFirst
                    ? known.OrderByDescending(a => a.GlobalPercent.Value)
                        .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : known.OrderBy(a => a.GlobalPercent.Value)
                        .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                // global rate range filter
                known = known
                    .Where(a => a.GlobalPercent.Value >= this._Settings.MinimumPercent &&
                                a.GlobalPercent.Value <= this._Settings.MaximumPercent)
                    .ToList();

                candidates = known;

                if (this._Settings.SkipAlreadyUnlocked == false)
                {
                    // unknown-rate achievements stay in for the other orders
                }
            }
            else
            {
                candidates = candidates.OrderBy(_ => _Random.Next()).ToList();
            }

            if (this._Settings.Order != PacingOrder.Random)
            {
                candidates.AddRange(unknownPercent);
            }

            var steps = new List<PacingStep>(candidates.Count);
            double offset = Math.Max(0.0, this._Settings.InitialDelaySeconds);
            foreach (var achievement in candidates)
            {
                steps.Add(new PacingStep()
                {
                    Achievement = achievement,
                    OffsetSeconds = offset,
                });

                offset += this.NextIntervalSeconds();
            }

            this.Steps = steps;
            this.Index = 0;
        }

        /// <summary>
        /// 直接指定要解锁的成就序列（长时间铺开模式用）。
        /// </summary>
        public void SetSteps(IReadOnlyList<PacingStep> steps)
        {
            this.Steps = steps ?? new List<PacingStep>();
            this.Index = 0;
        }

        private double NextIntervalSeconds()
        {
            double minimum = Math.Max(0.0, this._Settings.MinimumIntervalSeconds);
            double maximum = Math.Max(minimum, this._Settings.MaximumIntervalSeconds);

            if (maximum <= minimum)
            {
                return minimum;
            }

            switch (this._Settings.IntervalMode)
            {
                case PacingIntervalMode.RealisticBurst:
                {
                    // Most gaps are short (a player is actively making progress), every
                    // so often there is a longer break (reading, cutscene, getting up).
                    bool longBreak = _Random.NextDouble() < 0.18;
                    if (longBreak == true)
                    {
                        return minimum + (_Random.NextDouble() * (maximum - minimum) * 0.7) + ((maximum - minimum) * 0.3);
                    }

                    double shortSpan = (maximum - minimum) * 0.25;
                    return minimum + (_Random.NextDouble() * shortSpan);
                }

                default:
                {
                    return minimum + (_Random.NextDouble() * (maximum - minimum));
                }
            }
        }

        /// <summary>
        /// Executes the step at <see cref="Index"/>: writes the achievement and commits it
        /// so Steam records the unlock (and its timestamp) right now.
        /// </summary>
        public bool RunCurrentStep(out string error)
        {
            error = null;

            var step = this.Current;
            if (step == null)
            {
                return true;
            }

            var info = step.Achievement;
            var stats = this._Client.SteamUserStats;

            const int maximumAttempts = 3;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                // 解锁时间由 Steam 服务端在 StoreStats 时盖章，客户端无法指定历史时间。
                if (stats.SetAchievement(info.Id, true) == false)
                {
                    error = $"Steam 拒绝写入成就 {info.Id}";
                    return false;
                }

                if (stats.StoreStats() == false)
                {
                    if (attempt < maximumAttempts)
                    {
                        System.Threading.Thread.Sleep(400 * attempt);
                        continue;
                    }

                    error = $"提交成就 {info.Id} 时 Steam 返回失败（可能被限流）";
                    return false;
                }

                // Confirm Steam really took it; StoreStats can silently drop rapid writes.
                bool isAchieved;
                if (stats.GetAchievementAndUnlockTime(info.Id, out isAchieved, out _) == true && isAchieved == true)
                {
                    info.IsAchieved = true;
                    info.UnlockTime = DateTime.Now;
                    this.LastStoreTime = DateTime.Now;
                    this.Index++;
                    return true;
                }

                if (attempt < maximumAttempts)
                {
                    System.Threading.Thread.Sleep(400 * attempt);
                }
            }

            error = $"成就 {info.Id} 写入后未生效，已重试 {maximumAttempts} 次";
            return false;
        }

        /// <summary>Seconds until the next step should run; 0 means "right now".</summary>
        public double SecondsUntilNextStep(DateTime startTime)
        {
            var step = this.Current;
            if (step == null)
            {
                return 0.0;
            }

            var due = startTime.AddSeconds(step.OffsetSeconds);
            var remaining = (due - DateTime.Now).TotalSeconds;
            return remaining < 0.0 ? 0.0 : remaining;
        }

        /// <summary>Skipped/ignored steps that the user should know about.</summary>
        public static string DescribeUnavailable(IEnumerable<AchievementInfo> achievements)
        {
            var list = achievements.ToList();
            int noPercent = list.Count(a => a.GlobalPercent.HasValue == false && (a.Permission & 3) == 0);
            int protectedCount = list.Count(a => (a.Permission & 3) != 0);
            return $"共 {list.Count} 个成就，其中 {protectedCount} 个受保护（无法写入），{noPercent} 个没有全球解锁率数据。";
        }

        /// <summary>
        /// Checks whether there is enough global rate data to order achievements by it.
        /// </summary>
        public static bool TryValidateRateData(
            IEnumerable<AchievementInfo> achievements,
            PacingSettings settings,
            out int totalCount,
            out int withRateCount,
            out int missingRateCount)
        {
            var candidates = achievements
                .Where(a => a != null && string.IsNullOrEmpty(a.Id) == false && (a.Permission & 3) == 0)
                .ToList();

            if (settings.SkipAlreadyUnlocked == true)
            {
                candidates = candidates.Where(a => a.IsAchieved == false).ToList();
            }

            totalCount = candidates.Count;
            withRateCount = candidates.Count(a => a.GlobalPercent.HasValue == true);
            missingRateCount = totalCount - withRateCount;

            // With "unknown rate at the end" allowed, a single known rate is enough to be useful.
            return withRateCount > 0;
        }
    }
}
