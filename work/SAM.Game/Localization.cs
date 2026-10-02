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
using System.Windows.Forms;

namespace SAM.I18n
{
    /// <summary>
    /// 界面语言。
    /// </summary>
    internal class LanguageInfo
    {
        public string Code;
        public string NativeName;

        public override string ToString()
        {
            return this.NativeName;
        }
    }

    /// <summary>
    /// 极简本地化框架。
    ///
    /// 设计取舍：不改上游的 .csproj / .resx（那样会破坏"与上游最小差异"这一点，
    /// 也不好合并上游更新），而是用**英文原文作为 key** 在运行时替换。
    /// 代价是英文原文不能随意改动；收益是加一门语言只要往表里加一列。
    ///
    /// 两种用法：
    ///   1. 用 T("原文") 包装用户可见的字符串（对话框、状态栏等）；
    ///   2. 对控件树调用 ApplyTo，自动翻译 Text 之类的既有属性。
    /// </summary>
    internal static class Localization
    {
        public const string DefaultLanguage = "en";

        private static string _Current = DetectSystemLanguage();

        /// <summary>当前语言代码。</summary>
        public static string Current
        {
            get { return _Current; }
        }

        /// <summary>是否使用中文界面。</summary>
        public static bool IsChinese
        {
            get { return _Current == "zh-Hans"; }
        }

        /// <summary>所有可选语言。</summary>
        public static LanguageInfo[] Available = new[]
        {
            new LanguageInfo() { Code = "zh-Hans", NativeName = "简体中文" },
            new LanguageInfo() { Code = "en", NativeName = "English" },
            new LanguageInfo() { Code = "zh-Hant", NativeName = "繁體中文" },
        };

        /// <summary>
        /// 翻译表。key 是**原始界面文本**（上游是英文，魔改窗口是中文），
        /// value 按语言代码索引：[简体, 繁体, 英文]。
        /// 没命中的 key 会原样返回。
        ///
        /// 说明：因为魔改新增的窗口是用中文写的，所以为了支持英文界面，
        /// 这些条目额外提供了英文译文（第三个元素）。
        /// </summary>
        private static readonly Dictionary<string, string[]> _Table =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                // ---------- 工具栏 / 主界面 ----------
                { "Commit Changes", new[] { "提交更改", "提交變更", "Commit Changes" } },
                { "Store achievements and statistics for active game.", new[] { "把当前游戏的成就与统计数据写入 Steam。", "把目前遊戲的成就與統計資料寫入 Steam。", "Store achievements and statistics for active game." } },
                { "Refresh", new[] { "刷新", "重新整理", "Refresh" } },
                { "Refresh achievements and statistics for active game.", new[] { "重新从 Steam 读取当前游戏的成就与统计。", "重新從 Steam 讀取目前遊戲的成就與統計。", "Refresh achievements and statistics for active game." } },
                { "Reset", new[] { "重置", "重設", "Reset" } },
                { "Reset achievements and/or statistics for active game.", new[] { "重置当前游戏的成就和/或统计数据。", "重設目前遊戲的成就和/或統計資料。", "Reset achievements and/or statistics for active game." } },

                // ---------- 成就列表 ----------
                { "Achievements", new[] { "成就", "成就", "Achievements" } },
                { "Statistics", new[] { "统计", "統計", "Statistics" } },
                { "Name", new[] { "名称", "名稱", "Name" } },
                { "Description", new[] { "描述", "描述", "Description" } },
                { "Unlock Time", new[] { "解锁时间", "解鎖時間", "Unlock Time" } },
                { "Unlock Rate", new[] { "全球解锁率", "全球解鎖率", "Unlock Rate" } },
                { "Lock All", new[] { "全部锁定", "全部鎖定", "Lock All" } },
                { "Lock all achievements.", new[] { "取消勾选所有成就。", "取消勾選所有成就。", "Lock all achievements." } },
                { "Invert All", new[] { "反向勾选", "反向勾選", "Invert All" } },
                { "Invert all achievements.", new[] { "把勾选状态全部取反。", "把勾選狀態全部反轉。", "Invert all achievements." } },
                { "Unlock All", new[] { "全部勾选", "全部勾選", "Unlock All" } },
                { "Unlock all achievements.", new[] { "勾选所有成就。", "勾選所有成就。", "Unlock all achievements." } },
                { "Show only", new[] { "只显示", "只顯示", "Show only" } },
                { "locked", new[] { "未解锁", "未解鎖", "locked" } },
                { "unlocked", new[] { "已解锁", "已解鎖", "unlocked" } },
                { "Filter", new[] { "过滤", "篩選", "Filter" } },
                { "Type at least 3 characters that must appear in the name or description", new[] { "输入关键字（会匹配成就名称或描述）", "輸入關鍵字（會比對成就名稱或描述）", "Type at least 3 characters that must appear in the name or description" } },
                { "Download status", new[] { "下载状态", "下載狀態", "Download status" } },

                // ---------- 统计页 ----------
                { "I understand by modifying the values of stats, I may screw things up and can't blame anyone but myself.", new[] { "我明白修改统计数据可能搞坏存档，后果自负。", "我明白修改統計資料可能搞壞存檔，後果自負。", "I understand by modifying the values of stats, I may screw things up and can't blame anyone but myself." } },

                // ---------- 魔改新增按钮 ----------
                { "解锁率排序", new[] { "解锁率排序", "解鎖率排序", "Sort by Rate" } },
                { "按率刷完", new[] { "按率刷完", "按率刷完", "Unlock by Rate" } },
                { "按天铺开", new[] { "按天铺开", "按天鋪開", "Spread over Days" } },
                { "下载图标", new[] { "下载图标", "下載圖示", "Load Icons" } },
                { "语言", new[] { "语言", "語言", "Language" } },

                // ---------- 窗口标题 ----------
                { "Steam Achievement Manager 7.0.41 (personal mod)", new[] { "Steam 成就管理器 7.0.41（个人魔改版）", "Steam 成就管理器 7.0.41（個人魔改版）", "Steam Achievement Manager 7.0.41 (personal mod)" } },
                { "Steam Achievement Manager 7.0 | Pick a game... Any game...", new[] { "Steam 成就管理器 7.0 | 挑一个游戏…", "Steam 成就管理器 7.0 | 挑一個遊戲…", "Steam Achievement Manager 7.0 | Pick a game... Any game..." } },

                // ---------- 游戏选择器 ----------
                { "Refresh Games", new[] { "刷新游戏列表", "重新整理遊戲清單", "Refresh Games" } },
                { "Add Game", new[] { "添加游戏", "新增遊戲", "Add Game" } },
                { "Game filtering", new[] { "游戏过滤", "遊戲篩選", "Game filtering" } },
                { "Show &games", new[] { "显示游戏", "顯示遊戲", "Show &games" } },
                { "Show &demos", new[] { "显示试玩版", "顯示試玩版", "Show &demos" } },
                { "Show &mods", new[] { "显示模组", "顯示模組", "Show &mods" } },
                { "Show &junk", new[] { "显示杂项", "顯示雜項", "Show &junk" } },

                // ---------- 通用 ----------
                { "Error", new[] { "错误", "錯誤", "Error" } },
                { "Warning", new[] { "警告", "警告", "Warning" } },
                { "Question", new[] { "询问", "詢問", "Question" } },
                { "Information", new[] { "提示", "提示", "Information" } },
                { "Failed.", new[] { "操作失败。", "操作失敗。", "Failed." } },

                // ---------- 上游的错误与提示 ----------
                { "Error while retrieving stats:", new[] { "读取统计数据时出错：", "讀取統計資料時發生錯誤：", "Error while retrieving stats:" } },
                { "Failed to load schema.", new[] { "无法读取成就结构文件。", "無法讀取成就結構檔。", "Failed to load schema." } },
                { "Error when handling achievements retrieval.", new[] { "处理成就数据时出错。", "處理成就資料時發生錯誤。", "Error when handling achievements retrieval." } },
                { "Error when handling stats retrieval.", new[] { "处理统计数据时出错。", "處理統計資料時發生錯誤。", "Error when handling stats retrieval." } },
                { "Error when handling achievements retrieval:\n", new[] { "处理成就数据时出错：\n", "處理成就資料時發生錯誤：\n", "Error when handling achievements retrieval:\n" } },
                { "Error when handling stats retrieval:\n", new[] { "处理统计数据时出错：\n", "處理統計資料時發生錯誤：\n", "Error when handling stats retrieval:\n" } },
                { "An error occurred while storing, aborting.", new[] { "写入 Steam 时出错，已中止。", "寫入 Steam 時發生錯誤，已中止。", "An error occurred while storing, aborting." } },
                { "An error occurred while setting the state for {0}, aborting store.", new[] { "设置成就 {0} 的状态时出错，已中止写入。", "設定成就 {0} 的狀態時發生錯誤，已中止寫入。", "An error occurred while setting the state for {0}, aborting store." } },
                { "An error occurred while setting the value for {0}, aborting store.", new[] { "设置统计项 {0} 的数值时出错，已中止写入。", "設定統計項 {0} 的數值時發生錯誤，已中止寫入。", "An error occurred while setting the value for {0}, aborting store." } },
                { "Stored {0} achievements and {1} statistics.", new[] { "已写入 {0} 个成就和 {1} 项统计。", "已寫入 {0} 個成就和 {1} 項統計。", "Stored {0} achievements and {1} statistics." } },
                { "Retrieving stat information...", new[] { "正在读取统计数据…", "正在讀取統計資料…", "Retrieving stat information..." } },
                { "Retrieved {0} achievements and {1} statistics.", new[] { "已读取 {0} 个成就和 {1} 项统计。", "已讀取 {0} 個成就和 {1} 項統計。", "Retrieved {0} achievements and {1} statistics." } },
                { "Downloading {0} icons...", new[] { "正在下载 {0} 个图标…", "正在下載 {0} 個圖示…", "Downloading {0} icons..." } },
                { "Are you absolutely sure you want to reset stats?", new[] { "确定要重置统计数据吗？", "確定要重設統計資料嗎？", "Are you absolutely sure you want to reset stats?" } },
                { "Do you want to reset achievements too?", new[] { "要连成就一起重置吗？", "要連成就一起重設嗎？", "Do you want to reset achievements too?" } },
                { "Really really sure?", new[] { "真的要重置吗？（此操作会直接写入 Steam）", "真的要重設嗎？（此操作會直接寫入 Steam）", "Really really sure?" } },
                { "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager.", new[] { "这个成就是受保护的，SAM 无法修改它。", "這個成就是受保護的，SAM 無法修改它。", "Sorry, but this is a protected achievement and cannot be managed with Steam Achievement Manager." } },
                { "This tool declines to being run from the Steam directory.", new[] { "请不要把本程序放在 Steam 安装目录里运行。", "請不要把本程式放在 Steam 安裝目錄裡執行。", "This tool declines to being run from the Steam directory." } },
                { "You've caused an exceptional error!", new[] { "发生了异常错误。", "發生了異常錯誤。", "You've caused an exceptional error!" } },
                { "Steam is not running. Please start Steam then run this tool again.", new[] { "Steam 没有运行，请先启动 Steam 再试。", "Steam 沒有執行，請先啟動 Steam 再試。", "Steam is not running. Please start Steam then run this tool again." } },
                { "Steam is not running. Please start Steam then run this tool again.\n\n", new[] { "Steam 没有运行，请先启动 Steam 再试。\n\n", "Steam 沒有執行，請先啟動 Steam 再試。\n\n", "Steam is not running. Please start Steam then run this tool again.\n\n" } },
                { "If you have the game through Family Share, the game may be locked due to\nthe Family Share account actively playing a game.\n\n", new[] { "如果这个游戏是家庭共享来的，可能因为共享账号正在玩其它游戏而被锁定。\n\n", "如果這個遊戲是家庭共享來的，可能因為共享帳號正在玩其他遊戲而被鎖定。\n\n", "If you have the game through Family Share, the game may be locked due to\nthe Family Share account actively playing a game.\n\n" } },
                { "Could not parse application ID from command line argument.", new[] { "无法从命令行参数解析出应用 ID。", "無法從命令列參數解析出應用 ID。", "Could not parse application ID from command line argument." } },
                { "Failed to start SAM.Game.exe.", new[] { "无法启动 SAM.Game.exe。", "無法啟動 SAM.Game.exe。", "Failed to start SAM.Game.exe." } },
                { "Please enter a valid game ID.", new[] { "请输入有效的游戏 ID。", "請輸入有效的遊戲 ID。", "Please enter a valid game ID." } },
                { "You don't own that game.", new[] { "你的账号里没有这个游戏。", "你的帳號裡沒有這個遊戲。", "You don't own that game." } },

                // ---------- 魔改窗口：配置界面 ----------
                { "按解锁率节奏解锁", new[] { "按解锁率节奏解锁", "按解鎖率節奏解鎖", "Unlock by Rate (paced)" } },
                { "解锁顺序：", new[] { "解锁顺序：", "解鎖順序：", "Order:" } },
                { "只处理解锁率区间：", new[] { "只处理解锁率区间：", "只處理解鎖率區間：", "Global rate range:" } },
                { "%（100 = 不限制）", new[] { "%（100 = 不限制）", "%（100 = 不限制）", "% (100 = no limit)" } },
                { "间隔节奏：", new[] { "间隔节奏：", "間隔節奏：", "Interval style:" } },
                { "两个成就之间空闲：", new[] { "两个成就之间空闲：", "兩個成就之間空閒：", "Idle gap between achievements:" } },
                { "秒（随机取值）", new[] { "秒（随机取值）", "秒（隨機取值）", "seconds (picked randomly)" } },
                { "开始前先等：", new[] { "开始前先等：", "開始前先等：", "Wait before starting:" } },
                { "秒（0 = 立即开始）", new[] { "秒（0 = 立即开始）", "秒（0 = 立即開始）", "seconds (0 = start now)" } },
                { "跳过已经解锁的成就（推荐）", new[] { "跳过已经解锁的成就（推荐）", "跳過已經解鎖的成就（推薦）", "Skip achievements that are already unlocked (recommended)" } },
                { "成就", new[] { "成就", "成就", "Achievement" } },
                { "距开始", new[] { "距开始", "距開始", "From start" } },
                { "距上一个", new[] { "距上一个", "距上一個", "Gap" } },
                { "开始按节奏解锁", new[] { "开始按节奏解锁", "開始按節奏解鎖", "Start paced unlocking" } },
                { "取消", new[] { "取消", "取消", "Cancel" } },
                { "全球解锁率 从高到低（先拿大众成就）", new[] { "全球解锁率 从高到低（先拿大众成就）", "全球解鎖率 從高到低（先拿大眾成就）", "Global rate, highest first (common achievements first)" } },
                { "全球解锁率 从低到高（先拿稀有成就）", new[] { "全球解锁率 从低到高（先拿稀有成就）", "全球解鎖率 從低到高（先拿稀有成就）", "Global rate, lowest first (rare achievements first)" } },
                { "完全随机顺序", new[] { "完全随机顺序", "完全隨機順序", "Fully random order" } },
                { "均匀随机（min～max 之间随机）", new[] { "均匀随机（min～max 之间随机）", "均勻隨機（min～max 之間隨機）", "Uniform random (between min and max)" } },
                { "真人节奏（多为短间隔，偶尔长时间挂机）", new[] { "真人节奏（多为短间隔，偶尔长时间挂机）", "真人節奏（多為短間隔，偶爾長時間掛機）", "Human-like (mostly short gaps, occasional long break)" } },

                // ---------- 魔改窗口：进度 ----------
                { "按解锁率节奏解锁中", new[] { "按解锁率节奏解锁中", "按解鎖率節奏解鎖中", "Paced unlocking in progress" } },
                { "按解锁率节奏解锁 — 已结束", new[] { "按解锁率节奏解锁 — 已结束", "按解鎖率節奏解鎖 — 已結束", "Paced unlocking — finished" } },
                { "停止", new[] { "停止", "停止", "Stop" } },
                { "关闭", new[] { "关闭", "關閉", "Close" } },
                { "正在停止…", new[] { "正在停止…", "正在停止…", "Stopping..." } },
                { "已解锁", new[] { "已解锁", "已解鎖", "Unlocked" } },
                { "等待中", new[] { "等待中", "等待中", "Waiting" } },
                { "下一个", new[] { "下一个", "下一個", "Next" } },
                { "状态", new[] { "状态", "狀態", "Status" } },
                { "计划时间", new[] { "计划时间", "計畫時間", "Scheduled" } },

                // ---------- 魔改窗口：铺开 ----------
                { "长时间铺开（按天解锁）", new[] { "长时间铺开（按天解锁）", "長時間鋪開（按天解鎖）", "Spread over Days" } },
                { "开始日期：", new[] { "开始日期：", "開始日期：", "Start date:" } },
                { "结束日期：", new[] { "结束日期：", "結束日期：", "End date:" } },
                { "每天解锁：", new[] { "每天解锁：", "每天解鎖：", "Per day:" } },
                { "个（每天在这个区间里随机）", new[] { "个（每天在这个区间里随机）", "個（每天在這個區間裡隨機）", "achievements (randomized within this range each day)" } },
                { "每天的时间窗口：", new[] { "每天的时间窗口：", "每天的時間窗口：", "Daily time window:" } },
                { "点 ～", new[] { "点 ～", "點 ～", "h ～" } },
                { "点（成就只会落在这个时间段内）", new[] { "点（成就只会落在这个时间段内）", "點（成就只會落在這個時間段內）", "h (achievements only unlock inside this window)" } },
                { "错过时间窗口时立刻补做（不勾就是跳过那天，第二天继续）", new[] { "错过时间窗口时立刻补做（不勾就是跳过那天，第二天继续）", "錯過時間窗口時立刻補做（不勾就是跳過那天，第二天繼續）", "Catch up immediately if the window was missed (unchecked: skip that day)" } },
                { "提示：想尽量像真人，建议窗口设在你平时真会玩游戏的时段，每天 3~8 个。", new[] { "提示：想尽量像真人，建议窗口设在你平时真会玩游戏的时段，每天 3~8 个。", "提示：想盡量像真人，建議窗口設在你平時真會玩遊戲的時段，每天 3~8 個。", "Tip: to look human, use the hours you actually play and 3-8 achievements per day." } },
                { "开始铺开", new[] { "开始铺开", "開始鋪開", "Start spreading" } },
                { "日期", new[] { "日期", "日期", "Date" } },
                { "当天数量", new[] { "当天数量", "當天數量", "Count" } },
                { "第一个时刻", new[] { "第一个时刻", "第一個時刻", "First" } },
                { "最后一个时刻", new[] { "最后一个时刻", "最後一個時刻", "Last" } },
                { "当天累计", new[] { "当天累计", "當天累計", "Cumulative" } },

                // ---------- 魔改文本 ----------
                { "已经有一个按节奏解锁的任务在运行了。", new[] { "已经有一个按节奏解锁的任务在运行了。", "已經有一個按節奏解鎖的任務在執行。", "A paced unlocking session is already running." } },
                { "已经有一个解锁任务在运行了。", new[] { "已经有一个解锁任务在运行了。", "已經有一個解鎖任務在執行。", "An unlocking task is already running." } },
                { "成就数据还没有加载完，请稍等一下或者点一下 Refresh。", new[] { "成就数据还没有加载完，请稍等一下或者点一下「刷新」。", "成就資料還沒載入完成，請稍等一下或點一下「重新整理」。", "Achievement data is still loading. Please wait a moment or click Refresh." } },
                { "没有可解锁的成就", new[] { "没有可解锁的成就", "沒有可解鎖的成就", "Nothing left to unlock" } },
                { "按当前条件没有需要解锁的成就。\n\n", new[] { "按当前条件没有需要解锁的成就。\n\n", "依照目前條件沒有需要解鎖的成就。\n\n", "No achievements match the current settings.\n\n" } },
                { "缺少全球解锁率数据", new[] { "缺少全球解锁率数据", "缺少全球解鎖率資料", "No global unlock rate data" } },
                { "发现已有计划", new[] { "发现已有计划", "發現已有計畫", "Existing plan found" } },
                { "参数不对", new[] { "参数不对", "參數不對", "Invalid settings" } },
                { "提示", new[] { "提示", "提示", "Notice" } },
                { "这个游戏已经没有未解锁的成就了。\n\n", new[] { "这个游戏已经没有未解锁的成就了。\n\n", "這個遊戲已經沒有未解鎖的成就了。\n\n", "There is nothing left to unlock in this game.\n\n" } },
                { "现在没有任何成就的全球解锁率数据（待处理 {0} 个）。\n\n", new[] { "现在没有任何成就的全球解锁率数据（待处理 {0} 个）。\n\n", "現在沒有任何成就的全球解鎖率資料（待處理 {0} 個）。\n\n", "No global unlock rate data is available ({0} achievements pending).\n\n" } },
                { "已有计划已失效，将重新排一份：\n", new[] { "已有计划已失效，将重新排一份：\n", "已有計畫已失效，將重新排一份：\n", "The existing plan is no longer valid; a new one will be created:\n" } },
                { "计划参数有问题：", new[] { "计划参数有问题：", "計畫參數有問題：", "Invalid plan settings: " } },
                { "长时间铺开出错了：{0}\n\n计划已保存，下次打开可以继续。", new[] { "长时间铺开出错了：{0}\n\n计划已保存，下次打开可以继续。", "長時間鋪開出錯了：{0}\n\n計畫已儲存，下次開啟可以繼續。", "Spread mode failed: {0}\n\nThe plan has been saved; it will resume next time." } },
                { "长时间铺开", new[] { "长时间铺开", "長時間鋪開", "Spread over Days" } },
                { "长时间铺开已暂停，进度已保存。下次打开会自动接着跑。", new[] { "长时间铺开已暂停，进度已保存。下次打开会自动接着跑。", "長時間鋪開已暫停，進度已儲存。下次開啟會自動接著執行。", "Spread mode paused; progress saved and will resume next time." } },
                { "图标下载：开（点 Refresh 或重开游戏后生效）", new[] { "图标下载：开（点「刷新」或重开游戏后生效）", "圖示下載：開（點「重新整理」或重開遊戲後生效）", "Icon download: ON (takes effect after Refresh or reopening the game)" } },
                { "图标下载：关（点 Refresh 或重开游戏后生效）", new[] { "图标下载：关（点「刷新」或重开游戏后生效）", "圖示下載：關（點「重新整理」或重開遊戲後生效）", "Icon download: OFF (takes effect after Refresh or reopening the game)" } },
                { "是否继续使用已有进度？（选「否」会重新排一份新计划）", new[] { "是否继续使用已有进度？（选「否」会重新排一份新计划）", "是否繼續使用已有進度？（選「否」會重新排一份新計畫）", "Continue with the existing progress? (No = create a new plan)" } },
                { "请选择界面语言", new[] { "请选择界面语言", "請選擇介面語言", "Choose your interface language" } },
                { "Choose the interface language.\n成就的名称与描述会跟随 Steam 客户端语言，不受这里影响。", new[] { "选择界面语言。\n成就的名称与描述跟随 Steam 客户端语言，不受这里影响。", "選擇介面語言。\n成就的名稱與描述跟隨 Steam 客戶端語言，不受這裡影響。", "Choose the interface language.\nAchievement names and descriptions follow the Steam client language instead." } },
                { "确定 / OK", new[] { "确定", "確定", "OK" } },
                { "取消 / Cancel", new[] { "取消", "取消", "Cancel" } },
                { "界面语言 / Language", new[] { "界面语言", "介面語言", "Interface Language" } },
                { "切换界面语言（会记住选择，下次直接使用）", new[] { "切换界面语言（会记住选择，下次直接使用）", "切換介面語言（會記住選擇，下次直接使用）", "Switch the interface language (the choice is remembered)" } },
                { "是否从 Steam CDN 下载成就图标。图标只存在于内存中，关闭程序即释放，不占用任何磁盘空间；但每个图标需要一次网络请求，成就很多时（例如上千个）会明显拖慢加载。改动后需要点 Refresh 或重新打开游戏生效。", new[] { "是否从 Steam CDN 下载成就图标。图标只存在于内存中，关闭程序即释放，不占用任何磁盘空间；但每个图标需要一次网络请求，成就很多时（例如上千个）会明显拖慢加载。改动后需要点「刷新」或重新打开游戏生效。", "是否從 Steam CDN 下載成就圖示。圖示只存在於記憶體中，關閉程式即釋放，不佔用任何磁碟空間；但每個圖示需要一次網路請求，成就很多時（例如上千個）會明顯拖慢載入。改動後需要點「重新整理」或重新開啟遊戲生效。", "Whether to download achievement icons from the Steam CDN. Icons live in memory only and are dropped when the app exits, so they never touch your disk; but each icon costs one network request, which is slow for games with many achievements. Takes effect after Refresh or reopening the game." } },
                { "按 Steam 全球解锁率排序成就列表（再点一次反向排序）。", new[] { "按 Steam 全球解锁率排序成就列表（再点一次反向排序）。", "依 Steam 全球解鎖率排序成就清單（再點一次反向排序）。", "Sort the achievement list by Steam global unlock rate (click again to reverse)." } },
                { "按全球解锁率顺序依次解锁成就，并在每个成就之间插入随机的空闲间隔，模拟真人游玩。", new[] { "按全球解锁率顺序依次解锁成就，并在每个成就之间插入随机的空闲间隔，模拟真人游玩。", "依全球解鎖率順序依次解鎖成就，並在每個成就之間插入隨機的空閒間隔，模擬真人遊玩。", "Unlock achievements one by one in global unlock-rate order, with randomized idle gaps in between to look like a real player." } },
                { "长时间铺开：把成就分散到很多天里，每天按配额到点解锁（解锁时间由 Steam 真实记录）。", new[] { "长时间铺开：把成就分散到很多天里，每天按配额到点解锁（解锁时间由 Steam 真实记录）。", "長時間鋪開：把成就分散到很多天裡，每天依配額到點解鎖（解鎖時間由 Steam 真實記錄）。", "Spread over days: distribute achievements across many days, unlocking on schedule each day (timestamps are recorded by Steam for real)." } },
            };

        /// <summary>
        /// 自动探测界面语言，优先级：Steam 客户端语言 &gt; 系统语言 &gt; 默认（英文）。
        /// Steam 语言直接读注册表，不需要 Steam 客户端处于运行状态。
        /// </summary>
        public static string DetectLanguage()
        {
            var steam = DetectSteamLanguage();
            if (string.IsNullOrEmpty(steam) == false)
            {
                return steam;
            }

            return DetectSystemLanguage();
        }

        /// <summary>探测语言来自哪里，用于在选择窗口里提示用户。</summary>
        public static string DescribeDetection()
        {
            if (string.IsNullOrEmpty(DetectSteamLanguage()) == false)
            {
                return "steam";
            }

            return "system";
        }

        /// <summary>
        /// 读取 Steam 客户端语言（HKCU\Software\Valve\Steam 的 Language 值）。
        /// Steam 用的语言名是 schinese / tchinese / english 这种，需要映射一下。
        /// 读取失败时返回 null。
        /// </summary>
        public static string DetectSteamLanguage()
        {
            try
            {
                object raw = null;

                try
                {
                    raw = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Valve\Steam", "Language", null);
                }
                catch (Exception)
                {
                }

                if (raw == null)
                {
                    try
                    {
                        raw = Microsoft.Win32.Registry.GetValue(
                            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "Language", null);
                    }
                    catch (Exception)
                    {
                    }
                }

                var name = raw as string;
                if (string.IsNullOrEmpty(name) == true)
                {
                    return null;
                }

                var lower = name.Trim().ToLowerInvariant();
                if (lower.Contains("tchinese") == true)
                {
                    return "zh-Hant";
                }

                if (lower.Contains("schinese") == true || lower == "zh" || lower == "zh-cn")
                {
                    return "zh-Hans";
                }

                // 目前只提供中英三种界面语言，其它语言一律用英文
                return DefaultLanguage;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>根据系统界面语言猜一个默认值（中文系统 → 简体中文）。</summary>
        private static string DetectSystemLanguage()
        {
            try
            {
                var name = CultureInfo.CurrentUICulture.Name;
                if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("HK", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "zh-Hant"
                        : "zh-Hans";
                }
            }
            catch (Exception)
            {
            }

            return DefaultLanguage;
        }

        public static void SetLanguage(string code)
        {
            if (string.IsNullOrEmpty(code) == true)
            {
                code = DefaultLanguage;
            }

            if (Available.Any(l => l.Code == code) == false)
            {
                code = DefaultLanguage;
            }

            _Current = code;
        }

        /// <summary>翻译一条文本；没有对应译文时返回原文。</summary>
        public static string T(string text)
        {
            if (string.IsNullOrEmpty(text) == true)
            {
                return text;
            }

            // 注意：这里**不能**因为"当前语言就是默认语言"而提前返回。
            // 因为魔改新增的窗口是用中文写的，它们在英文界面下需要查表翻成英文。
            string[] translations;
            if (_Table.TryGetValue(text, out translations) == false || translations == null)
            {
                return text;
            }

            var index = _Current == "zh-Hans" ? 0 : (_Current == "zh-Hant" ? 1 : 2);
            return index < translations.Length ? translations[index] : text;
        }

        /// <summary>带参数的翻译（内部走 string.Format）。</summary>
        public static string T(string text, params object[] args)
        {
            var format = T(text);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>
        /// 递归翻译控件树上已有的文本（Text / ToolTip / 菜单项 / 列头等）。
        /// 需要重复调用（每次改语言后），所以是幂等的：只替换能命中的 key。
        /// </summary>
        public static void ApplyTo(Control root)
        {
            if (root == null)
            {
                return;
            }

            ApplyToControl(root);
        }

        private static void ApplyToControl(Control control)
        {
            var text = control.Text;
            if (string.IsNullOrEmpty(text) == false)
            {
                var translated = T(text);
                if (translated != text)
                {
                    control.Text = translated;
                }
            }

            var toolStrip = control as ToolStrip;
            if (toolStrip != null)
            {
                foreach (ToolStripItem item in toolStrip.Items)
                {
                    ApplyToItem(item);
                }
            }

            var listView = control as ListView;
            if (listView != null)
            {
                foreach (ColumnHeader column in listView.Columns)
                {
                    var columnText = column.Text;
                    var translated = T(columnText);
                    if (translated != columnText)
                    {
                        column.Text = translated;
                    }
                }
            }

            foreach (Control child in control.Controls)
            {
                ApplyToControl(child);
            }
        }

        private static void ApplyToItem(ToolStripItem item)
        {
            if (item == null)
            {
                return;
            }

            var text = item.Text;
            if (string.IsNullOrEmpty(text) == false)
            {
                var translated = T(text);
                if (translated != text)
                {
                    item.Text = translated;
                }
            }

            var tooltip = item.ToolTipText;
            if (string.IsNullOrEmpty(tooltip) == false)
            {
                var translated = T(tooltip);
                if (translated != tooltip)
                {
                    item.ToolTipText = translated;
                }
            }

            var dropDown = item as ToolStripDropDownItem;
            if (dropDown != null)
            {
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    ApplyToItem(child);
                }
            }
        }

        /// <summary>
        /// 把界面上的译文反查回原始 key。
        /// 切换语言时必须先做这一步：否则界面文本已经是上一个语言的译文，
        /// 再拿它去查表就查不到了。
        /// </summary>
        public static void RestoreKeys(Control root)
        {
            if (root == null)
            {
                return;
            }

            RestoreKeysInControl(root);
        }

        /// <summary>把一条译文反查回原始 key；查不到时原样返回。</summary>
        public static string ToKey(string text)
        {
            if (string.IsNullOrEmpty(text) == true)
            {
                return text;
            }

            // 本身就是 key（或英文原文）的情况：直接命中
            if (_Table.ContainsKey(text) == true)
            {
                return text;
            }

            foreach (var pair in _Table)
            {
                foreach (var translation in pair.Value)
                {
                    if (translation == text)
                    {
                        return pair.Key;
                    }
                }
            }

            return text;
        }

        private static void RestoreKeysInControl(Control control)
        {
            var text = control.Text;
            if (string.IsNullOrEmpty(text) == false)
            {
                var key = ToKey(text);
                if (key != text)
                {
                    control.Text = key;
                }
            }

            var toolStrip = control as ToolStrip;
            if (toolStrip != null)
            {
                foreach (ToolStripItem item in toolStrip.Items)
                {
                    RestoreKeysInItem(item);
                }
            }

            var listView = control as ListView;
            if (listView != null)
            {
                foreach (ColumnHeader column in listView.Columns)
                {
                    var key = ToKey(column.Text);
                    if (key != column.Text)
                    {
                        column.Text = key;
                    }
                }
            }

            foreach (Control child in control.Controls)
            {
                RestoreKeysInControl(child);
            }
        }

        private static void RestoreKeysInItem(ToolStripItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = ToKey(item.Text);
            if (key != item.Text)
            {
                item.Text = key;
            }

            var tooltipKey = ToKey(item.ToolTipText);
            if (tooltipKey != item.ToolTipText)
            {
                item.ToolTipText = tooltipKey;
            }

            var dropDown = item as ToolStripDropDownItem;
            if (dropDown != null)
            {
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    RestoreKeysInItem(child);
                }
            }
        }

        // ---------- 语言选择的持久化 ----------

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sam-mod-config.txt");
        }

        /// <summary>读取上次选择的语言；没有记录时返回 null（表示需要询问用户）。</summary>
        public static string LoadSavedLanguage()
        {
            try
            {
                var path = GetConfigPath();
                if (File.Exists(path) == false)
                {
                    return null;
                }

                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("language=", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return trimmed.Substring("language=".Length).Trim();
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        public static void SaveLanguage(string code)
        {
            try
            {
                File.WriteAllText(GetConfigPath(), "language=" + code + Environment.NewLine);
            }
            catch (Exception)
            {
            }
        }
    }
}
