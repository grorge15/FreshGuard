---
标题: 最终波清空胜利结算计划V1
版本: 1
状态: executing
工程: C:/Users/Admin/FreshGuard
确认来源: 用户“执行计划最终波清空胜利结算计划V1”
---

# 最终波清空胜利结算计划V1

## 一、代码逻辑设计

最终波由本局加载的 TbWavePlan 和其引用的全部 TbWaveSpawn 决定，不写死20、时间或Boss类型。复用 BattleWaveTimeline.IsAllSpawnsCompleted；在双方实际出兵完成、活动敌人清空、死亡表现结束且玩家塔仍存活时请求胜利。预备、暂停、清理和旧局不得触发。

EnemyBattleController 提供带当前对局归属检查的 IsWaveVictoryReady。保留 IsAllEnemiesCleared 的既有含义；新条件额外检查双方每组实际生成数量、死亡表现字典和异常移除状态。意外销毁或手动清理敌人不能被当成正常击杀或抵塔通关。死亡动画复用现有完成回调与3秒兜底，暂停时不推进。

BattleShopSceneController 在本帧敌人推进、抵塔批量伤害与机器人攻击处理后检查条件。先核对同一个 Context 和战斗状态，再调用 TowerBattleSession.TryCompleteWaveClear。塔摧毁结算优先，保留同一抵塔时刻双方同时摧毁按胜利处理。

BattleSettlementSnapshot 增加 BattleSettlementReason（TowerDestroyed、FinalWaveCleared）；胜负显式构造并保存双方真实HP。没有死亡塔的清空胜利使用可空塔死亡字段，不伪造敌方死亡。TowerBattleSession 的终局锁同时覆盖两类原因；复用 SettlementRequested、入口延迟清理、BattleSettlementPresenter 和 CheckOutUI，保持每局一次。

Cycle 与最终波分别处理：当前 Global[48] 定义每Cycle波数，计算 (wave-1)/wavesPerCycle+1。现有敌人只有 HpC1/HpC2，实体创建和时间线均明确拒绝Cycle>2。本次保留属性契约，不新增未确认的第三套生命或增长公式；超过20波可由策划显式调整Global[48]（例如15支持30波），21/30波验证使用独立测试配置。超过两个已配置Cycle仍须在准备阶段明确报错，不截断、不钳制、不静默复用HpC2。未来需要3个以上Cycle时另行确认属性表扩展方案。

## 二、计划改动

初始版本。

## 文件边界

- Assets/GameScripts/HotFix/GameLogic/Gameplay/Battle/TowerBattleSession.cs
- Assets/GameScripts/HotFix/GameLogic/Gameplay/Battle/EnemyBattleController.cs
- Assets/GameScripts/HotFix/GameLogic/Gameplay/ShopPresentation/BattleShopSceneController.cs
- Assets/GameScripts/HotFix/GameLogic/Gameplay/ShopPresentation/BattleShopSceneController.Towers.cs（必要时）
- 相关现有结算、波次、敌人和入口集成测试
- 必要的项目级验证脚本与配置使用说明
- 不修改场景、Prefab、正式配置二进制、源图片或既有奖励规则。

## 执行Todo

- [x] 核对源码、已有改动及Cycle限制。
- [x] 保存已确认计划。
- [ ] 实现原因快照与统一终局锁。
- [ ] 接入当前局清空条件与帧末检测。
- [ ] 覆盖可变波数、抵塔优先、表现等待、异常清理、重复请求与重开。
- [ ] Unity编译与Console检查。
- [ ] TEngine Launcher实际运行验证。
- [ ] 运行 scripts/verify-unity-mcp-gate.mjs。

## 校验点与恢复策略

用户已有 .gitignore 修改与 doc_extract_utf8.txt 保留。仓库引用的 plans/_TEMPLATE.md 和 .cursor/rules/plan-file-protocol.mdc 本次实际不存在，按AGENTS要求保留版本、进度、边界、校验与恢复信息。没有修改前创建任何Unity资源。

中断后先检查Git差异及本文件未完成项；不得重复运行或覆盖用户资源。MCP不可用时可继续代码与离线检查，停止所有编辑器资源写入，明确记录未完成运行验证；不可将只读门禁当成功能验证。

## 执行记录

- 已检查：现有最终波清空属性未接入结算；抵塔先提交双方HP再发布死亡通知；死亡表现有3秒兜底。尚未修改代码或资源。
