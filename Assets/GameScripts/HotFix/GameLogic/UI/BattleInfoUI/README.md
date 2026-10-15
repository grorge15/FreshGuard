# 战斗信息栏与主战斗窗口

战斗入口预制体：`Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab`。

`MainCombatUI` 是唯一的全屏 `UIWindow`，内部的 `BattleInfoUI` 与 `BattleShopUI` 是 `UIWidget`。打开主预制体，选择这两个直接子节点，调整 RectTransform 的 Pos、Width、Height、Scale 和锚点，然后保存。内部面板跟随各自区域拉伸，运行时代码不会重置区域位置、尺寸或缩放。请修改 Project 中的预制体；Play 模式下对实例的调整不会自动保留。

源预制体 `UI/BattleInfo/BattleInfoUI.prefab` 和 `UI/Shop/BattleShopUI.prefab` 保留为嵌套模板；整体布局在 MainCombatUI 的嵌套实例上调整。不要再独立 ShowUI/CloseUI 两个子区域。

场景 host 统一打开 `MainCombatUI(context, host, progress)`，通过 `CombatWindow.Info`、`CombatWindow.Shop` 访问子区域。主窗口关闭时，框架统一清理子组件的事件、按钮与拖拽；隐藏主窗口时取消拖拽。

信息栏左侧为玩家、右侧为敌方、中间为当前波次与整场累计时间。内容接口：

```csharp
var view = host.CombatWindow.Info.View;
view.SetPlayerInfo(playerName, playerTrophies, playerAvatar);
view.SetEnemyInfo(enemyName, enemyTrophies, enemyAvatar);
view.SetBattleProgress(currentWave, totalBattleElapsedSeconds);
```

`BattleInfoUI.Bind` 绑定对局快照；`AddUIEvent` 只接收同 BattleId 的进度事件。累计秒数由战斗控制器提供，UI 仅显示 MM:SS，跨波不清零、60分钟不回绕。头像 Sprite 由调用方保持有效，null 显示占位。信息区域不阻挡盘面点击，商店区域正常交互。

`BattleInfoView.SetLayoutPosition` 保留为可选的手动调用接口；当前对局流程不调用它，以免覆盖预制体布局。

Editor 菜单 `FreshGuard/UI/Create MainCombatUI If Missing` 只创建缺失的组合预制体，重复执行不覆盖现有布局。旧信息栏 Builder 仅用于维护源模板。
