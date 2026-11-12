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
var item = ConfigSystem.Instance.Tables.TbItemBase.Get(10000);
var name = item.Name;
```

启动入口为 `Assets/Scenes/main.unity`。直接运行业务场景不能保证 YooAsset 已初始化。
首次访问表会同步加载该表并缓存；目前沿用项目现有加载器。
修改加载器应修改 `CustomTemplate` 中的源文件；不要手改生成代码。代码与二进制数据需要配套更新。

## 新增表

参考 `bullet.xlsx` 的实际格式，在 `__tables__.xlsx` 中登记表名、记录类型、输入文件等。旧演示表 `item.xlsx` 已移除，表索引也不再注册 `item.TbItem`；正式道具数据使用 `item_base.xlsx`。
表格首列是 Luban 标记列，字段从第二列开始。普通数据行首列留空，避免将数据写到标记列。
基础字段使用 `##var` 和 `##type` 定义，`##group` 指定分组，`##` 行用于说明。
新增表后执行同一个转表入口即可。

## 技能与技能等级

702擀面杖的1606保持普通攻击3500并携带2603，`attack_cycle/cast_skill`每5次接受后额外排队1601。1601使用`robot.roll`、`bulletId=0`，不再使用旧3601穿透弹或Damage/PierceTargetCount/KnockbackDistance；对应等级行新增`staffPrefab`、`staffSpawnGap`、`staffRollSpeed`、`staffRollDistance`、`staffWidth`、`staffThickness`、`staffAppearGrowTime`、`staffAppearSettleTime`。资源为StaffBullet，默认0.15净间隔、0.8滚速、1.2距离和宽度、0.2厚度，出现先0.2秒放大至1.1倍再0.1秒回落。Staff仅沿道路反向推动阻挡，不伤害、不吃击退承受比例，不向敌人添加Buff；相关导表使用`gen_code_bin_to_project_lazyload.bat`以保留现有Tables延迟加载和可替换表接口。

`skill.xlsx` 保存技能基础定义：`id`、`skillType`（0普通攻击、1机器人技能、2战场技能、3敌人技能）、`timelineKey`、`maxLevel`、`cooldown`、`targetRule`、`passiveBuffIds` 和 `bulletId`。`bulletId=0` 表示不使用弹丸，非零值必须引用 `bullet.xlsx`。技能执行顺序由时间轴节点驱动，Buff 行为由代码注册。

`skill_level.xlsx` 保存每一级数值：`id` 是独立记录ID，`skillId` 引用技能基础表，`level` 从1开始，`damage`、`slow` 和 `duration` 分别表示伤害、减速比例和持续秒数。运行时按 `(skillId, level)` 查询；同技能等级不可重复，必须完整配置1至 `maxLevel` 的所有等级。旧文件 `skilllevel.xlsx` 不再由表索引引用。

机器人技能新增具名等级字段：`scatterCount`、`scatterAngle`、`pierceTargetCount`、`burstCount`、`burstInterval`、`knockbackDistance`。不使用的字段填0。散射数量和穿透人数至少为1，连射次数至少为1、间隔大于0；角度范围0～360度，距离和时间必须是有限非负数。

1500～1504 为机器人技能，时间轴依次为 `robot.single`、`robot.scatter`、`robot.pierce`、`robot.burst`、`robot.impact`，1～5级每弹伤害均为20/30/40/50/60。1501散射5枚、总角度60度；1502最多穿透3人；1503连射4发、间隔0.2秒；1504击退1单位且不附带减速。

1505 保留原有5级配置：伤害20/28/40/55/77，减速比例0.3/0.3/0.4/0.4/0.4，持续3秒。源表中的动态数组公式及缓存值保留。`buff.xlsx` 中的2505提供减速行为，施法钩子调整后的 `slow` 和 `duration` 仍在施法快照中绑定命中Buff。

601冰霜炮使用1505，实际减速为配置减速比例乘以敌人的控制承受比例，持续时间不折算，最终移动速度不低于基础速度的50%。同来源覆盖数值并刷新持续时间，不同来源独立到期且同一时刻采用最强减速。

603毒素炮使用机器人技能1507，沿用 `robot.single` 与弹丸3500。`skill_level.xlsx` 新增 `poisonDamage:int`、`poisonDuration:float`、`poisonInterval:float`、`poisonMaxLayers:int`，其他技能填0。1507的1～5级直接伤害为20/30/40/50/60，每层每跳伤害为5/7/10/14/20，持续5秒、间隔1秒、目标总层数上限3。持续时间与间隔必须为有限正数，每跳伤害非负，本次各等级上限统一3。

`buff.xlsx` 的2507使用 `poison` 行为，`maxStacks=1`，模型 `tickInterval=0`；实际间隔由每层冻结的技能参数提供。每次命中新建独立中毒实例，包括同一Caster再次命中，所有来源合计最多3层。每层独立保存来源、伤害快照和计时，经过完整间隔才首次跳伤，结束时刻的Tick仍结算，因此5秒/1秒为5跳。满层时，新层完整总伤害严格高于现有最低剩余总伤害才替换，并列时替换最早加入层；拒绝不影响直接伤害。周期伤害属于 `FollowUp`，可击杀并正常奖励，但不触发攻击、命中、助力、肉鸽或其他后续效果。

机器人技能等级跟随当前机器人等级并限制在技能最大等级内。等级、目标与数值在施放时锁定，升级或更改等级配置不会改变已经发出的投射物。

## 弹丸与受击半径

弹丸关系为 `Robot.skillId → Skill.bulletId → Bullet`。`robot.xlsx` 已移除 `bulletFlightTime`、`bulletArcHeight`、`bulletPrefab`，飞行和资源配置统一放在 `bullet.xlsx`。

`bullet.xlsx` 在表索引注册为 `bullet.TbBullet`，字段包括 `id`、`prefab`、`flightType`、`trackTarget`、`hitMode`、`speed`、`flightTime`、`arcHeight`、`hitRadius`。飞行枚举为 `bullet.BulletFlightType`（Straight=0、Parabolic=1），命中枚举为 `bullet.BulletHitMode`（LockedTarget=0、FirstEnemy=1、PierceEnemies=2）。

当前支持追踪抛物线指定目标，以及不追踪的直线首次命中/穿透。直线速度必须大于0，抛物线飞行时间必须大于0。3500使用 `RobotBullet`、0.6秒和1单位弧高；3501/3502分别供散射/穿透使用，速度10单位/秒、命中半径0.1单位。未使用的字段填0。

1500/1503/1504引用3500，1501引用3501，1502引用3502；其余机器人原飞行参数相同，共用3500，保留原有追踪抛物线行为。`enemy.xlsx` 的 `hitRadius` 是受击圆半径，当前三种演示敌人均为0.3单位；碰撞半径按敌人与弹丸半径之和计算。

## 波次演示配置

`wave_plan.xlsx` 的 `enemySpawnId` 引用 `wave_spawn.xlsx` 的独立生成组 ID，数组以逗号分隔。
`spawnSecRange` 是正式开战后的累计秒数窗口 `[A,B)`，每组每侧生成 `enemyCount` 只，间隔为 `(B-A)/enemyCount`。

当前是用于联调的 20 波演示数据，每波 20 秒：普通波 11 只普通怪；3、6、13、16 波为 10 只普通怪加 1 只精英；10、20 波为 1 只 Boss。
双方每侧合计 200 只。`Global[48]=10` 控制每个 Cycle 的波数，仅用于阶段划分。敌人基础生命使用 `enemy.xlsx` 的 `hp`，保留普通怪100、精英350、Boss1800。
`Global[53]` 为 `EnemyHpGrowthPerWave`，当前0.15。生成生命值为 `hp × [1 + (所属波次 - 1) × Global[53]]`，按四舍五入取整（0.5向上）；首波倍率1，整场线性累计，跨 Cycle 不重置，0表示关闭成长。参数必须为有限非负数，所有生成组的生命值会在准备阶段校验，不能超过整数范围。
双方敌人使用相同规则，仅在生成时计算。跨波留场的敌人不重置、不追补当前或最大生命值；前波生成组延迟出兵时，仍使用其所属波次，而非战场当前波次。
调整节奏或敌人组合只需修改这两张表并重新导出；这些数量和时点不是正式平衡数值。

最终波清空胜利依据本局所有已配置生成组完成出兵、双方敌人处理完毕和死亡表现结束，且玩家基地仍存活；不固定第20波，也不等待出兵窗口的结束秒数。
敌人不再使用分阶段的两套生命值，波次方案不受两个 Cycle 的限制。`Global[48]` 仍须为正整数，但修改阶段长度不会影响生命成长。调整 `hp` 或 `Global[53]` 后重新转表并开启新局，成长参数按局快照。
