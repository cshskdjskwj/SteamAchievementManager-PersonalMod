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
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using static SAM.Game.InvariantShorthand;

// 魔改：T(...) 用于翻译用户可见的文本；与上面的 _(...) 不冲突
using static SAM.I18n.Localization;
using APITypes = SAM.API.Types;

using SAM.I18n;

namespace SAM.Game
{
    internal partial class Manager : Form
    {
        private readonly long _GameId;
        private readonly API.Client _SteamClient;

        private readonly WebClient _IconDownloader = new();

        /// <summary>
        /// 成就数超过这个值就不再自动下载图标。
        /// 图标只存在于内存中（关闭即释放、不占磁盘），但每个图标要一次网络请求，
        /// 上千个成就会让加载慢好几分钟，所以默认跳过。
        /// </summary>
        private const int AutoSkipIconsThreshold = 100;

        private bool _ApplyingIconDefault;
        private bool _IconDefaultSkipped;
        private string _IconSkipNote;
        private long _IconDecisionGameId = -1;
        private bool _IconPreferenceSetByUser;

        private readonly List<Stats.AchievementInfo> _IconQueue = new();
        private readonly List<Stats.StatDefinition> _StatDefinitions = new();

        private readonly List<Stats.AchievementDefinition> _AchievementDefinitions = new();

        private readonly BindingList<Stats.StatInfo> _Statistics = new();

        private readonly API.Callbacks.UserStatsReceived _UserStatsReceivedCallback;

        private readonly API.Callbacks.GameAchievementData _GameAchievementDataCallback;

        private readonly Dictionary<string, double> _GlobalPercentByAchievementId =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Stats.AchievementInfo> _AllAchievements =
            new(StringComparer.OrdinalIgnoreCase);

        private bool _WaitingForGlobalPercentages;
        private bool _GlobalPercentagesLoaded;
        private bool _GlobalPercentagesFetched;
        private Timer _GlobalPercentTimeoutTimer;
        private bool _IsUnlockingSchedule;
        private Timer _PacingTimer;
        private PacingSession _PacingSession;
        private PacingScheduleForm _PacingStatusForm;
        private DateTime _PacingStartTime;
        private SortMode _AchievementSortMode = SortMode.None;

        private enum SortMode
        {
            None = 0,
            GlobalPercentDescending = 1,
            GlobalPercentAscending = 2,
        }

        public Manager(long gameId, API.Client client)
        {
            this.InitializeComponent();

            // 魔改：按当前语言替换界面上的既有文本（含工具栏、列头、标签页）
            Localization.ApplyTo(this);

            // 关键：上游把 ListView.Sorting 设成了 Ascending，控件会自己按第一列
            // （成就名）自动排序，从而覆盖掉"按全球解锁率"的排序结果 —— 表现就是
            // 点了排序按钮却像没生效。这里关掉自动排序，改由 GetAchievementItemsInOrder 控制。
            this._AchievementListView.Sorting = SortOrder.None;
            this._AchievementListView.ListViewItemSorter = null;

            this._MainTabControl.SelectedTab = this._AchievementsTabPage;
            //this.statisticsList.Enabled = this.checkBox1.Checked;

            this._AchievementImageList.Images.Add("Blank", new Bitmap(64, 64));

            this._StatisticsDataGridView.AutoGenerateColumns = false;

            this._StatisticsDataGridView.Columns.Add("name", "Name");
            this._StatisticsDataGridView.Columns[0].ReadOnly = true;
            this._StatisticsDataGridView.Columns[0].Width = 200;
            this._StatisticsDataGridView.Columns[0].DataPropertyName = "DisplayName";

            this._StatisticsDataGridView.Columns.Add("value", "Value");
            this._StatisticsDataGridView.Columns[1].ReadOnly = this._EnableStatsEditingCheckBox.Checked == false;
            this._StatisticsDataGridView.Columns[1].Width = 90;
            this._StatisticsDataGridView.Columns[1].DataPropertyName = "Value";

            this._StatisticsDataGridView.Columns.Add("extra", "Extra");
            this._StatisticsDataGridView.Columns[2].ReadOnly = true;
            this._StatisticsDataGridView.Columns[2].Width = 200;
            this._StatisticsDataGridView.Columns[2].DataPropertyName = "Extra";

            this._StatisticsDataGridView.DataSource = new BindingSource()
            {
                DataSource = this._Statistics,
            };

            // 全球解锁率列（由魔改添加）：数据来自 Steam 客户端 / Steam Web API
            // 这两列不硬写英文字面量，统一走 T() 取译文，
            // 否则会覆盖掉上面 ApplyTo 已经翻译好的列头（"Unlock Time" 就是这么被漏掉的）。
            this._AchievementListView.Columns[2].Text = T("Unlock Time");
            this._AchievementListView.Columns.Add(T("Unlock Rate"), 90, HorizontalAlignment.Right);

            this._GameId = gameId;
            this._SteamClient = client;

            this._IconDownloader.DownloadDataCompleted += this.OnIconDownload;

            string name = this._SteamClient.SteamApps001.GetAppData((uint)this._GameId, "name");
            if (name != null)
            {
                base.Text += " | " + name;
            }
            else
            {
                base.Text += " | " + this._GameId.ToString(CultureInfo.InvariantCulture);
            }

            this._UserStatsReceivedCallback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
            this._UserStatsReceivedCallback.OnRun += this.OnUserStatsReceived;

            // 全球解锁率回调（id 1102），由魔改添加
            this._GameAchievementDataCallback =
                client.CreateAndRegisterCallback<API.Callbacks.GameAchievementData>();
            this._GameAchievementDataCallback.OnRun += this.OnGameAchievementData;

            this._GlobalPercentTimeoutTimer = new Timer()
            {
                Interval = 3000,
            };
            this._GlobalPercentTimeoutTimer.Tick += this.OnGlobalPercentTimeout;

            // 节奏解锁定时器：负责在随机空闲间隔之后写入下一个成就
            this._PacingTimer = new Timer()
            {
                Interval = 250,
            };
            this._PacingTimer.Tick += this.OnPacingTimerTick;

            // 关窗时保存「长时间铺开」的进度
            this.FormClosing += this.OnFormClosingSpread;

            this.RefreshStats();
        }

        private void AddAchievementIcon(Stats.AchievementInfo info, Image icon)
        {
            if (icon == null)
            {
                info.ImageIndex = 0;
            }
            else
            {
                info.ImageIndex = this._AchievementImageList.Images.Count;
                this._AchievementImageList.Images.Add(info.IsAchieved == true ? info.IconNormal : info.IconLocked, icon);
            }
        }

        private void OnIconDownload(object sender, DownloadDataCompletedEventArgs e)
        {
            if (e.Error == null && e.Cancelled == false)
            {
                var info = (Stats.AchievementInfo)e.UserState;

                Bitmap bitmap;
                try
                {
                    using (MemoryStream stream = new())
                    {
                        stream.Write(e.Result, 0, e.Result.Length);
                        bitmap = new(stream);
                    }
                }
                catch (Exception)
                {
                    bitmap = null;
                }

                this.AddAchievementIcon(info, bitmap);
                this._AchievementListView.Update();
            }

            this.DownloadNextIcon();
        }

        private void DownloadNextIcon()
        {
            if (this._IconQueue.Count == 0)
            {
                this._DownloadStatusLabel.Visible = false;
                return;
            }

            if (this._IconDownloader.IsBusy == true)
            {
                return;
            }

            this._DownloadStatusLabel.Text = T("Downloading {0} icons...", this._IconQueue.Count);
            this._DownloadStatusLabel.Visible = true;

            var info = this._IconQueue[0];
            this._IconQueue.RemoveAt(0);


            this._IconDownloader.DownloadDataAsync(
                new Uri(_($"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{this._GameId}/{(info.IsAchieved == true ? info.IconNormal : info.IconLocked)}")),
                info);
        }

        private static string TranslateError(int id) => id switch
        {
            2 => "generic error -- this usually means you don't own the game",
            _ => _($"{id}"),
        };

        private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
        {
            var name = kv[language].AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            if (language != "english")
            {
                name = kv["english"].AsString("");
                if (string.IsNullOrEmpty(name) == false)
                {
                    return name;
                }
            }

            name = kv.AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            return defaultValue;
        }

        private bool LoadUserGameStatsSchema()
        {
            string path;
            try
            {
                string fileName = _($"UserGameStatsSchema_{this._GameId}.bin");
                path = API.Steam.GetInstallPath();
                path = Path.Combine(path, "appcache", "stats", fileName);
                if (File.Exists(path) == false)
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                return false;
            }

            var kv = KeyValue.LoadAsBinary(path);
            if (kv == null)
            {
                return false;
            }

            var currentLanguage = this._SteamClient.SteamApps008.GetCurrentGameLanguage();

            this._AchievementDefinitions.Clear();
            this._StatDefinitions.Clear();

            var stats = kv[this._GameId.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (stats.Valid == false || stats.Children == null)
            {
                return false;
            }

            foreach (var stat in stats.Children)
            {
                if (stat.Valid == false)
                {
                    continue;
                }

                APITypes.UserStatType type;

                // schema in the new format?
                var typeNode = stat["type"];
                if (typeNode.Valid == true && typeNode.Type == KeyValueType.String)
                {
                    if (Enum.TryParse((string)typeNode.Value, true, out type) == false)
                    {
                        type = APITypes.UserStatType.Invalid;
                    }
                }
                else
                {
                    type = APITypes.UserStatType.Invalid;
                }

                // schema in the old format?
                if (type == APITypes.UserStatType.Invalid)
                {
                    var typeIntNode = stat["type_int"];
                    var rawType = typeIntNode.Valid == true
                        ? typeIntNode.AsInteger(0)
                        : typeNode.AsInteger(0);
                    type = (APITypes.UserStatType)rawType;
                }

                switch (type)
                {
                    case APITypes.UserStatType.Invalid:
                    {
                        break;
                    }

                    case APITypes.UserStatType.Integer:
                    {
                        var id = stat["name"].AsString("");
                        string name = GetLocalizedString(stat["display"]["name"], currentLanguage, id);

                        this._StatDefinitions.Add(new Stats.IntegerStatDefinition()
                        {
                            Id = stat["name"].AsString(""),
                            DisplayName = name,
                            MinValue = stat["min"].AsInteger(int.MinValue),
                            MaxValue = stat["max"].AsInteger(int.MaxValue),
                            MaxChange = stat["maxchange"].AsInteger(0),
                            IncrementOnly = stat["incrementonly"].AsBoolean(false),
                            SetByTrustedGameServer = stat["bSetByTrustedGS"].AsBoolean(false),
                            DefaultValue = stat["default"].AsInteger(0),
                            Permission = stat["permission"].AsInteger(0),
                        });
                        break;
                    }

                    case APITypes.UserStatType.Float:
                    case APITypes.UserStatType.AverageRate:
                    {
                        var id = stat["name"].AsString("");
                        string name = GetLocalizedString(stat["display"]["name"], currentLanguage, id);

                        this._StatDefinitions.Add(new Stats.FloatStatDefinition()
                        {
                            Id = stat["name"].AsString(""),
                            DisplayName = name,
                            MinValue = stat["min"].AsFloat(float.MinValue),
                            MaxValue = stat["max"].AsFloat(float.MaxValue),
                            MaxChange = stat["maxchange"].AsFloat(0.0f),
                            IncrementOnly = stat["incrementonly"].AsBoolean(false),
                            DefaultValue = stat["default"].AsFloat(0.0f),
                            Permission = stat["permission"].AsInteger(0),
                        });
                        break;
                    }

                    case APITypes.UserStatType.Achievements:
                    case APITypes.UserStatType.GroupAchievements:
                    {
                        if (stat.Children != null)
                        {
                            foreach (var bits in stat.Children.Where(
                                b => string.Compare(b.Name, "bits", StringComparison.InvariantCultureIgnoreCase) == 0))
                            {
                                if (bits.Valid == false || bits.Children == null)
                                {
                                    continue;
                                }

                                foreach (var bit in bits.Children)
                                {
                                    string id = bit["name"].AsString("");
                                    string name = GetLocalizedString(bit["display"]["name"], currentLanguage, id);
                                    string desc = GetLocalizedString(bit["display"]["desc"], currentLanguage, "");

                                    this._AchievementDefinitions.Add(new()
                                    {
                                        Id = id,
                                        Name = name,
                                        Description = desc,
                                        IconNormal = bit["display"]["icon"].AsString(""),
                                        IconLocked = bit["display"]["icon_gray"].AsString(""),
                                        IsHidden = bit["display"]["hidden"].AsBoolean(false),
                                        Permission = bit["permission"].AsInteger(0),
                                    });
                                }
                            }
                        }

                        break;
                    }

                    default:
                    {
                        throw new InvalidOperationException("invalid stat type");
                    }
                }
            }

            return true;
        }

        private void OnUserStatsReceived(APITypes.UserStatsReceived param)
        {
            if (param.Result != 1)
            {
                this._GameStatusLabel.Text = $"Error while retrieving stats: {TranslateError(param.Result)}";
                this.EnableInput();
                return;
            }

            if (this.LoadUserGameStatsSchema() == false)
            {
                this._GameStatusLabel.Text = "Failed to load schema.";
                this.EnableInput();
                return;
            }

            try
            {
                this.GetAchievements();
            }
            catch (Exception e)
            {
                this._GameStatusLabel.Text = "Error when handling achievements retrieval.";
                this.EnableInput();
                MessageBox.Show(
                    T("Error when handling achievements retrieval:\n") + e,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            try
            {
                this.GetStatistics();
            }
            catch (Exception e)
            {
                this._GameStatusLabel.Text = "Error when handling stats retrieval.";
                this.EnableInput();
                MessageBox.Show(
                    T("Error when handling stats retrieval:\n") + e,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            this._GameStatusLabel.Text = T("Retrieved {0} achievements and {1} statistics.", this._AchievementListView.Items.Count, this._StatisticsDataGridView.Rows.Count);
            this.EnableInput();

            // 魔改：成就数过多时默认不下载图标（每次打开都要重新下载，很耗时）
            this.ApplyIconLoadDefault();

            // 魔改：成就列表就绪后再去取全球解锁率，避免发得太早
            this.FetchGlobalPercentages();

            // 魔改：如果上次有没跑完的「长时间铺开」计划，自动接着跑
            this.ResumeSpreadIfAny();

        }

        private void RefreshStats()
        {
            if (this._IsUnlockingSchedule == true)
            {
                // 节奏解锁期间刷新会把列表清空，进度窗口会跟着错乱
                return;
            }

            this._AchievementListView.Items.Clear();
            this._AllAchievements.Clear();
            this._StatisticsDataGridView.Rows.Clear();

            // 关掉图标下载后，把还没下完的队列清空，避免继续发网络请求
            if (this._LoadIconsCheckBox.Checked == false)
            {
                this._IconQueue.Clear();
                this._DownloadStatusLabel.Visible = false;
            }

            var steamId = this._SteamClient.SteamUser.GetSteamId();

            // This still triggers the UserStatsReceived callback, in addition to the callresult.
            // No need to implement callresults for the time being.
            var callHandle = this._SteamClient.SteamUserStats.RequestUserStats(steamId);
            if (callHandle == API.CallHandle.Invalid)
            {
                MessageBox.Show(this, "Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this._GameStatusLabel.Text = "Retrieving stat information...";
            this.DisableInput();
        }

        private bool _IsUpdatingAchievementList;

        private void GetAchievements()
        {
            var textSearch = this._MatchingStringTextBox.Text.Length > 0
                ? this._MatchingStringTextBox.Text
                : null;

            this._IsUpdatingAchievementList = true;

            this._AchievementListView.Items.Clear();
            this._AllAchievements.Clear();
            this._AchievementListView.BeginUpdate();

            bool wantLocked = this._DisplayLockedOnlyButton.Checked == true;
            bool wantUnlocked = this._DisplayUnlockedOnlyButton.Checked == true;

            foreach (var def in this._AchievementDefinitions)
            {
                if (string.IsNullOrEmpty(def.Id) == true)
                {
                    continue;
                }

                if (this._SteamClient.SteamUserStats.GetAchievementAndUnlockTime(
                    def.Id,
                    out bool isAchieved,
                    out var unlockTime) == false)
                {
                    continue;
                }

                Stats.AchievementInfo info = new()
                {
                    Id = def.Id,
                    IsAchieved = isAchieved,
                    UnlockTime = isAchieved == true && unlockTime > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).LocalDateTime
                        : null,
                    IconNormal = string.IsNullOrEmpty(def.IconNormal) ? null : def.IconNormal,
                    IconLocked = string.IsNullOrEmpty(def.IconLocked) ? def.IconNormal : def.IconLocked,
                    Permission = def.Permission,
                    Name = def.Name,
                    Description = def.Description,
                };

                // 魔改添加的全球解锁率
                if (this._GlobalPercentByAchievementId.TryGetValue(def.Id, out var globalPercent) == true)
                {
                    info.GlobalPercent = globalPercent;
                }

                // 记住所有成就（含被显示过滤器隐藏的），节奏解锁要用到完整列表
                this._AllAchievements[info.Id] = info;

                bool wanted = (wantLocked == false && wantUnlocked == false) || isAchieved switch
                {
                    true => wantUnlocked,
                    false => wantLocked,
                };
                if (wanted == false)
                {
                    continue;
                }

                if (textSearch != null)
                {
                    if (def.Name.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                        def.Description.IndexOf(textSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                this.CreateAchievementItem(info);
            }

            this.GetAchievementItemsInOrder();
            this._AchievementListView.EndUpdate();
            this._IsUpdatingAchievementList = false;

            this.DownloadNextIcon();
        }

        /// <summary>Creates the list view row for an achievement and fills it into the icon queue.</summary>
        private void CreateAchievementItem(Stats.AchievementInfo info)
        {
            ListViewItem item = new()
            {
                Checked = info.IsAchieved,
                Tag = info,
                Text = info.Name,
                BackColor = (info.Permission & 3) == 0 ? Color.Black : Color.FromArgb(64, 0, 0),
            };

            info.Item = item;

            if (item.Text.StartsWith("#", StringComparison.InvariantCulture) == true)
            {
                item.Text = info.Id;
                item.SubItems.Add("");
            }
            else
            {
                item.SubItems.Add(info.Description);
            }

            item.SubItems.Add(info.UnlockTime.HasValue == true
                ? info.UnlockTime.Value.ToString()
                : "");

            // 魔改添加的全球解锁率显示
            item.SubItems.Add(info.GlobalPercent.HasValue == true
                ? info.GlobalPercent.Value.ToString("0.00", CultureInfo.CurrentCulture) + "%"
                : "");

            info.ImageIndex = 0;

            this.AddAchievementToIconQueue(info, false);
            this._AchievementListView.Items.Add(item);
        }

        /// <summary>Applies the current "Unlock Rate" sort order to the list view.</summary>
        private void GetAchievementItemsInOrder()
        {
            if (this._AchievementSortMode == SortMode.None)
            {
                return;
            }

            var items = new System.Collections.Generic.List<ListViewItem>();
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                items.Add(item);
            }

            bool ascending = this._AchievementSortMode == SortMode.GlobalPercentAscending;

            // 没有解锁率数据的排在最后
            var sorted = ascending
                ? items.OrderBy(i => i.Tag is Stats.AchievementInfo a && a.GlobalPercent.HasValue == true
                        ? a.GlobalPercent.Value
                        : double.MaxValue)
                    .ToList()
                : items.OrderByDescending(i => i.Tag is Stats.AchievementInfo a && a.GlobalPercent.HasValue == true
                        ? a.GlobalPercent.Value
                        : double.MinValue)
                    .ToList();

            // 关键：Clear()/AddRange() 会触发 ItemCheck -> OnCheckAchievement，
            // 而那个处理函数会再调一次 GetAchievements() 把列表按原始顺序重建，
            // 结果就是"排完序立刻被打乱"。用同一个标志把这段保护起来。
            this._IsUpdatingAchievementList = true;
            try
            {
                this._AchievementListView.BeginUpdate();
                this._AchievementListView.Items.Clear();
                this._AchievementListView.Items.AddRange(sorted.ToArray());
            }
            finally
            {
                this._AchievementListView.EndUpdate();
                this._IsUpdatingAchievementList = false;
            }
        }

        private void GetStatistics()
        {
            this._Statistics.Clear();
            foreach (var stat in this._StatDefinitions)
            {
                if (string.IsNullOrEmpty(stat.Id) == true)
                {
                    continue;
                }

                if (stat is Stats.IntegerStatDefinition intStat)
                {
                    if (this._SteamClient.SteamUserStats.GetStatValue(intStat.Id, out int value) == false)
                    {
                        continue;
                    }
                    this._Statistics.Add(new Stats.IntStatInfo()
                    {
                        Id = intStat.Id,
                        DisplayName = intStat.DisplayName,
                        IntValue = value,
                        OriginalValue = value,
                        IsIncrementOnly = intStat.IncrementOnly,
                        Permission = intStat.Permission,
                    });
                }
                else if (stat is Stats.FloatStatDefinition floatStat)
                {
                    if (this._SteamClient.SteamUserStats.GetStatValue(floatStat.Id, out float value) == false)
                    {
                        continue;
                    }
                    this._Statistics.Add(new Stats.FloatStatInfo()
                    {
                        Id = floatStat.Id,
                        DisplayName = floatStat.DisplayName,
                        FloatValue = value,
                        OriginalValue = value,
                        IsIncrementOnly = floatStat.IncrementOnly,
                        Permission = floatStat.Permission,
                    });
                }
            }
        }

        private void AddAchievementToIconQueue(Stats.AchievementInfo info, bool startDownload)
        {
            int imageIndex = this._AchievementImageList.Images.IndexOfKey(
                info.IsAchieved == true ? info.IconNormal : info.IconLocked);

            if (imageIndex >= 0)
            {
                info.ImageIndex = imageIndex;
            }
            else
            {
                // 用户关掉了「下载图标」：不排队，直接用占位图
                if (this._LoadIconsCheckBox.Checked == false)
                {
                    info.ImageIndex = 0;
                    return;
                }

                this._IconQueue.Add(info);

                if (startDownload == true)
                {
                    this.DownloadNextIcon();
                }
            }
        }

        /// <summary>
        /// 成就数超过阈值时，默认不下载图标并告知用户。
        /// 每个游戏只自动决定一次；用户一旦手动勾选，就完全尊重用户的选择。
        /// </summary>
        private void ApplyIconLoadDefault()
        {
            if (this._IconDecisionGameId == this._GameId)
            {
                return;
            }

            this._IconDecisionGameId = this._GameId;

            if (this._IconPreferenceSetByUser == true)
            {
                return;
            }

            int count = this._AchievementDefinitions.Count;
            if (count <= AutoSkipIconsThreshold)
            {
                return;
            }


            this._ApplyingIconDefault = true;
            try
            {
                this._LoadIconsCheckBox.Checked = false;
            }
            finally
            {
                this._ApplyingIconDefault = false;
            }

            // 关键：此时图标已经全部入队了，必须把队列清掉，
            // 否则后台仍会把这几百上千个请求一个个发完。
            this._IconQueue.Clear();
            this._DownloadStatusLabel.Visible = false;

            this._IconDefaultSkipped = true;
            this._IconSkipNote = string.Format(
                CultureInfo.CurrentCulture,
                "这个游戏有 {0} 个成就，已默认跳过图标下载（图标只存在于内存、关闭即释放，不占磁盘空间；" +
                "但每个图标需要一次网络请求，全部下载会明显变慢）。需要图标请勾选工具栏的「下载图标」再点 Refresh。",
                count);

            this._GameStatusLabel.Text = this._IconSkipNote;
        }

        private void OnLoadIconsChanged(object sender, EventArgs e)
        {
            // 程序自己改的勾选状态不算用户意愿
            if (this._ApplyingIconDefault == false)
            {
                this._IconPreferenceSetByUser = true;
            }

            this._GameStatusLabel.Text = this._LoadIconsCheckBox.Checked == true
                ? T("图标下载：开（点 Refresh 或重开游戏后生效）")
                : T("图标下载：关（点 Refresh 或重开游戏后生效）");
        }

        /// <summary>切换界面语言：还原成原始 key 后按新语言重刷，并记住选择。</summary>
        private void OnChangeLanguage(object sender, EventArgs e)
        {
            var picked = LanguagePickerForm.Ask(true);
            if (string.IsNullOrEmpty(picked) == true)
            {
                return;
            }

            // 界面上的文本已经是上一个语言的译文，直接再翻译会因为 key 对不上而失效，
            // 所以先按旧语言反查回原始 key，再按新语言刷一遍。
            Localization.RestoreKeys(this);
            Localization.SetLanguage(picked);
            Localization.SaveLanguage(Localization.Current);
            Localization.ApplyTo(this);

            this._GameStatusLabel.Text = Localization.Current == "en"
                ? "Interface language switched to English. Achievement names and descriptions still follow the Steam client language."
                : "界面语言已切换。成就名称与描述仍跟随 Steam 客户端语言。";
        }

        private int StoreAchievements()
        {
            if (this._AchievementListView.Items.Count == 0)
            {
                return 0;
            }

            List<Stats.AchievementInfo> achievements = new();
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                if (item.Tag is not Stats.AchievementInfo achievementInfo ||
                    achievementInfo.IsAchieved == item.Checked)
                {
                    continue;
                }

                achievementInfo.IsAchieved = item.Checked;
                achievements.Add(achievementInfo);
            }

            if (achievements.Count == 0)
            {
                return 0;
            }

            foreach (var info in achievements)
            {
                if (this._SteamClient.SteamUserStats.SetAchievement(info.Id, info.IsAchieved) == false)
                {
                    MessageBox.Show(
                        this,
                        T("An error occurred while setting the state for {0}, aborting store.", info.Id),
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return -1;
                }
            }

            return achievements.Count;
        }

        private int StoreStatistics()
        {
            if (this._Statistics.Count == 0)
            {
                return 0;
            }

            var statistics = this._Statistics.Where(stat => stat.IsModified == true).ToList();
            if (statistics.Count == 0)
            {
                return 0;
            }

            foreach (var stat in statistics)
            {
                if (stat is Stats.IntStatInfo intStat)
                {
                    if (this._SteamClient.SteamUserStats.SetStatValue(
                        intStat.Id,
                        intStat.IntValue) == false)
                    {
                        MessageBox.Show(
                            this,
                            T("An error occurred while setting the value for {0}, aborting store.", stat.Id),
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return -1;
                    }
                }
                else if (stat is Stats.FloatStatInfo floatStat)
                {
                    if (this._SteamClient.SteamUserStats.SetStatValue(
                        floatStat.Id,
                        floatStat.FloatValue) == false)
                    {
                        MessageBox.Show(
                            this,
                            T("An error occurred while setting the value for {0}, aborting store.", stat.Id),
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return -1;
                    }
                }
                else
                {
                    throw new InvalidOperationException("unsupported stat type");
                }
            }

            return statistics.Count;
        }

        private void DisableInput()
        {
            this._ReloadButton.Enabled = false;
            this._StoreButton.Enabled = false;
            this._SpreadButton.Enabled = false;
        }

        private void EnableInput()
        {
            this._ReloadButton.Enabled = true;
            this._StoreButton.Enabled = true;
            this._SpreadButton.Enabled = true;
        }

        private bool _IsRunningCallbacks;

        private void OnTimer(object sender, EventArgs e)
        {
            // StoreStats() 会泵消息，可能重入到这里
            if (this._IsRunningCallbacks == true)
            {
                return;
            }

            this._IsRunningCallbacks = true;
            try
            {
                this._CallbackTimer.Enabled = false;
                this._SteamClient.RunCallbacks(false);
            }
            finally
            {
                this._CallbackTimer.Enabled = true;
                this._IsRunningCallbacks = false;
            }
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            this.RefreshStats();
        }

        private void OnLockAll(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                item.Checked = false;
            }
        }

        private void OnInvertAll(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                item.Checked = !item.Checked;
            }
        }

        private void OnUnlockAll(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this._AchievementListView.Items)
            {
                item.Checked = true;
            }
        }

        private bool Store()
        {
            if (this._SteamClient.SteamUserStats.StoreStats() == false)
            {
                MessageBox.Show(
                    this,
                    T("An error occurred while storing, aborting."),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private void OnStore(object sender, EventArgs e)
        {
            int achievements = this.StoreAchievements();
            if (achievements < 0)
            {
                this.RefreshStats();
                return;
            }

            int stats = this.StoreStatistics();
            if (stats < 0)
            {
                this.RefreshStats();
                return;
            }

            if (this.Store() == false)
            {
                this.RefreshStats();
                return;
            }

            MessageBox.Show(
                this,
                T("Stored {0} achievements and {1} statistics.", achievements, stats),
                "Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            this.RefreshStats();
        }

        private void OnStatDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            if (e.Context != DataGridViewDataErrorContexts.Commit)
            {
                return;
            }

            var view = (DataGridView)sender;
            if (e.Exception is Stats.StatIsProtectedException)
            {
                e.ThrowException = false;
                e.Cancel = true;
                view.Rows[e.RowIndex].ErrorText = "Stat is protected! -- you can't modify it";
            }
            else
            {
                e.ThrowException = false;
                e.Cancel = true;
                view.Rows[e.RowIndex].ErrorText = "Invalid value";
            }
        }

        private void OnStatAgreementChecked(object sender, EventArgs e)
        {
            this._StatisticsDataGridView.Columns[1].ReadOnly = this._EnableStatsEditingCheckBox.Checked == false;
        }

        private void OnStatCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            var view = (DataGridView)sender;
            view.Rows[e.RowIndex].ErrorText = "";
        }

        private void OnResetAllStats(object sender, EventArgs e)
        {
            // 注意：这三个确认框都必须用 "!= Yes" 判断。
            // 用 "== No" 的话，点 X 关闭对话框返回的是 Cancel，会被当成"没选否"而继续执行重置。
            if (MessageBox.Show(
                T("Are you absolutely sure you want to reset stats?"),
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            bool achievementsToo = DialogResult.Yes == MessageBox.Show(
                T("Do you want to reset achievements too?"),
                "Question",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (MessageBox.Show(
                T("Really really sure?"),
                "Warning",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error) != DialogResult.Yes)
            {
                return;
            }

            if (this._SteamClient.SteamUserStats.ResetAllStats(achievementsToo) == false)
            {
                MessageBox.Show(this, "Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.RefreshStats();
        }

        private void OnCheckAchievement(object sender, ItemCheckEventArgs e)
        {
            if (sender != this._AchievementListView)
            {
                return;
            }

            if (this._IsUpdatingAchievementList == true)
            {
                return;
            }

            if (this._AchievementListView.Items[e.Index].Tag is not Stats.AchievementInfo info)
            {
                return;
            }

            if ((info.Permission & 3) != 0)
            {
                MessageBox.Show(
                    this,
                    T("Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager."),
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                e.NewValue = e.CurrentValue;
            }
        }

        private void OnDisplayUncheckedOnly(object sender, EventArgs e)
        {
            if ((sender as ToolStripButton).Checked == true)
            {
                this._DisplayLockedOnlyButton.Checked = false;
            }

            this.GetAchievements();
        }

        private void OnDisplayCheckedOnly(object sender, EventArgs e)
        {
            if ((sender as ToolStripButton).Checked == true)
            {
                this._DisplayUnlockedOnlyButton.Checked = false;
            }

            this.GetAchievements();
        }

        private void OnFilterUpdate(object sender, KeyEventArgs e)
        {
            this.GetAchievements();
        }

        #region 魔改：全球解锁率

        private string GetGameName()
        {
            try
            {
                return this._SteamClient.SteamApps001.GetAppData((uint)this._GameId, "name");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Asks Steam for the global unlock rates of the current game. The Steam client
        /// answers through the GameAchievementData callback (id 1102); if that never shows
        /// up we fall back to the public Steam Web API.
        /// </summary>
        private void FetchGlobalPercentages()
        {
            if (this._GlobalPercentagesFetched == true || this._WaitingForGlobalPercentages == true)
            {
                return;
            }

            try
            {
                var callHandle = this._SteamClient.SteamUserStats.RequestGlobalAchievementPercentages();
                if (callHandle != API.CallHandle.Invalid)
                {
                    this._WaitingForGlobalPercentages = true;
                    this._GlobalPercentTimeoutTimer.Stop();
                    this._GlobalPercentTimeoutTimer.Start();
                    return;
                }
            }
            catch (Exception)
            {
                // fall through to the web API
            }

            this.StartWebPercentageFetch();
        }

        private void OnGameAchievementData(APITypes.GameAchievementData param)
        {
            this._GlobalPercentTimeoutTimer.Stop();

            if (this._WaitingForGlobalPercentages == false)
            {
                return;
            }

            this._WaitingForGlobalPercentages = false;

            Dictionary<string, double> percentages;
            try
            {
                percentages = API.Callbacks.GameAchievementData.ReadPercentages(param);
            }
            catch (Exception e)
            {
                this._GameStatusLabel.Text = "解析 Steam 返回的全球解锁率失败：" + e.Message;
                this.StartWebPercentageFetch();
                return;
            }

            if (percentages.Count == 0)
            {
                // Steam returned nothing usable, try the web API instead
                this.StartWebPercentageFetch();
                return;
            }

            this._GlobalPercentagesLoaded = true;
            this._GlobalPercentagesFetched = true;
            this.ApplyGlobalPercentages(percentages);
        }

        private void OnGlobalPercentTimeout(object sender, EventArgs e)
        {
            this._GlobalPercentTimeoutTimer.Stop();
            if (this._WaitingForGlobalPercentages == false)
            {
                return;
            }

            this._WaitingForGlobalPercentages = false;
            this.StartWebPercentageFetch();
        }

        private void StartWebPercentageFetch()
        {
            if (this._GlobalPercentagesLoaded == true || this._GlobalPercentagesFetched == true)
            {
                return;
            }

            this._GlobalPercentagesFetched = true;
            this._GameStatusLabel.Text = "Steam 没有返回全球解锁率，正在从 Steam Web API 获取…";

            var gameId = this._GameId;
            var thread = new System.Threading.Thread(() =>
            {
                Dictionary<string, double> percentages = null;
                string error = null;
                try
                {
                    percentages = GlobalAchievementPercentages.FetchFromWebApi(gameId);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }

                try
                {
                    this.BeginInvoke(new Action(() => this.OnWebPercentagesFetched(percentages, error)));
                }
                catch (InvalidOperationException)
                {
                    // the window went away while downloading
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void OnWebPercentagesFetched(Dictionary<string, double> percentages, string error)
        {
            if (this.IsDisposed == true)
            {
                return;
            }

            if (percentages == null || percentages.Count == 0)
            {
                this._GameStatusLabel.Text = "无法获取全球解锁率：" +
                    (error ?? "Steam 与 Steam Web API 都没有返回数据") +
                    "（按解锁率排序解锁功能仍可用，但只能随机顺序）";
                return;
            }

            this._GlobalPercentagesLoaded = true;
            this._GameStatusLabel.Text = $"已从 Steam Web API 获取 {percentages.Count} 个成就的全球解锁率。";
            this.ApplyGlobalPercentages(percentages);
        }

        private void ApplyGlobalPercentages(Dictionary<string, double> percentages)
        {
            this._GlobalPercentByAchievementId.Clear();
            foreach (var pair in percentages)
            {
                this._GlobalPercentByAchievementId[pair.Key] = pair.Value;
            }

            int matched = 0;
            foreach (var info in this._AllAchievements.Values)
            {
                if (this._GlobalPercentByAchievementId.TryGetValue(info.Id, out var percent) == true)
                {
                    info.GlobalPercent = percent;
                    matched++;
                }
            }

            // 百分比是异步到达的，重新生成一次列表把数值显示出来
            if (this._AchievementListView.Items.Count == 0 && this._AllAchievements.Count == 0)
            {
                return;
            }

            this.GetAchievements();
            // 图标跳过说明放在这里，否则会被这条异步返回的状态文本覆盖掉
            this._GameStatusLabel.Text = $"已更新 {matched} 个成就的全球解锁率。" +
                (this._IconDefaultSkipped == true && string.IsNullOrEmpty(this._IconSkipNote) == false
                    ? "  " + this._IconSkipNote
                    : "");
        }

        #endregion

        #region 魔改：按解锁率节奏解锁

        private void OnSortByUnlockRate(object sender, EventArgs e)
        {
            this._AchievementSortMode = this._AchievementSortMode == SortMode.GlobalPercentDescending
                ? SortMode.GlobalPercentAscending
                : SortMode.GlobalPercentDescending;

            this.GetAchievements();

            this._GameStatusLabel.Text = this._AchievementSortMode == SortMode.GlobalPercentDescending
                ? "已按全球解锁率从高到低排序。"
                : "已按全球解锁率从低到高排序。";
        }

        private void OnAchievementColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column != 3)
            {
                return;
            }

            this.OnSortByUnlockRate(sender, EventArgs.Empty);
        }

        private void OnUnlockByRatePacing(object sender, EventArgs e)
        {
            if (this._IsUnlockingSchedule == true)
            {
                MessageBox.Show(
                    this,
                    T("已经有一个按节奏解锁的任务在运行了。"),
                    T("提示"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this._AllAchievements.Count == 0)
            {
                MessageBox.Show(
                    this,
                    T("成就数据还没有加载完，请稍等一下或者点一下 Refresh。"),
                    T("提示"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var all = this._AllAchievements.Values.ToList();
            var initial = this.CreateDefaultPacingSettings();

            // 先判断"还有没有可解锁的成就"。
            // 否则全成就解锁时会误报成"缺少全球解锁率数据"，把人引到错误的方向。
            int lockedCount = all.Count(a => (a.Permission & 3) == 0 && a.IsAchieved == false);
            if (lockedCount == 0)
            {
                MessageBox.Show(
                    this,
                    T("这个游戏已经没有未解锁的成就了。\n\n") +
                    "如果你想重新刷一遍，可以先用工具栏的 Reset 重置成就，或者手动取消勾选某些成就。",
                    T("没有可解锁的成就"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (PacingSession.TryValidateRateData(all, initial, out var total, out var withRate, out var missingRate) == false)
            {
                var answer = MessageBox.Show(
                    this,
                    T("现在没有任何成就的全球解锁率数据（待处理 {0} 个）。\n\n", total) +
                    "可能是 Steam 还没返回数据，或者这个游戏的成就没有公开统计。\n\n" +
                    "可以先点 Refresh 重试；也可以改用随机顺序解锁。要继续配置吗？",
                    T("缺少全球解锁率数据"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                // 只有明确点「是」才继续；点「否」或直接关掉这个提示框都视为取消
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                initial.Order = PacingOrder.Random;
            }

            using (var form = new PacingSettingsForm(initial, all))
            {
                // 必须看返回值：点 X 或「取消」时是 Cancel，不能当成确认
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                this.StartPacingSession(form.Settings);
            }
        }

        private PacingSettings CreateDefaultPacingSettings()
        {
            var settings = new PacingSettings();
            settings.MinimumIntervalSeconds = 60;
            settings.MaximumIntervalSeconds = 600;
            settings.IntervalMode = PacingIntervalMode.RealisticBurst;
            settings.Order = PacingOrder.MostCommonFirst;

            if (PacingSession.TryValidateRateData(
                this._AllAchievements.Values,
                settings,
                out var totalCount,
                out var withRate,
                out var missingRate) == false || withRate == 0)
            {
                settings.Order = PacingOrder.Random;
            }

            return settings;
        }

        private void StartPacingSession(PacingSettings settings)
        {
            var session = new PacingSession(this._SteamClient, settings);
            session.Build(this._AllAchievements.Values);

            if (session.Steps.Count == 0)
            {
                int lockedTotal = this._AllAchievements.Values
                    .Count(a => (a.Permission & 3) == 0 && a.IsAchieved == false);
                int lockedWithRate = this._AllAchievements.Values
                    .Count(a => (a.Permission & 3) == 0 && a.IsAchieved == false &&
                                a.GlobalPercent.HasValue == true);
                double? lowestRate = this._AllAchievements.Values
                    .Where(a => (a.Permission & 3) == 0 && a.IsAchieved == false &&
                                a.GlobalPercent.HasValue == true)
                    .Select(a => (double?)a.GlobalPercent.Value)
                    .OrderBy(v => v)
                    .FirstOrDefault();

                MessageBox.Show(
                    this,
                    T("按当前条件没有需要解锁的成就。\n\n") +
                    $"这个游戏还有 {lockedTotal} 个成就没解锁，其中 {lockedWithRate} 个有全球解锁率数据" +
                    (lowestRate.HasValue == true
                        ? $"，最低的解锁率是 {lowestRate.Value:0.00}%。\n\n"
                        : "。\n\n") +
                    "常见原因：\n" +
                    "· 解锁率区间设得太窄，把它放宽（例如下限设 0、上限设 100）即可。\n" +
                    "· 勾选了“跳过已经解锁的成就”，而剩下的成就都已经解锁了。",
                    T("没有可解锁的成就"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            this._PacingSession = session;
            this._PacingStartTime = DateTime.Now;
            this._IsUnlockingSchedule = true;

            this.DisableInput();
            this._UnlockByRateButton.Enabled = false;
            this._AchievementListView.Enabled = false;

            this._PacingStatusForm = new PacingScheduleForm(this.GetGameName());
            this._PacingStatusForm.StopRequested += this.OnPacingStopRequested;
            this._PacingStatusForm.LoadSteps(session);
            this._PacingStatusForm.UpdateProgress(session, this._PacingStartTime);
            this._PacingStatusForm.Show(this);

            this._PacingTimer.Start();
        }

        private void OnPacingStopRequested(object sender, EventArgs e)
        {
            this._PacingTimer.Stop();
            this.FinishPacingSession("已手动停止。");
        }

        private void OnPacingTimerTick(object sender, EventArgs e)
        {
            if (this._IsUnlockingSchedule == false || this._PacingSession == null)
            {
                this._PacingTimer.Stop();
                return;
            }

            // 到时间了就写入下一个成就，否则只刷新倒计时
            if (this._PacingSession.IsFinished == true ||
                this._PacingSession.SecondsUntilNextStep(this._PacingStartTime) <= 0.0)
            {
                if (this._PacingSession.IsFinished == false)
                {
                    string error;
                    if (this._PacingSession.RunCurrentStep(out error) == false)
                    {
                        this._PacingTimer.Stop();
                        this._PacingStatusForm?.UpdateProgress(this._PacingSession, this._PacingStartTime);
                        this.FinishPacingSession("出错了：" + error);
                        return;
                    }

                    this._GameStatusLabel.Text =
                        $"按节奏解锁中：{this._PacingSession.Index} / {this._PacingSession.Steps.Count}";
                }

                if (this._PacingSession.IsFinished == true)
                {
                    this._PacingTimer.Stop();
                    this._PacingStatusForm?.UpdateProgress(this._PacingSession, this._PacingStartTime);
                    this.FinishPacingSession(
                        $"全部完成，共解锁 {this._PacingSession.Steps.Count} 个成就。" +
                        "（解锁时间由 Steam 服务端盖章，客户端无法指定历史时间）");
                    return;
                }
            }

            this._PacingStatusForm?.UpdateProgress(this._PacingSession, this._PacingStartTime);
        }

        private void FinishPacingSession(string reason)
        {
            this._IsUnlockingSchedule = false;
            this._PacingTimer.Stop();

            if (this._PacingStatusForm != null)
            {
                this._PacingStatusForm.MarkStopped(reason);
            }

            this.EnableInput();
            this._UnlockByRateButton.Enabled = true;
            this._AchievementListView.Enabled = true;

            this._GameStatusLabel.Text = "按节奏解锁结束：" + reason;

            // 重新从 Steam 读回真实状态与解锁时间
            this.RefreshStats();
        }

        #endregion

        #region 魔改：长时间铺开（按天解锁）

        private SpreadProgress _SpreadProgress;
        private PacingSession _SpreadSession;
        private Timer _SpreadTimer;
        private DateTime _SpreadLastStepTime = DateTime.MinValue;
        private bool _IsSpreading;

        /// <summary>点「长时间铺开」：加载/新建计划并开始按天解锁。</summary>
        private void OnSpreadOverDays(object sender, EventArgs e)
        {
            if (this._IsSpreading == true || this._IsUnlockingSchedule == true)
            {
                MessageBox.Show(
                    this,
                    T("已经有一个解锁任务在运行了。"),
                    T("提示"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (this._AllAchievements.Count == 0)
            {
                MessageBox.Show(
                    this,
                    T("成就数据还没有加载完，请稍等一下或者点一下 Refresh。"),
                    T("提示"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var pending = this._AllAchievements.Values
                .Where(a => (a.Permission & 3) == 0 && a.IsAchieved == false)
                .OrderByDescending(a => a.GlobalPercent ?? double.MinValue)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pending.Count == 0)
            {
                MessageBox.Show(
                    this,
                    T("当前没有待解锁的成就。"),
                    T("提示"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // 已有计划则先校验它是否还成立
            var progress = SpreadProgress.Load(this._GameId);
            if (progress != null && this.IsSpreadProgressValid(progress, out var invalidReason) == false)
            {
                MessageBox.Show(
                    this,
                    T("已有计划已失效，将重新排一份：\n") + invalidReason,
                    T("提示"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SpreadProgress.Delete(this._GameId);
                progress = null;
            }

            string existingText = null;
            if (progress != null)
            {
                int done = progress.Index;
                int total = progress.Order.Length;
                var next = done < total
                    ? this.DescribeSpreadNext(progress)
                    : "全部已排完";

                existingText = string.Format(
                    CultureInfo.CurrentCulture,
                    "已有计划：共 {0} 个成就，已解锁 {1} 个，下一个：{2}。",
                    total,
                    done,
                    next);
            }

            var initial = progress != null
                ? progress.Config
                : this.CreateDefaultSpreadConfig();

            using (var form = new SpreadPlanForm(initial, pending, existingText))
            {
                // 必须看返回值：点 X 或「取消」时是 Cancel，
                // 否则不但会开跑，还会用对话框里的当前值覆盖掉已保存的计划配置
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                bool rebuild = progress == null || form.ResetExistingPlan == true;
                if (rebuild == true)
                {
                    progress = this.CreateSpreadProgress(form.Config, pending);
                    if (progress == null)
                    {
                        return;
                    }
                }
                else
                {
                    progress.Config = form.Config;
                    progress.Save();
                }

                this._SpreadProgress = progress;
            }

            this.StartSpreadRun();
        }

        private SpreadConfig CreateDefaultSpreadConfig()
        {
            return new SpreadConfig()
            {
                StartDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = DateTime.Today.AddDays(89).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MinimumPerDay = 3,
                MaximumPerDay = 8,
                WindowStartHour = 19,
                WindowEndHour = 23,
                CatchUp = true,
            };
        }

        private SpreadProgress CreateSpreadProgress(
            SpreadConfig config,
            System.Collections.Generic.List<Stats.AchievementInfo> pending)
        {
            SpreadSchedule schedule;
            try
            {
                schedule = new SpreadSchedule(config, pending.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    T("计划参数有问题：") + ex.Message,
                    T("参数不对"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            schedule.Assign(pending.Select(a => a.Id));

            var progress = new SpreadProgress()
            {
                AppId = this._GameId,
                Config = config,
                Order = schedule.Entries.Select(entry => entry.Id).ToArray(),
                Index = 0,
            };
            progress.Save();

            return progress;
        }

        /// <summary>检查已保存的计划是否还能用（成就是否被外部改动过）。</summary>
        private bool IsSpreadProgressValid(SpreadProgress progress, out string reason)
        {
            reason = null;

            if (progress.Order == null || progress.Order.Length == 0)
            {
                reason = "计划内容为空。";
                return false;
            }

            int index = Math.Max(0, Math.Min(progress.Index, progress.Order.Length));
            for (int i = index; i < progress.Order.Length; i++)
            {
                if (this._AllAchievements.TryGetValue(progress.Order[i], out var info) == false)
                {
                    reason = "成就 " + progress.Order[i] + " 不在当前游戏里。";
                    return false;
                }

                if (info.IsAchieved == true)
                {
                    reason = "成就 " + progress.Order[i] + " 已经被解锁了。";
                    return false;
                }
            }

            return true;
        }

        private string DescribeSpreadNext(SpreadProgress progress)
        {
            if (progress.Index >= progress.Order.Length)
            {
                return "全部已排完";
            }

            var id = progress.Order[progress.Index];
            return this._AllAchievements.TryGetValue(id, out var info) == true
                ? (info.Name ?? id)
                : id;
        }

        private void StartSpreadRun()
        {
            if (this._SpreadProgress == null)
            {
                return;
            }

            this._IsSpreading = true;
            this._SpreadLastStepTime = DateTime.MinValue;

            this.DisableInput();
            this._UnlockByRateButton.Enabled = false;
            this._SpreadButton.Enabled = false;
            this._AchievementListView.Enabled = false;

            if (this._SpreadTimer == null)
            {
                this._SpreadTimer = new Timer()
                {
                    Interval = 30000,
                };
                this._SpreadTimer.Tick += this.OnSpreadTimerTick;
            }

            this._SpreadTimer.Start();
            this.OnSpreadTimerTick(this, EventArgs.Empty);
        }

        /// <summary>到点就解锁当天该解锁的成就；没到点只刷新状态。</summary>
        private void OnSpreadTimerTick(object sender, EventArgs e)
        {
            if (this._IsSpreading == false || this._SpreadProgress == null)
            {
                this._SpreadTimer?.Stop();
                return;
            }

            // 正在跑当天的队列：到间隔就解锁下一个
            if (this._SpreadSession != null)
            {
                if (this._SpreadSession.IsFinished == true)
                {
                    this.FinishSpreadDay(true, null);
                    return;
                }

                if ((DateTime.Now - this._SpreadLastStepTime).TotalSeconds >= this.GetSpreadStepGapSeconds())
                {
                    return; // 还没到下一个的时刻
                }

                string error;
                if (this._SpreadSession.RunCurrentStep(out error) == false)
                {
                    this.FinishSpreadDay(false, error);
                    return;
                }

                this._SpreadProgress.Index = this._SpreadSession.Index;
                this._SpreadProgress.Save();
                this._SpreadLastStepTime = DateTime.Now;

                this._GameStatusLabel.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "长时间铺开中：今天已解锁 {0} 个，累计 {1} / {2}",
                    this._SpreadSession.Index - this._SpreadDayStartIndex,
                    this._SpreadProgress.Index,
                    this._SpreadProgress.Order.Length);

                if (this._SpreadSession.IsFinished == true)
                {
                    this.FinishSpreadDay(true, null);
                }

                return;
            }

            // 没在跑：判断该不该开始今天的一批
            var next = this.GetNextSpreadEntry();
            if (next == null)
            {
                this.FinishSpreadAll();
                return;
            }

            var now = DateTime.Now;
            if (next.When > now)
            {
                this._GameStatusLabel.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "长时间铺开待命中：下一个成就将在 {0:MM-dd HH:mm:ss} 解锁（{1} 之后）；累计 {2} / {3}",
                    next.When,
                    FormatSpan(next.When - now),
                    this._SpreadProgress.Index,
                    this._SpreadProgress.Order.Length);
                return;
            }

            // 到点（或已过时）：可以开工
            if (this._SpreadProgress.Config.CatchUp == false &&
                next.When.Date < now.Date)
            {
                // 用户选择"错过就跳过"：把这天的剩余条目直接跳过
                this.SkipSpreadDay(next.When.Date);
                return;
            }

            this.StartSpreadDay(next);
        }

        private SpreadEntry GetNextSpreadEntry()
        {
            var progress = this._SpreadProgress;
            if (progress == null || progress.Index >= progress.Order.Length)
            {
                return null;
            }

            // 时间表与 order 一一对应，但 order 只存了 id；时间按 index 重新推算
            var schedule = this.GetSpreadTimes(progress);
            if (schedule == null || progress.Index >= schedule.Count)
            {
                return null;
            }

            return new SpreadEntry()
            {
                Id = progress.Order[progress.Index],
                When = schedule[progress.Index],
            };
        }

        /// <summary>
        /// 按保存的配置与顺序重新算出每个成就的计划时刻（确定性，无需额外存档）。
        /// </summary>
        private System.Collections.Generic.List<DateTime> GetSpreadTimes(SpreadProgress progress)
        {
            if (this._SpreadTimes != null && this._SpreadTimesAppId == progress.AppId &&
                this._SpreadTimesCount == progress.Order.Length)
            {
                return this._SpreadTimes;
            }

            try
            {
                var schedule = new SpreadSchedule(progress.Config, progress.Order.Length);
                var times = schedule.Entries.Select(entry => entry.When).ToList();
                this._SpreadTimes = times;
                this._SpreadTimesAppId = progress.AppId;
                this._SpreadTimesCount = progress.Order.Length;
                return times;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private System.Collections.Generic.List<DateTime> _SpreadTimes;
        private long _SpreadTimesAppId = -1;
        private int _SpreadTimesCount = -1;
        private int _SpreadDayStartIndex;

        private void StartSpreadDay(SpreadEntry next)
        {
            // 今天这一批：把所有时刻已经不晚于"现在 + 一点缓冲"的后续条目算进同一批
            var schedule = this.GetSpreadTimes(this._SpreadProgress);
            if (schedule == null)
            {
                this.FinishSpreadDay(false, "计划时间表重建失败");
                return;
            }

            var day = next.When.Date;
            var batch = new System.Collections.Generic.List<PacingStep>();
            int index = this._SpreadProgress.Index;
            while (index < schedule.Count && schedule[index].Date == day)
            {
                if (this._AllAchievements.TryGetValue(this._SpreadProgress.Order[index], out var info) == true &&
                    info.IsAchieved == false)
                {
                    batch.Add(new PacingStep()
                    {
                        Achievement = info,
                        OffsetSeconds = 0,
                    });
                }

                index++;
            }

            if (batch.Count == 0)
            {
                this.SkipSpreadDay(day);
                return;
            }

            this._SpreadDayStartIndex = this._SpreadProgress.Index;
            this._SpreadSession = new PacingSession(this._SteamClient, new PacingSettings());
            this._SpreadSession.SetSteps(batch);
            this._SpreadLastStepTime = DateTime.MinValue;

            this._GameStatusLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "长时间铺开：开始解锁 {0:MM-dd} 的 {1} 个成就…",
                day,
                batch.Count);
        }

        private double GetSpreadStepGapSeconds()
        {
            // 一天之内用 20~90 秒的小间隔，避免几个成就挤在同一秒
            return 20.0 + (this._SpreadGapRandom.NextDouble() * 70.0);
        }

        private readonly Random _SpreadGapRandom = new();

        private void SkipSpreadDay(DateTime day)
        {
            var schedule = this.GetSpreadTimes(this._SpreadProgress);
            if (schedule == null)
            {
                this.FinishSpreadAll();
                return;
            }

            int index = this._SpreadProgress.Index;
            while (index < schedule.Count && schedule[index].Date == day)
            {
                index++;
            }

            this._SpreadProgress.Index = index;
            this._SpreadProgress.Save();
            this._GameStatusLabel.Text = $"长时间铺开：已跳过 {day:MM-dd}（错过时间窗口）。";
        }

        private void FinishSpreadDay(bool success, string error)
        {
            this._SpreadSession = null;

            if (success == false)
            {
                this.FinishSpreadAll();
                MessageBox.Show(
                    this,
                    T("长时间铺开出错了：{0}\n\n计划已保存，下次打开可以继续。", error),
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (this._SpreadProgress.Index >= this._SpreadProgress.Order.Length)
            {
                this.FinishSpreadAll();
                return;
            }

            var schedule = this.GetSpreadTimes(this._SpreadProgress);
            var when = schedule != null && this._SpreadProgress.Index < schedule.Count
                ? schedule[this._SpreadProgress.Index].ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
                : "（待定）";

            this._GameStatusLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "长时间铺开：今天的份额已完成，累计 {0} / {1}。下一个：{2}",
                this._SpreadProgress.Index,
                this._SpreadProgress.Order.Length,
                when);
        }

        private void FinishSpreadAll()
        {
            this._SpreadTimer?.Stop();

            bool wasSpreading = this._IsSpreading;
            this._IsSpreading = false;
            this._SpreadSession = null;

            this.EnableInput();
            this._UnlockByRateButton.Enabled = true;
            this._SpreadButton.Enabled = true;
            this._AchievementListView.Enabled = true;

            if (this._SpreadProgress != null)
            {
                if (this._SpreadProgress.Index >= this._SpreadProgress.Order.Length)
                {
                    SpreadProgress.Delete(this._SpreadProgress.AppId);
                    this._GameStatusLabel.Text =
                        $"长时间铺开：全部完成，共解锁 {this._SpreadProgress.Order.Length} 个成就。";
                    this._SpreadProgress = null;
                }
                else
                {
                    this._SpreadProgress.Save();
                }
            }

            if (wasSpreading == true)
            {
                this.RefreshStats();
            }
        }

        private void StopSpread(bool silent)
        {
            this._SpreadTimer?.Stop();
            this._IsSpreading = false;
            this._SpreadSession = null;

            this._SpreadProgress?.Save();

            this.EnableInput();
            this._UnlockByRateButton.Enabled = true;
            this._SpreadButton.Enabled = true;
            this._AchievementListView.Enabled = true;

            if (silent == false)
            {
                this._GameStatusLabel.Text = "长时间铺开已暂停，进度已保存。下次打开会自动接着跑。";
            }
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span.TotalDays >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 天 {1} 小时",
                    (int)span.TotalDays,
                    span.Hours);
            }

            if (span.TotalHours >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} 小时 {1} 分",
                    (int)span.TotalHours,
                    span.Minutes);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0} 分钟", Math.Max(1, (int)span.TotalMinutes));
        }

        /// <summary>启动时自动接着上次的计划跑。</summary>
        private void ResumeSpreadIfAny()
        {
            var progress = SpreadProgress.Load(this._GameId);
            if (progress == null)
            {
                return;
            }

            if (this.IsSpreadProgressValid(progress, out var reason) == false)
            {
                this._GameStatusLabel.Text = "长时间铺开的旧计划已失效：" + reason;
                SpreadProgress.Delete(this._GameId);
                return;
            }

            this._SpreadProgress = progress;
            this.StartSpreadRun();
        }

        private void OnFormClosingSpread(object sender, FormClosingEventArgs e)
        {
            if (this._IsSpreading == true)
            {
                this.StopSpread(true);
            }
        }

        #endregion
    }
}
