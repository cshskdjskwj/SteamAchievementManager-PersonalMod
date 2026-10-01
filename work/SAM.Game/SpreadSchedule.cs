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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SAM.Game.Stats;

namespace SAM.Game
{
    /// <summary>
    /// 单个成就的计划：什么时候应该被解锁。
    /// </summary>
    internal class SpreadEntry
    {
        public string Id;

        /// <summary>本地时间。</summary>
        public DateTime When;
    }

    /// <summary>
    /// 「长时间铺开」的配置。
    /// </summary>
    [DataContract]
    internal class SpreadConfig
    {
        [DataMember(Name = "startDate")]
        public string StartDate = "";

        [DataMember(Name = "endDate")]
        public string EndDate = "";

        [DataMember(Name = "minPerDay")]
        public int MinimumPerDay = 3;

        [DataMember(Name = "maxPerDay")]
        public int MaximumPerDay = 8;

        [DataMember(Name = "windowStartHour")]
        public int WindowStartHour = 19;

        [DataMember(Name = "windowEndHour")]
        public int WindowEndHour = 23;

        /// <summary>错过时间窗口后是否补做（true = 补，false = 跳过那天）。</summary>
        [DataMember(Name = "catchUp")]
        public bool CatchUp = true;

        public bool TryGetDates(out DateTime start, out DateTime end)
        {
            start = DateTime.MinValue;
            end = DateTime.MinValue;

            if (DateTime.TryParseExact(
                    this.StartDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out start) == false)
            {
                return false;
            }

            if (DateTime.TryParseExact(
                    this.EndDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out end) == false)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 进度存档：哪个成就排在第几位、已经解锁到第几个。
    /// 存到 exe 旁边的 spread-&lt;appid&gt;.json，重启后可以接着跑。
    /// </summary>
    [DataContract]
    internal class SpreadProgress
    {
        [DataMember(Name = "appId")]
        public long AppId;

        [DataMember(Name = "order")]
        public string[] Order = new string[0];

        [DataMember(Name = "index")]
        public int Index;

        [DataMember(Name = "config")]
        public SpreadConfig Config = new SpreadConfig();

        [DataMember(Name = "lastRunUtc")]
        public string LastRunUtc = "";

        public static string GetPath(long appId)
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                string.Format(CultureInfo.InvariantCulture, "spread-{0}.json", appId));
        }

        public static SpreadProgress Load(long appId)
        {
            var path = GetPath(appId);
            if (File.Exists(path) == false)
            {
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(SpreadProgress));
                    var progress = (SpreadProgress)serializer.ReadObject(stream);
                    if (progress == null || progress.Order == null || progress.Order.Length == 0)
                    {
                        return null;
                    }

                    return progress;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Save()
        {
            this.LastRunUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            var path = GetPath(this.AppId);
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(SpreadProgress));
                serializer.WriteObject(stream, this);
                File.WriteAllBytes(path, stream.ToArray());
            }
        }

        public static void Delete(long appId)
        {
            var path = GetPath(appId);
            if (File.Exists(path) == true)
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// 生成「长时间铺开」的时间表。
    ///
    /// 重要前提：Steam 的成就解锁时间是**服务端**在收到上报时盖章的，客户端无法指定历史时间
    /// （详见 README 第 7 节）。所以想让成就看起来是在过去几个月里陆续解锁的，唯一办法就是
    /// 真的在那段时间里逐个解锁——这个类负责的正是"每天该解锁哪几个、大概什么时候"。
    /// </summary>
    internal class SpreadSchedule
    {
        private static readonly Random _Random = new();

        private readonly DateTime _StartDate;
        private readonly DateTime _EndDate;

        public SpreadSchedule(SpreadConfig config, int achievementCount)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            DateTime start;
            DateTime end;
            if (config.TryGetDates(out start, out end) == false)
            {
                throw new ArgumentException("日期格式不正确，需要 yyyy-MM-dd。", nameof(config));
            }

            if (end < start)
            {
                throw new ArgumentException("结束日期不能早于开始日期。", nameof(config));
            }

            this.Config = config;
            this._StartDate = start.Date;
            this._EndDate = end.Date;
            this.TotalDays = (int)(this._EndDate - this._StartDate).TotalDays + 1;
            this.AchievementCount = achievementCount;

            this.DailyQuota = new int[this.TotalDays];
            this.Entries = new List<SpreadEntry>(achievementCount);
            this.BuildPlan(achievementCount);
        }

        public SpreadConfig Config { get; }

        public int TotalDays { get; }

        public int AchievementCount { get; }

        /// <summary>每天计划解锁的数量。</summary>
        public int[] DailyQuota { get; }

        public List<SpreadEntry> Entries { get; }

        /// <summary>计划排不完时给出的提示（null 表示没问题）。</summary>
        public string Warning { get; private set; }

        private void BuildPlan(int achievementCount)
        {
            int min = Math.Max(1, this.Config.MinimumPerDay);
            int max = Math.Max(min, this.Config.MaximumPerDay);

            if (achievementCount <= 0)
            {
                return;
            }

            // 1) 给每一天分配配额，直到成就分完（或天数用完）
            int remaining = achievementCount;
            for (int day = 0; day < this.TotalDays && remaining > 0; day++)
            {
                int quota = _Random.Next(min, max + 1);
                if (quota > remaining)
                {
                    quota = remaining;
                }

                this.DailyQuota[day] = quota;
                remaining -= quota;
            }

            if (remaining > 0)
            {
                // 配额不足以铺完，把剩下的平摊到全部天数里（宁可每天多一点，也不要最后一天爆量）
                this.Warning = string.Format(
                    CultureInfo.CurrentCulture,
                    "按 {0}~{1} 个/天的配额，{2} 天只能铺 {3} 个，还剩 {4} 个没排进去；" +
                    "已自动提高每日数量以便排完，建议把结束日期往后延或提高每日上限。",
                    min,
                    max,
                    this.TotalDays,
                    achievementCount - remaining,
                    remaining);

                int extra = remaining;
                int index = 0;
                while (extra > 0)
                {
                    this.DailyQuota[index % this.TotalDays]++;
                    extra--;
                    index++;
                }
            }

            // 2) 给每一天生成具体时刻
            int cursor = 0;
            var windowStart = new TimeSpan(Math.Max(0, Math.Min(23, this.Config.WindowStartHour)), 0, 0);
            var windowEnd = new TimeSpan(Math.Max(0, Math.Min(24, this.Config.WindowEndHour)), 0, 0);
            if (windowEnd <= windowStart)
            {
                windowEnd = windowStart.Add(TimeSpan.FromHours(1));
            }

            double windowSeconds = (windowEnd - windowStart).TotalSeconds;

            for (int day = 0; day < this.TotalDays; day++)
            {
                int quota = this.DailyQuota[day];
                if (quota <= 0)
                {
                    continue;
                }

                var dayDate = this._StartDate.AddDays(day);
                var dayStart = dayDate + windowStart;
                var dayEnd = dayDate + windowEnd;

                // 每天的开始时刻加一点随机，避免天与天之间过于整齐
                double lead = _Random.NextDouble() * Math.Min(30.0 * 60.0, windowSeconds * 0.25);
                var first = dayStart.AddSeconds(lead);

                double available = (dayEnd - first).TotalSeconds;
                if (available < 0)
                {
                    available = 0;
                }

                // 把当天的成就散在剩余窗口里：基础间隔 + 抖动（抖动取间隔的一小部分，
                // 保证时间严格递增，不会几个成就挤在同一秒）
                double baseInterval = quota > 1 ? available / (quota - 1) : 0.0;
                if (baseInterval > 3600.0)
                {
                    // 间隔过大就说明这天其实很空，收一点，看起来像"玩了一会儿"
                    baseInterval = 3600.0;
                }

                double jitter = baseInterval * 0.35;
                var when = first;
                for (int i = 0; i < quota && cursor < achievementCount; i++)
                {
                    if (i > 0)
                    {
                        double gap = baseInterval + ((_Random.NextDouble() * 2.0 - 1.0) * jitter);
                        if (gap < 20.0)
                        {
                            gap = 20.0;
                        }

                        when = when.AddSeconds(gap);
                        if (when > dayEnd)
                        {
                            when = dayEnd;
                        }
                    }

                    this.Entries.Add(new SpreadEntry()
                    {
                        Id = null, // 由调用方按顺序填成就 id
                        When = when,
                    });

                    cursor++;
                }
            }

            // 时间可能因为抖动略微乱序，排一下（保持稳定顺序）
            var ordered = this.Entries.OrderBy(e => e.When).ToList();
            this.Entries.Clear();
            this.Entries.AddRange(ordered);
        }

        /// <summary>把成就 id 按顺序填进计划。</summary>
        public void Assign(IEnumerable<string> achievementIds)
        {
            int index = 0;
            foreach (var id in achievementIds)
            {
                if (index >= this.Entries.Count)
                {
                    break;
                }

                this.Entries[index].Id = id;
                index++;
            }
        }

        /// <summary>按计划铺完所有成就，大约需要多少个自然日。</summary>
        public int EstimatedSpanDays()
        {
            int days = 0;
            foreach (var quota in this.DailyQuota)
            {
                if (quota > 0)
                {
                    days++;
                }
            }

            return days;
        }
    }
}
