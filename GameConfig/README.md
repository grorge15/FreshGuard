# FreshGuard 导表

## 目录位置

将整个 `GameConfig` 文件夹从 `FreshGuard/Configs/` 移动到 `FreshGuard/UnityProject/GameConfig/`，与 `Assets`、`Packages` 同级。菜单和客户端脚本使用此新位置，Excel 在 `UnityProject/GameConfig/Datas/` 中。

客户端代码和数据仍导出到同一个 Unity 工程的 `Assets/` 下。`FreshGuard/Tools/` 保持原位；只拉取 UnityProject 仓库的同事如需转表，还需要在仓库的上一级准备该工具目录。服务端脚本到 `FreshGuard/Tools/` 和 `FreshGuard/Server/` 的相对层级未变。

## 日常使用

1. 修改并保存 `Datas/` 中的 Excel。
2. 在 Unity 非运行状态点击 `TEngine > Luban > 转表`，快捷键 `Alt+X`。
3. Console 出现 `Luban export succeeded` 后，等待 Unity 编译结束。

菜单默认使用按表懒加载模板。失败时 Console 会显示 Luban 的具体错误；修正表格后重新转表。
也可以从 UnityProject 目录执行 `GameConfig/gen_code_bin_to_project_lazyload.bat`，成功退出码为 0。
`gen_code_bin_to_project.bat` 是不使用自定义懒加载模板的另一入口。

## 本机工具

- Luban：4.9.0，`Tools/Luban/Luban.dll`。
- .NET Runtime：8.0.31 Windows x64，`Tools/dotnet/dotnet.exe`。
- Windows 客户端脚本优先使用上述项目内运行时，不需要配置系统 PATH。
- 非 Windows 脚本仍依赖系统 `dotnet`；本次只验证 Windows 客户端流程。

工具来源：

- https://github.com/focus-creative-games/luban/releases/tag/v4.9.0
- https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.31/dotnet-runtime-8.0.31-win-x64.zip

换机器时，Luban 压缩包中的 `Luban.dll`、依赖和模板目录应直接位于 `Tools/Luban/`，不要多套一层 `Luban/`。
.NET ZIP 解压到 `Tools/dotnet/`。无需安装 SDK，也无需运行拉取最新源码的 `Tools/build-luban.bat`。

## 生成结果与读取

- 代码：`UnityProject/Assets/GameScripts/HotFix/GameProto/GameConfig/`。
- 数据：`UnityProject/Assets/AssetRaw/Configs/bytes/`。
- 加载器：从 `CustomTemplate/ConfigSystem.cs` 复制到 `GameProto/ConfigSystem.cs`。

业务代码在游戏启动流程完成资源初始化后读取：

```csharp
var item = ConfigSystem.Instance.Tables.TbItem.Get(10000);
var name = item.Name;
```

启动入口为 `Assets/Scenes/main.unity`。直接运行业务场景不能保证 YooAsset 已初始化。
首次访问表会同步加载该表并缓存；目前沿用项目现有加载器。
修改加载器应修改 `CustomTemplate` 中的源文件；不要手改生成代码。代码与二进制数据需要配套更新。

## 新增表

参考 `item.xlsx` 的实际格式，在 `__tables__.xlsx` 中登记表名、记录类型、输入文件等。
表格首列是 Luban 标记列，字段从第二列开始。普通数据行首列留空，避免将数据写到标记列。
基础字段使用 `##var` 和 `##type` 定义，`##group` 指定分组，`##` 行用于说明。
新增表后执行同一个转表入口即可。

## 技能与技能等级

`skill.xlsx` 保存技能基础定义：`id`、`name`、`skillType`（1主动、2被动）、`behaviorType`、`requiresTarget` 和 `maxLevel`。技能行为与执行顺序由代码实现。

`skilllevel.xlsx` 保存每一级数值：`id` 是独立记录ID，`skillId` 引用技能基础表，`level` 从1开始，`intValues` 和 `floatValues` 使用逗号分隔。运行时按 `(skillId, level)` 查询；同技能等级不可重复，必须完整配置1至 `maxLevel` 的所有等级。

1505 当前仅配置1级：`intValues=20` 为直接伤害，`floatValues=0.3,3` 为减速30%、持续3秒。后续新增等级时增加等级行并同步调整基础表的 `maxLevel`，无需修改技能行为代码。

施放请求的 `SkillLevel` 默认是1，调用方可显式指定；当前不自动绑定机器人等级。等级与数值在施放时锁定，升级或更改等级配置不会改变已经发出的投射物。

## 波次演示配置

`wave_plan.xlsx` 的 `enemySpawnId` 引用 `wave_spawn.xlsx` 的独立生成组 ID，数组以逗号分隔。
`spawnSecRange` 是正式开战后的累计秒数窗口 `[A,B)`，每组每侧生成 `enemyCount` 只，间隔为 `(B-A)/enemyCount`。

当前是用于联调的 20 波演示数据，每波 20 秒：普通波 11 只普通怪；3、6、13、16 波为 10 只普通怪加 1 只精英；10、20 波为 1 只 Boss。
双方每侧合计 200 只。`Global[48]=10` 控制每个 Cycle 波数，敌人使用所属波次对应的生命配置，跨波留场不重置。
调整节奏或敌人组合只需修改这两张表并重新导出；这些数量和时点不是正式平衡数值。
