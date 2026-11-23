# 每幕自定义加层 v0.8.3

给《杀戮尖塔 2》（Slay the Spire 2）第一、二、三幕增加额外楼层，每幕最多 +50 层；地图、羊皮纸背景、
宝箱行和房间比例都会跟着新的长度一起调整。

- **创意工坊：** https://steamcommunity.com/sharedfiles/filedetails/?id=3843992717
- **English:** [README.md](README.md)
- **完整更新日志：** [CHANGELOG.zhs.md](docs/CHANGELOG.zhs.md) / [CHANGELOG.md](docs/CHANGELOG.md)

适用版本：游戏 v0.113.1，BaseLib v3.4.8。

## 功能一览

| 功能 | 改了什么 |
|---|---|
| 额外楼层 | 每幕 0～50 层，在 BaseLib 的模组配置页里调（也可以直接改 `settings.cfg`）。 |
| 地图拉伸 | 地图保持原版行距、画布变长，房间不再挤在一起；Boss 标记跟着上移，滚动上限同步放宽。 |
| 羊皮纸背景 | 上下两个卷边完全不动，中间那段按原版宽度切条叠起来，纸变长但不会被拉变形。 |
| 宝箱行 | 每隔 N 行多一排宝箱（默认 7，0 = 关闭），从原版那排宝箱往上数，只对加过层的地图生效。 |
| 房间比例 | 问号、商店、休息点、精英各占多少百分比，剩下的算普通战斗；四个都是 0 就保持原版比例。 |
| 联机 | 房主开局时把自己的层数和房间比例发给所有人，其他人的滑块不生效。 |
| 特殊地图 | 藏宝图那一幕和金色罗盘的直线地图都会按设置层数生成，不再固定原版长度。 |
| 语言 | 配置页、模组列表名字和提示都会跟着游戏语言切换（英文 / 简体中文 / 繁体中文）。 |

## 安装

**创意工坊（最省事）：** 订阅上面的条目，再装好 [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)，启动游戏即可。

**手动安装：** 把压缩包里的 `ActFloorCustomizer` 文件夹整个复制到
`Slay the Spire 2\mods\` 下（里面有 `ActFloorCustomizer.dll` 和 `ActFloorCustomizer.json`）。
`settings.cfg` 可有可无，没有也能正常运行。每个版本的压缩包都在 [`releases/`](releases/)，
同时也挂在对应的 [GitHub 发行版](https://github.com/Icantfindone/Act-Floor-Customizer/releases) 上。

联机：每个玩家都要装好本模组和 BaseLib；一局里的数值以房主为准。

详细安装和联机说明：[联机安装与使用说明.md](docs/联机安装与使用说明.md) /
[INSTALL_AND_COOP_EN.md](docs/INSTALL_AND_COOP_EN.md)。

## 配置

在主菜单的模组配置页里打开「每幕自定义加层」，现在分成三块（都可以折叠）：

- **每幕层数**：三个楼层滑块（0～50）。
- **宝箱行**：每隔多少行加一排宝箱（0～30，默认 7）。
- **房间比例**：问号 / 商店 / 休息点 / 精英的百分比，剩下的算普通战斗。

数值会自动保存，下一局生效；也可以直接改 `settings.cfg`（`Act1ExtraFloors`、`Act2ExtraFloors`、
`Act3ExtraFloors`、`TreasureEveryRows`、`UnknownPercent`、`ShopPercent`、`RestPercent`、`ElitePercent`）。

## 已知兼容问题

- **会穷举所有路线的模组**（例如「地图增强」的路线分析）耗时随行数指数增长，46 行时约 5000 万条路线。
  开着这类模组时，每幕不要超过 15 层。
- **会画大尺寸小地图总览的模组**每帧重建缩略图，超长地图上会把地图界面拖到个位数帧率；关掉总览或降低层数即可。
- **会改写战斗池或地图结构的模组**可能冲突，请先小规模测试再正式开一局。

细节都在更新日志里。

## 这一版：0.8.3

- 现在有三个地方能点进这个仓库：创意工坊的英文页和简体中文页各有一个「Links / 相关链接」小节，
  游戏内模组列表显示的模组说明末尾也带上了 GitHub 地址。
- 每个 GitHub 发行版都附带可直接手动安装的 zip：下载、解压、把 `ActFloorCustomizer` 文件夹放进
  `Slay the Spire 2\mods\` 即可，不需要自己编译。
- 发版流程改成了 `tools/release.ps1` + `tools/publish-release.py`，见
  [workshop/README.md](workshop/README.md)。

每个版本的完整改动见 [CHANGELOG.zhs.md](docs/CHANGELOG.zhs.md)。

## 验证情况

- 离线用例 128 个，覆盖 0～50 层的地图、战斗池、确定性和存档读写；还包括对着正式程序集的
  layout transpiler 检查、配置元数据与中英文翻译、联机规则（联机时房主的层数和房间比例生效、
  单人时本地滑块生效）、房间比例（归一化、双方一致、保留原版节奏规则）。
- 拉伸后的地图、拼接的羊皮纸、宝箱行、特殊地图都在游戏里确认过；联机同步在真实的双人大厅里
  通过诊断日志确认过。
- 0.8.0 之后的房间比例和 50 层设置还没有完整跑过一局。
- 诊断日志默认关闭：放一个 `%APPDATA%\SlayTheSpire2\ActFloorCustomizer-test.txt` 才会生成
  `ActFloorCustomizer-diag.log`，删掉就停止；正常安装不写任何日志文件。

## 关于 AI 参与

这是一个非官方的玩家自制模组，与 Mega Crit 无关，也不受其支持。

代码和文档是**在 AI 辅助下写的**：最初的可用版本由 ChatGPT 完成，之后的地图拉伸、特殊地图、
房间比例和联机同步由 DeepSeek 在原有代码上继续写。功能取舍、拆包安装、游戏内实测、报错定位
和每个版本发不发，都由我（Bobojo）决定。如果你只接受纯手工编写的模组，这个不符合。

## 目录结构

- `src/ModEntry.cs` —— 设置、Harmony 初始化、`ActModel` 房间数补丁。
- `src/InGameConfig.cs` —— BaseLib 配置页、标签、每局设置快照、联机规则。
- `src/HostSync.cs` —— 房主广播层数与房间比例、大厅钩子、网络消息。
- `src/ModNameLocalization.cs` —— 让模组列表里的名字跟随游戏语言。
- `src/MapLayout.cs` —— 保持原版行距并重建羊皮纸背景。
- `src/RoomMix.cs` —— 房间比例的重新分配和节奏规则。
- `src/MapCompat.cs` —— 特殊地图（藏宝图、金色罗盘）的兼容和额外宝箱行。
- `tests/Program.cs` —— 离线测试。
- `package/` —— 可安装的 DLL、清单、配置和文档。
- `workshop/` —— 工坊元数据、页面中文文案和发布工具。

用 .NET 9 构建：`dotnet build src/ActFloorCustomizer.csproj -c Release -p:UseSharedCompilation=false`。
不要分发引用的游戏或第三方程序集。
