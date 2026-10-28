# Global 参数逐项核对

核对日期：2026-10-27。覆盖 global_config.xlsx 的全部52个ID，保留所有ID、数值和数组。

结果：原有25项已接入，本次补接10项，1项受限接入（仅三槽），2项已迁移停用，3项功能未接入，11项空白预留。

生产入口：BattleShopSceneController开局读取BallGlobalRules；ShopRules.FromTables读取经济配置；GameApp读取体力配置。仅出现在测试默认构造中的常量不等于生产写死值。

主塔血量以tower.xlsx.maxHp为唯一入口（当前5）。Global1和8已停用，不覆盖Tower/Robot表。Global23当前受三槽UI限制。Global43、44、50尚无完整功能，不伪造为已生效。

| ID | 参数 | 原状态 | 当前状态 | 读取位置与说明 |
| --- | --- | --- | --- | --- |
| 1 | TargetHp | 无调用；主塔实际固定5点血 | 已停用 | BattleShopSceneController.Towers.PrepareTowers → TowerBattleSession；本次修复 maxHp 被固定5覆盖。当前主塔血量请修改 tower.xlsx 的 maxHp。Global 此行不生效。 |
| 2 | NormalSpeed | 使用Prefab序列化speed，未读取Global | 本次接入 | BallGlobalRules → PhysicalCollisionBall.Speed / EffectiveSpeed。运行时再乘地图缩放和拖拽减速。 |
| 3 | RageSpeed | 暴走不切换速度，仍使用Prefab speed | 本次接入 | BallGlobalRules → PhysicalCollisionBall.Speed / SetBerserk。主球和额外球均使用此速度，暴走时不受拖拽减速。 |
| 4 | NormalBounceAngleRange | 已读取 | 已接入 | PhysicalCollisionBall.Bounce → BallBounceMath.Resolve。不是初始发射角。 |
| 5 | RageBounceAngleRange | 已读取 | 已接入 | PhysicalCollisionBall.Bounce → BallBounceMath.Resolve。仅对仍会反弹的碰撞体生效。 |
| 6 | NormalAntiLoopProfile | 已读取 | 已接入 | PhysicalCollisionBall.Bounce / ValidateConfig。用于防止沿水平或垂直方向循环反弹。 |
| 7 | RageAntiLoopProfile | 已读取 | 已接入 | PhysicalCollisionBall.Bounce / ValidateConfig。不是异常重发计时。 |
| 8 | RobotTriggerInterval | 无调用；使用Robot.MinTriggerInterval（目前0.2秒），非写死常量 | 已停用 | RobotCombatController.TryFire → RobotEntity.TryConsumeAttackTrigger。当前请修改 robot.xlsx 的 minTriggerInterval，各机器人独立配置。 |
| 9 | GlassTriggerInterval | 已读取 | 已接入 | GlassBoardController.PrepareAsync → GlassEntity.ApplyCollision。同一玻璃实体共享冷却，暴走球不损伤玻璃。 |
| 10 | ComboTriggerCount | PhysicalCollisionBall写死50 | 本次接入 | BallGlobalRules → PhysicalCollisionBall.ProcessRobotCombo。不再固定50次。玻璃、墙壁不计Combo。 |
| 11 | Reserved11 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 12 | Reserved12 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 13 | Reserved13 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 14 | Reserved14 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 15 | RageDurationSec | 已读取 | 已接入 | PlacementBoardsBootstrap.BindBattle / AdvanceBerserk。暂停战斗期间不消耗此时间。 |
| 16 | RageStayInterval | 每次接触只请求一次，无驻留重试 | 本次接入 | PhysicalCollisionBall.AdvanceContactTime / ProcessRobotCombo。仍受机器人自身冷却、目标及子弹就绪条件限制，不增加Combo。 |
| 17 | DragSpeedRate | 已读取 | 已接入 | PhysicalCollisionBall.ReadDraggingMultiplier；RobotDragController / OpponentAiController。无效倍率按1处理，暴走球不减速。 |
| 18 | LowSpeedThreshold | 无读取方、无低速恢复逻辑 | 本次接入 | BallGlobalRules → PhysicalCollisionBall.FixedUpdate。与实际物理速度比较并按地图缩放；设0关闭低速判定。 |
| 19 | LowSpeedDuration | 无读取方 | 本次接入 | PhysicalCollisionBall.FixedUpdate → LaunchAt。回到该球本次发射点，按Global21重新抽角。暂停不计时。 |
| 20 | NoInteractionDuration | 仅累计SecondsSinceEffectiveInteraction，未执行恢复；机器人未重置计时 | 本次接入 | PhysicalCollisionBall.FixedUpdate / ProcessRobotCombo / Bounce。墙壁反弹不重置计时，暂停不计时。 |
| 21 | LaunchDirectionRange | 开局写死20至70度；额外球写死0至360度 | 本次接入 | BallGlobalRules → PhysicalCollisionBall.SampleLaunchAngle → PlacementBoardsBootstrap.LaunchBoth。0向右、90向上。开局双方共用抽角，额外球及异常重发独立抽角。value不用填。 |
| 22 | BattleInitialSP | FromTables覆盖构造默认20，正常生产路径已生效 | 已接入 | ShopRules.FromTables → BattleShopContext → ShopState。SP是本局商店资源，不是主界面持久金币。 |
| 23 | ShopSlotCount | 已读取但验证必须为3，非自由可配 | 受限接入 | ShopRules.FromTables / SlotCount；MainCombatUI三槽布局。当前UI和掉落规则只支持3，必须填3；其他值会在开局校验时报错。 |
| 24 | NormalEnemyKillSP | 生产FromTables已读取；构造默认4只用于便捷构造 | 已接入 | ShopRules.FromTables → BattleRewardAdapter。同一实体的奖励去重。 |
| 25 | EliteEnemyKillSP | 已读取 | 已接入 | ShopRules.FromTables → BattleRewardAdapter。同一实体的奖励去重。 |
| 26 | BossKillSP | 已读取 | 已接入 | ShopRules.FromTables → BattleRewardAdapter。同一实体的奖励去重。 |
| 27 | NormalGlassBreakSP | 已读取 | 已接入 | ShopRules.FromTables → GlassBoardController → BattleRewardAdapter。只在破碎时奖励，不是每次碰撞奖励。 |
| 28 | ColoredGlassBreakSP | 已读取，当前表值10；便捷构造默认30并不影响生产 | 已接入 | ShopRules.FromTables → BattleRewardAdapter。生产路径使用此行。ShopRules便捷构造的默认30不覆盖此值。 |
| 29 | ShopRefreshPrice | 已读取，默认参数5不是生产写死值 | 已接入 | ShopRules.FromTables → BattleShopContext.TryRefresh；AI读取Context.RefreshPrice。与剩余商品数量无关。 |
| 30 | ShopLv2RateAfterInitial | 已读取 | 已接入 | ShopRules.FromTables → BattleShopContext商品生成。首轮商品固定1星。 |
| 31 | ShopRepeatRerollLimit | 已读取 | 已接入 | ShopRules.FromTables → BattleShopContext商品生成。达到上限接受最后结果，避免候选不足时死循环。 |
| 32 | Reserved32 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 33 | Reserved33 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 34 | ComboBreakSec | 已读取 | 已接入 | PhysicalCollisionBall.BindCombo / AdvanceComboTime。间隔严格大于此值才清零Combo，暴走和冻结期间不推进。 |
| 35 | RageExtraBallCount | 绑定和碰撞忽略逻辑均写死两颗 | 本次接入 | BallGlobalRules → PlacementBoardsBootstrap.BindSession / EnterBerserk / DetachSession。0表示不增加球；超过场景预放数量时按主球模板创建，结束本局销毁。 |
| 36 | ShopQuality2Lv1Price | 已读取 | 已接入 | ShopRules.FromTables / GetPrice。其他星级价格为本值乘星级。 |
| 37 | ShopQuality3Lv1Price | 已读取 | 已接入 | ShopRules.FromTables / GetPrice。其他星级价格为本值乘星级。 |
| 38 | ShopQuality4Lv1Price | 已读取 | 已接入 | ShopRules.FromTables / GetPrice。其他星级价格为本值乘星级。 |
| 39 | ShopQuality5Lv1Price | 已读取 | 已接入 | ShopRules.FromTables / GetPrice。其他星级价格为本值乘星级。 |
| 40 | ShopQuality6Lv1Price | 已读取 | 已接入 | ShopRules.FromTables / GetPrice。其他星级价格为本值乘星级。 |
| 41 | Reserved41 | 空值且无生产调用，旧回放测试还误用41 | 预留 | 无生产调用；BerserkPlaybackTests测试夹具需改用15。保持空白，不是当前暴走持续时间。持续时间使用Global15。 |
| 42 | Reserved42 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 43 | BuffAutoSelectSec | 无读取方；BuffSelectRequest默认参数5f；无生产创建请求的调用方 | 未接入 | BuffSelectData / BuffSelectSession；缺少战斗强化选择调用流程。当前强化UI仅为独立展示组件，时长由BuffSelectRequest传入，默认5秒。战斗没有发起强化抽选流程，改此行不生效。 |
| 44 | BuffSameBuildWeightRate | 无读取方，无候选抽选系统 | 未接入 | BuffSelectUI只显示外部传入的三个候选，不执行随机。当前没有强化候选抽取及Build归类规则，改此行不生效。 |
| 45 | Reserved45 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 46 | Reserved46 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 47 | Reserved47 | 空值且无调用 | 预留 | 无。保持空白，当前没有读取方。 |
| 48 | CycleWave | 已读取且校验 | 已接入 | BattleWaveTimeline.FromTables → EnemyBattleController。当前敌人配置只有两个Cycle，波次表不能超过两个Cycle。 |
| 49 | RageReentryBlockSec | 无读取方，无重入保护 | 本次接入 | BallGlobalRules → PhysicalCollisionBall.SetBerserk / AdvanceComboTime / ProcessRobotCombo。保护期仍可碰撞和攻击，但不累计Combo；暂停不计时。0表示无保护期。 |
| 50 | NormalSpeedMaxRate | 无读取方，无普通球加速机制 | 未接入 | PhysicalCollisionBall.EffectiveSpeed只有基础速度、地图倍率和拖拽减速。当前没有普通球加速或叠加速度系统，普通球使用Global2恒速，此上限没有应用对象。 |
| 51 | StaminaMax | 已读取 | 已接入 | GameApp.CreatePlayerState → MainMenuPlayerState / MatchPlayerRecord.Restore。启动时读取。存档恢复也使用此上限；读取失败回退Demo默认值并告警。 |
| 52 | StaminaRecoverIntervalMin | 已读取 | 已接入 | GameApp.CreatePlayerState → MainMenuPlayerState.Recover。启动时读取，保存时间用于离线恢复；读取失败回退5分钟并告警。 |

## 验证边界

新增回归覆盖：配置球速、地图和拖拽倍率、非50的Combo阈值、发射角数组、低速与无交互恢复、暴走驻留重试、重入保护、0/1/3颗额外球及动态实例清理、无效参数拒绝、主塔非5血量。

强化候选抽选、Build权重、普通球加速系统不在当前工程的运行链路内，未在此次审计中自行补做玩法。

## 验证结果

- 275项相关EditMode测试通过，14项PlayMode真实物理及主塔回放通过。
- Luban实际转表成功；生成的数据和配置代码无变化，因为本次没有调整任何配置数值。
- 52行描述已更新，原有ID、数值、数组、字段类型、52条批注和冻结行均保留。
- 预期错误用例日志已归档，重新请求编译后Unity门禁通过，Console错误为0。
- 修改能源球规则后需转表并开启新局；规则按局快照，不在运行中的物理帧重新加载Excel。
- 测试期间其他会话正在移动UI图片资源，相关资源改动未纳入本次修改，也未回退。
