# SAM 7.0.41 个人魔改版 — 按 Steam 全球解锁率 + 随机空闲间隔依次解锁

> ### ⚠️ 修改版声明（Derivation Notice）
>
> **本项目是修改版（mod），并非原版软件，也不是官方项目。**
>
> - 原版项目：[gibbed/SteamAchievementManager](https://github.com/gibbed/SteamAchievementManager)
> - 原作者：Rick (rick 'at' gibbed 'dot' us)
> - 原版许可证：[zlib License](LICENSE.txt)（本仓库未作任何修改）
> - 本仓库基于原版 **7.0.41** 的源码修改而来，**相对上游的改动已在第 8.3 节逐项列出**
> - **未获**原作者背书或审核，请不要把它当成原版下载
> - 非官方个人魔改版，与原作者无关
>
> 依据 zlib 许可证第 2 条：
> `Altered source versions must be plainly marked as such, and must not be misrepresented as being
> the original software.`
> 上述清单即为该条款要求的"明确标注"。原版权声明与许可证全文在所有改动文件中均被完整保留。

---

## 1. 这个魔改加了什么

原版 SAM 只能「全部勾选 → 一起提交」，所有成就在同一秒解锁，一眼就能看出是刷的。
本次魔改加了三件事：

### ① 全球解锁率（Unlock Rate）列

- 成就列表新增第 4 列 **Unlock Rate**，显示每个成就的 **Steam 全球玩家解锁率**（就是 steamdb.info 那个数据）。
- 数据来源优先走 **Steam 客户端本地接口** `ISteamUserStats013::RequestGlobalAchievementPercentages`，
  通过 `GameAchievementData` 回调（id 1102）取回；失败时自动回退到 **Steam Web API**
  `GetGlobalAchievementPercentagesForApp`（公开接口，不需要 API Key）。
- 工具栏 **解锁率排序** 按钮（或直接点该列表头）可按解锁率 **从高到低 / 从低到高** 切换排序。

### ② 按解锁率节奏依次解锁（核心功能）

工具栏 **按率刷完** 按钮：

1. 弹出配置窗口，可设置
   - **解锁顺序**：全球解锁率从高到低（先拿大众成就）／从低到高（先拿稀有成就）／完全随机
   - **解锁率区间**：只处理某个区间的成就（例如只刷 0%~5% 的稀有成就）
   - **间隔节奏**：均匀随机，或「真人节奏」（多数短间隔 + 偶尔长时间挂机）
   - **两个成就之间的空闲时间**：min ~ max 秒，每次在这个区间内随机取值
   - **开始前先等 N 秒**
   - **跳过已经解锁的成就**（推荐勾选，避免动到你已有的解锁时间戳）
2. 配置窗口底部会给出**预览**：前 8 个成就、各自的全球解锁率、距离开始的累计时间、与上一个的间隔，
   以及「将解锁 N 个成就，预计总耗时 X」。
3. 确认后弹出进度窗口：实时进度条、下一个成就与倒计时、每个成就的状态，随时可以点 **停止**。
4. 每个成就是**单独写入 + 单独提交**（`SetAchievement` → `StoreStats`），中间插入随机空闲时间，
   所以 Steam 记录的解锁时间戳是逐个拉开、间隔随机的，符合真人游玩的节奏。

**其它细节**

- 受保护成就（`permission` 标志）会自动跳过，不会误点。
- 没有全球解锁率数据的成就不会丢，会被排在最后。
- 每个成就写入后都会**回读校验**，没生效会自动重试最多 3 次；连续失败会中止并报错。
- 解锁期间 `Refresh` / `Commit` / 列表编辑会被禁用，避免和运行中的会话打架。

### ③ 长时间铺开（按天解锁）

工具栏 **按天铺开** 按钮。用途：**让解锁时间线自然地分散到很多天**，而不是集中在几分钟内。

- 可设置 **开始日期**、**结束日期**、**每天解锁数量区间**（如 3~8 个/天）、
  **每天的时间窗口**（如 19 点~23 点，成就只会落在你平时真会玩游戏的时段）。
- 点击「开始铺开」后程序会**驻留待命**：到点就解锁一个，做完当天的配额就等第二天。
- **进度自动保存**到 `spread-<appid>.json`，随时关掉程序都行，**下次打开会自动接着跑**。
- 如果某天错过时间窗口，可选「立刻补做」或「跳过那天」。
- 配置窗口里有预览：前 12 天的日期、当天数量、首末时刻、累计数量。

> **为什么需要这个模式**：见下面第 7 节——Steam 的解锁时间是**服务端**盖章的，客户端
> 无法指定历史时间。所以想让成就看起来是在过去几个月里陆续解锁的，唯一办法就是真的花那些天。

---

## 2. 怎么用

### 2.1 先备份（强烈建议）

- 用 SAM 改动成就前，建议先关掉 Steam 云同步，或至少记下当前状态。
- 原版 7.0.41 的三个文件（exe/dll）请从上游 Releases 自行下载备份，本仓库不附带原版二进制。

### 2.2 运行

```
1) 关闭 Steam 客户端（SAM 需要独占连接，游戏也要先退出）
2) 双击 SAM.Picker.exe → 在游戏列表里搜索并双击你的游戏
3) 等成就列表装载完，确认 Unlock Rate 列出现百分比
4) 点工具栏的 【按率刷完】，配置好间隔后点「开始按节奏解锁」
```

> 直接把 `SAM.Game.exe <appid>` 也可以直接打开某个游戏（appid 是 Steam 商店链接里的数字）。
> 比如 `SAM.Game.exe 250900` 就是 The Binding of Isaac: Rebirth。

### 2.3 文件说明

| 文件 | 说明 |
| --- | --- |
| `SAM.Picker.exe` | 游戏选择器 |
| `SAM.Game.exe` | 成就管理器（魔改主要在这里） |
| `SAM.API.dll` | Steam 原生接口封装（新增了全球解锁率相关调用） |
| `System.Resources.Extensions.dll` 等 4 个 dll | 用新版 SDK 编译 .NET Framework 工程所需的运行时库，**不要删** |
| `*.exe.config` | 运行时配置 |
| uild.ps1 / un.ps1 | 一键编译 / 启动调试脚本 |

---

## 3. 源码与自行编译

### 3.1 目录

| 路径 | 说明 |
| --- | --- |
| `work\` | **完整可编译源码**（官方 7.0.41 + 本次魔改） |
| `work\Directory.Build.props` | 构建环境适配 |
| `build.ps1` | 一键编译脚本 |
| `run.ps1` | 启动调试脚本 |
| `_src\test\` | 自动化测试用的窗口操作辅助脚本 |
| `_src\tree\` | 官方 7.0.41 原始源码，未改动，方便 diff 对比 |

### 3.2 编译

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

产物落在 `work\upload\`。编译命令等价于：

```powershell
_src\dotnet\dotnet.exe build work\SAM.sln -c Release -p:Platform=x86 `
    -p:GenerateResourceUsePreserializedResources=true
```

> `GenerateResourceUsePreserializedResources=true` 是必须的：
> 原工程是 VS2019 时代的 SDK 工程，`.resx` 里有 `System.Drawing.Bitmap` 图标，
> 新版 SDK 需要这个开关 + `System.Resources.Extensions` 才能编译。

### 3.3 本次改动清单

**新增文件**

| 文件 | 作用 |
| --- | --- |
| `work\SAM.Game\PacingSession.cs` | 节奏解锁的核心：挑选/排序/随机间隔调度 + 单步写入与校验 |
| `work\SAM.Game\PacingSettingsForm.cs` | 节奏解锁配置窗口（含预览与耗时估算） |
| `work\SAM.Game\PacingScheduleForm.cs` | 运行进度窗口（进度条 / 倒计时 / 停止） |
| `work\SAM.Game\SpreadSchedule.cs` | 长时间铺开：按天排期 + 进度存档（`spread-<appid>.json`） |
| `work\SAM.Game\SpreadPlanForm.cs` | 长时间铺开配置窗口（起止日期 / 每日配额 / 时间窗口 + 预览） |
| `work\SAM.Game\GlobalAchievementPercentages.cs` | Steam Web API 回退实现（TLS1.2 + DataContract 解析） |
| `work\SAM.API\Types\GameAchievementData.cs` | 回调参数结构（name, percent 数组） |
| `work\SAM.API\Callbacks\GameAchievementData.cs` | 回调 id 1102 + 原始缓冲解析 |
| `work\Directory.Build.props` | 构建适配 |

**修改文件**

| 文件 | 改动 |
| --- | --- |
| `work\SAM.API\Interfaces\ISteamUserStats013.cs` | 只保留原布局；**故意不定义** `SetAchievementAndUnlockTime`（见下方踩坑记录） |
| `work\SAM.API\Wrappers\SteamUserStats013.cs` | 新增 `RequestGlobalAchievementPercentages` / `GetAchievementAchievedPercent` |
| `work\SAM.Game\Manager.cs` | 全球解锁率获取与显示、`_AllAchievements` 全量缓存、排序、节奏解锁会话驱动、长时间铺开调度、回调重入保护 |
| `work\SAM.Game\Manager.Designer.cs` | 新增三个工具栏按钮 + 列头点击排序 + 工具栏自动换行 |
| `work\SAM.Game\SAM.Game.csproj` | 引用 `System.Runtime.Serialization` |
| `work\SAM.Game\Stats\AchievementInfo.cs` | 新增 `GlobalPercent` 字段 |

> ⚠️ **踩坑记录 1：虚表槽位不能想当然**
> `ISteamUserStats013` 是一张函数指针虚表。曾经把 `SetAchievementAndUnlockTime` 插在
> `GetAchievementAndUnlockTime` 后面（按文档顺序），结果**后面所有槽位全部错位**，
> 调用 `RequestGlobalAchievementPercentages` 时直接 `AccessViolationException` 崩溃。
> 虚表布局必须以上游代码为准，不要按文档"推测"插入。
>
> ⚠️ **踩坑记录 2：末尾追加也不一定安全**
> 后来改成都按"Valve 追加在表尾"处理，把 `SetAchievementAndUnlockTime` 放在最后。
> 实测在当前 Steam 客户端上调用它依然 **`0xc0000005` 访问违例崩溃**（faulting module = `clr.dll`），
> 说明这个客户端根本没有在虚表里暴露该函数。**已彻底移除**，详见第 7 节。

---

## 4. 实测记录（2026-10，Steam 客户端新版）

### 4.1 「按率刷完」实测通过

The Binding of Isaac: Rebirth（appid 250900），641 个成就，间隔 1 秒：

| 项目 | 结果 |
| --- | --- |
| 实际耗时 | 约 10 分 40 秒（641 秒 + 每次写入开销） |
| 结果 | 641 / 641 全部解锁，进度窗口显示「全部完成」 |
| 顺序 | 严格按全球解锁率从高到低（84.70% → 60.20% → … → 2.60%） |
| 解锁时间戳 | 逐个拉开、各不相同，落在 1:50~1:59 的区间内 |
| 失败/重试 | 0 次 |

### 4.2 「指定历史解锁时间」实测失败（不可行）

结论见第 7 节。

---

## 5. 合规与风险说明

用之前请把这一节看完，这不是形式声明，是实际边界。

### 5.1 代码层面：合法

SAM 上游采用 **zlib 许可证**（见 `LICENSE.txt`），该许可证明确授权：

> Permission is granted to anyone to use this software for any purpose, including commercial
> applications, and to **alter** it and redistribute it freely, subject to the following restrictions.

也就是说，**修改、重新编译、再分发甚至商用都是原作者明确授权的**，唯一义务是三条：

1. 不得声称原创；
2. 改动版必须明确标注为改动版（本 README 开头已声明）；
3. 不得移除或修改许可证声明。

本魔改版使用的还是官方发布的源码（非逆向），因此这一层没有任何法律问题。

### 5.2 平台层面：违反 Steam 用户协议（SSA）

**合法 ≠ 平台允许。** Steam 用户协议明确禁止使用第三方工具或自动化手段伪造成就与统计数据。
因此需要区分两件事：

- 这**不是违法行为**：不涉及刑法、没有破解 DRM、没有侵犯他人权益——改的是自己账号里自己的数据，
  走的是 Valve 自己公开的 Steamworks API。
- 这**是违约行为**：理论上 Valve 有权对账号采取措施（警告、重置成就，极端情况封禁）。

### 5.3 实际风险分级

| 场景 | 风险 |
| --- | --- |
| 单机游戏自己刷成就 | 极低。Valve 基本不主动排查，也少见因 SAM 封号的公开案例 |
| 带 VAC / 反作弊的联机游戏（CS2、TF2 等） | **别碰**。VAC 主要针对内存篡改，但没必要去试 |
| 有第三方成就站 / 服务器验证排行榜的游戏 | 会被这些站点标记或清榜。这是站点规则，不是 Valve 处罚 |
| 拿去做「代刷成就」对外服务 | 性质变为商业作弊，风险与追责概率都上一个量级 |

另外两点值得知道：

1. **很多游戏自己的 EULA 比 SSA 写得更严**，真被追究时依据通常是游戏 EULA，而不是 Steam。
2. **成就没有经济价值、也不影响他人**，所以 Valve 缺乏排查动力——它更在意你有没有改游戏内存、碰不碰它的钱包。

### 5.4 本魔改版的特殊性

本版本用「真实全球解锁率 + 随机空闲间隔」来解锁，客观上比原版的「1 秒全解锁」更难被识别，
但**这不改变任何定性**：它在 SSA 意义上依然是「非授权手段修改成就」。技术差别只在于：
原版一眼可辨是伪造，本版本看起来像真的玩了若干小时。

**建议**：自己账号、单机游戏、自娱自乐就用；不要拿去做交易或对外宣称是手打的
（那才是真正会出问题的地方，属于信誉问题，而非合规问题）。
如果想更稳妥，操作前可以顺手关掉 Steam 云同步，避免以后重装游戏时云端覆盖本地改动。

> 以上仅为技术说明，不构成法律意见。

---

## 6. 已知限制

- 需要 **Steam 客户端正在运行且已登录**，且游戏最好先退出。
- SAM 是 **x86 / .NET Framework 4.8** 程序，无法在 macOS / Linux 原生运行。
- 全球解锁率需要联网（走 Steam 客户端时其实也依赖 Steam 的远端数据）。
- 「受保护成就」Steam 本身就是不允许第三方写入的，任何工具都改不了。
- 解锁间隔如果设成 0 秒，效果等同于原版的「全部提交」，配置窗口会提示确认。

---

## 7. 为什么不能"伪造历史解锁时间"

这是本项目**实测确认**的结论，写下来免得以后重复踩坑。

### 7.1 结论

**无法把成就的解锁时间设置成过去的某个时刻。** 想让成就显示"2024 年就解锁了"，
只有一条路：**真的在那些时间点去解锁**（也就是第 1 节的「长时间铺开」模式）。

### 7.2 依据一：原生接口不存在（实测崩溃）

`SetAchievementAndUnlockTime(name, achieved, unixTime)` 看起来正好能干这件事，
于是我把它按"Valve 追加到虚表末尾"的假设加进了 `ISteamUserStats013`，实测结果：

```
异常代码：0xc0000005            ← 访问违例
出错模块：clr.dll
.NET Runtime：由于 .NET 运行时中出现内部错误，进程终止
```

调用瞬间进程崩溃，说明**当前 Steam 客户端的虚表里并没有这个函数**，读到的是越界内存。
（补充证据：直接扫描 `steamclient.dll` 的字符串表，连 `ISteamUserStats0xx` 这类
版本化接口名都搜不到，说明该客户端并未按老式 `CreateInterface` 约定暴露 `ISteamUserStats`。）

该槽位已从代码中**彻底移除**，并在 `ISteamUserStats013.cs` / `SteamUserStats013.cs`
里留了注释警告。

### 7.3 依据二：就算函数存在，服务端也不会听

Steam 的成就数据是客户端通过 `CMsgClientStoreUserStats2` 这类消息上报给服务端的，
而这个消息里客户端**只能表达"已解锁 / 未解锁"，没有时间戳字段**。
解锁时间是 **Steam 服务端在收到上报时自己盖章**的。

所以下面这些路也都走不通：

| 思路 | 为什么不行 |
| --- | --- |
| 改 Steam 本地文件（`appcache` / `userdata`） | 解锁时间不在本地，Valve 服务器上才是权威 |
| 改注册表 | 同上 |
| 走 SteamKit2 等第三方协议库 | 协议本身没有这个字段 |
| 用 `ISteamUserStats013::SetAchievementAndUnlockTime` | 函数不存在（见 7.2，实测崩溃） |

### 7.4 那怎么办

用「长时间铺开」模式（第 1 节 ③）：程序按你设定的起止日期、每日配额和时间窗口，
**在那段时间里真的逐个解锁**。这样每一个时间戳都是 Steam 亲手盖的，
从任何角度看都和真玩家一致——因为它压根不是伪造的。

代价就是**需要等**：铺多少天就要花多少天，过去的日期补不回来。

---

## 8. 许可

### 8.1 本项目使用什么许可

上游 gibbed/SteamAchievementManager 采用 **zlib License**，这是一种**宽松许可证（permissive）**，
不是 GPL 那种传染性许可证：

| 事项 | 是否允许 / 要求 |
| --- | --- |
| 修改 | ✅ 明示允许（授权语中的 `to alter it`） |
| 再分发（含修改版） | ✅ 明示允许（`redistribute it freely`） |
| 商用 | ✅ 明示允许 |
| 闭源 / 不公开改动 | ✅ 允许（zlib 无 copyleft、无 share-alike 义务） |
| 衍生代码必须沿用 zlib | ❌ 不要求 |

**本仓库没有附加自己的许可证。** 仓库内所有代码（含本次新增的文件）统一适用上游的
zlib License，完整原文见 [`LICENSE.txt`](LICENSE.txt)。这份文件与上游**逐字节一致**
（SHA256 `E4BFF363695D6FD3CC517CCCF2821D8A0887389C49840F23A758920133A0F35C`），未作任何改动。

### 8.2 本仓库如何遵守 zlib 的三条限制

zlib 的三条限制是：

> 1. The origin of this software must not be misrepresented; you must not claim that you wrote
>    the original software. […]
> 2. **Altered source versions must be plainly marked as such**, and must not be misrepresented
>    as being the original software.
> 3. This notice may not be removed or altered from any source distribution.

对应到本仓库：

1. **来源未被歪曲**：README 顶部显著位置写明了原版项目、原作者、原版许可，
   并声明本仓库**不是**官方版本、**未获**原作者背书。
2. **改动已被明确标注**：见下方 8.3 的逐项清单。
3. **许可证声明未被移除或修改**：`LICENSE.txt` 在仓库根目录及 `SAM.API/`、`SAM.Game/`、
   `SAM.Picker/` 下共四份，全部与上游逐字节一致；每个 `.cs` 源文件顶部的
   `Copyright (c) 2024 Rick …` 及 zlib 全文均保持原样（本次新增的文件同样带有该声明）。

### 8.3 相对上游的全部改动

**新增文件（8 个）**

| 文件 | 作用 |
| --- | --- |
| `SAM.Game/PacingSession.cs` | 节奏解锁核心：排序、随机间隔调度、单步写入与回读校验 |
| `SAM.Game/PacingSettingsForm.cs` | 节奏解锁配置窗口（含预览与耗时估算） |
| `SAM.Game/PacingScheduleForm.cs` | 运行进度窗口（进度条 / 倒计时 / 停止） |
| `SAM.Game/SpreadSchedule.cs` | 长时间铺开：按天排期 + 进度存档 |
| `SAM.Game/SpreadPlanForm.cs` | 长时间铺开配置窗口（起止日期 / 每日配额 / 时间窗口） |
| `SAM.Game/GlobalAchievementPercentages.cs` | Steam Web API 回退实现 |
| `SAM.API/Types/GameAchievementData.cs` | 回调参数结构（name, percent 数组） |
| `SAM.API/Callbacks/GameAchievementData.cs` | 回调 id 1102 + 原始缓冲解析 |

另外新增了 `Directory.Build.props`（构建环境适配，非上游文件）。

**修改文件（6 个）**

| 文件 | 改动 |
| --- | --- |
| `SAM.API/Interfaces/ISteamUserStats013.cs` | 保持原虚表布局，仅加注释说明为何不定义 `SetAchievementAndUnlockTime` |
| `SAM.API/Wrappers/SteamUserStats013.cs` | 新增 `RequestGlobalAchievementPercentages` / `GetAchievementAchievedPercent` 封装 |
| `SAM.Game/Manager.cs` | 解锁率获取与显示、全量成就缓存、排序、节奏解锁与铺开模式的调度、回调重入保护 |
| `SAM.Game/Manager.Designer.cs` | 新增三个工具栏按钮 + 列头点击排序 + 工具栏自动换行 |
| `SAM.Game/SAM.Game.csproj` | 引用 `System.Runtime.Serialization`；版本号对齐 7.0.41 并标注个人魔改版 |
| `SAM.Game/Stats/AchievementInfo.cs` | 新增 `GlobalPercent` 字段 |

**上游原样的文件**：其余全部文件与上游 7.0.41 逐字节相同（可用
`_src/tree/` 中的原始源码自行 diff 验证）。

### 8.4 第三方组件

运行时随附的几个 DLL 来自 .NET 平台，均为 MIT 许可；.NET SDK 只用于构建，不随产物分发。

| 组件 | 许可 |
| --- | --- |
| `System.Resources.Extensions.dll` | MIT |
| `System.Memory.dll` / `System.Buffers.dll` / `System.Numerics.Vectors.dll` / `System.Runtime.CompilerServices.Unsafe.dll` | MIT |
| .NET SDK（仅构建期） | MIT |

> 以上仅为技术说明，不构成法律意见。
