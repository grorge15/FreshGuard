# FreshGuard 地图编辑器使用与验证

实施版本：地图编辑器计划V4。地图只使用 `.bytes` 二进制，没有 JSON 地图加载、保存或调试导出。

## 编辑地图

1. 在 Unity 2021.3.50f1 中打开本工程，选择 `FreshGuard > Map Editor`。
2. 默认新图为11列、9行、格距0.35，全部为 Empty + None。行列各支持1～64；在顶部修改后点“调整尺寸”。扩大保留左下重叠区域并填空，缩小提示裁剪范围和格子数量，可取消或撤销。
3. 基础地形画笔选择轨道或空位，只修改底层；障碍画笔选择无障碍、普通玻璃或彩色玻璃，只修改障碍。左键点击或拖拽绘制，右键统一只擦除障碍，保留底层。矩形及全图填充使用当前画笔；重置格子工具或“重置全图”同时恢复 Empty + None。
4. 每次连续拖拽作为一步撤销。尺寸、参数、填充同样支持撤销/重做。新建、切换文件、关闭窗口会检查未保存修改。
5. 点“校验”检查数据、玻璃配置及资源、中心球体净空和通行区域。点击带坐标的问题可定位。错误禁止保存；警告允许保存。
6. “保存”或 `Ctrl+S` 写回当前文件；“另存 / 导出”或 `Ctrl+Shift+S` 选择新文件。默认目录是 `Assets/AssetRaw/Configs/Maps`，文件名必须为 `Map_名称.bytes`。通过“加载 .bytes”重新编辑。

左下角为(0,0)，列向右、行向上；窗口上方显示最大行号。底色区分Track/Empty；居中的玻璃图标保留底色边框，悬停显示坐标、基础地形与障碍。玻璃画笔带现有Prefab缩略图。

球体净空预检读取当前场景双方绑定的棋盘、参考格距和能源球，分别计算缩放后的局部半径。缺少场景基准或存在非等比/剪切时提示运行时复检，不再按固定1.1缩放阻止导出；必要资源缺失仍报错。游戏开局还会按真实球体半径及碰撞体复检。单格窄通道仅为警告，不代表整张地图无法通行。

## 在游戏中使用

示例：[Map_Demo.bytes](C:/Users/Admin/FreshGuard/Assets/AssetRaw/Configs/Maps/Map_Demo.bytes)。99格，外围为轨道，含3块普通玻璃和2块彩色玻璃，中心留空。

1. 在 Unity 打开业务场景 `Assets/AssetRaw/Scenes/Main.unity`，选择 `BattleShop` 对象。
2. 在 `BattleShopSceneController` 的 `Map Location` 填写 YooAsset 地址，例如 `Map_Demo`，不带扩展名，然后通过 Unity 保存场景。新增地图保持在现有资源收集目录内，打包时重新收集/构建资源包。
3. 在业务Main场景点击工具栏 **TEngine Launcher**。Launcher选择启动场景 `Assets/Scenes/main.unity`，初始化 TEngine、YooAsset 和 Luban 后进入对局，停止时自动返回原编辑场景。不要直接对业务Main场景点击Unity普通Play。

地图地址留空使用原 `_layoutConfigId` 布局。指定地址读取失败会停止开局并报错。本轮开始时业务场景配置为用户的`Map_Demo2`（13列×13行）；代码适配不改写此配置，可按上述Inspector方式更换地图或清空回到旧布局。

双方加载独立副本；外圈空位可以部署，轨道只允许球通过。最终机器人部署仍检查完整形状和占用。玻璃破碎先关闭碰撞，再只移除当前方障碍并刷新格子，随后走原奖励和淡出逻辑。Track底层保留不可部署规则，Empty底层恢复可部署；两者均允许能源球通行。破碎不写回地图文件，新局恢复原图。球从棋盘几何中心发射；中心与玻璃、外墙或准备期机器人重叠时停止开局。

以原11列×9行、参考格距0.35区域容纳地图。`fitScale = Min(11 / Columns, 9 / Rows) × referenceCellSize / CellSize`，棋盘XY缩放始终取原始缩放乘fitScale，保持正方形和居中留白。11×9原格距倍率1；22×18倍率0.5；13×13倍率9/13。棋盘原有世界XY非等比或剪切会报告错误，不通过拉伸补偿。

相机Position、Rotation、Orthographic Size、Viewport Rect以及双方棋盘中心、背景、边带、塔、路线、商店保持场景配置，不再动态取景或排列双方战场。格子、玻璃、外墙和机器人随网格缩放；机器人局部制作格距0.35会换算为地图CellSize。能源球保留独立刚体，尺寸与配置基础速度同时乘`fitScale × CellSize / referenceCellSize`，拖拽减速和暴走继续叠加，配置速度本身不改。重开从基准计算，退出、禁用或失败恢复原倍率。地图文件不保存显示缩放。战斗中不提供任意地形重建、改尺寸或地图热重载。

现有塔和路线位于棋盘下，运行时适配会先保留它们的世界变换并临时移到棋盘父节点，结束后恢复原层级和顺序；引用始终有效。退出或失败时隐藏旧局网格/玻璃/机器人并关闭外墙，避免恢复父缩放后残留放大的内容。

完整运行验证入口为 `FreshGuard > Map > Verify via TEngine Launcher`，调用工具栏Launcher使用的同一`SceneHelper.StartScene`，框架启动后自动检查地图，完成时停止。Launcher的原逻辑负责返回编辑场景；本轮自动返回未确认，见下方验证记录。`EditMode`测试仅验证独立逻辑，不替代该启动流程。

## 数据接口与格式

运行时代码位于 [Maps](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Maps)，不依赖UnityEditor。

- `MapData` 保存版本、行列、格距、两种玻璃Luban配置ID及一维 `MapCellData[] Cells`；每格含 `BaseTerrain` 和 `Obstacle`。`Clone`隔离双方和编辑文档。
- `GetBaseTerrain` / `GetObstacle` 分层读取；`SetBaseTerrain` / `SetObstacle`仅修改对应层。地图模式`BoardModel`提供两层读取；旧布局没有完整分层数据，调用新读取接口会明确报错。
- `CanPlaceUnit`仅允许Empty+None，不含机器人占用；`Evaluate`继续检查完整占格形状。`CanEnergyBallPass`要求有效基础地形且无障碍。布尔越界返回false，读取越界抛异常。
- `BoardModel.TryRemoveObstacle`是内部破碎入口；地图模式的旧`SetOpen`不再修改两层。无地图地址时仍使用原Luban开放格/保留边框行为。
- `MapLoader.Deserialize`、`LoadAssetAsync`、`LoadFileAsync`负责兼容解析、YooAsset读取与本地异步IO；资源读取后释放引用。
- `MapData.IsInside`、`GetNeighbors`保留边界及四邻格；`MapGridMath`和`PlacementBoardView`坐标映射不变，索引为 `Row * Columns + Column`。

当前写入 **FGMP V2**，固定小端30字节头：ASCII `FGMP`、UInt16版本、Int32列数、Int32行数、Float32格距、Int32普通玻璃ID、Int32彩色玻璃ID、Int32格数。随后每格2字节，依次基础地形和障碍，总长度 `30 + 2 * Rows * Columns`。

基础编号：Track=1、Empty=2；障碍编号：None=0、GlassNormal=1、GlassColored=2。读取严格拒绝无效编号、尺寸/格距、数量不符、截断、尾随和未知版本。

兼容FGMP V1每格1字节：旧Track/Empty分别迁移到Track+None/Empty+None；旧两类玻璃迁移到Empty+对应玻璃。读取只在内存迁移为V2，不写回文件；编辑器提示源版本，主动保存才升级。已有Map_Demo/Map_Demo2不批量转换。地图不保存GameObject/Prefab或场景显示缩放，不提供JSON。

## 改动文件

- 新增运行时：`MapData.cs`、`MapGridMath.cs`、`MapValidator.cs`、`MapBinarySerializer.cs`、`MapLoader.cs`、`MapBattleValidation.cs`、`BattleShopSceneController.Maps.cs`、`BattleMapLayoutController.cs`，均在上述Maps目录。
- 新增编辑器：[MapEditor](C:/Users/Admin/FreshGuard/Assets/Editor/MapEditor)下的`MapEditorWindow.cs`、`MapEditorDocument.cs`、`MapEditorGlassAssets.cs`、`MapEditorDocumentChecks.cs`；[MapEditorVerification.cs](C:/Users/Admin/FreshGuard/Assets/Editor/MapEditorVerification.cs)和[MapEditorPlayVerification.cs](C:/Users/Admin/FreshGuard/Assets/Editor/MapEditorPlayVerification.cs)提供显式MCP桥接和运行验证入口。
- 修改部署：[BoardModel.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Placement/BoardModel.cs)、[PlacementBoardView.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Placement/PlacementBoardView.cs)、[PlacementBoardsBootstrap.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Placement/PlacementBoardsBootstrap.cs)。
- 修改玻璃：[BoardLayoutRules.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Glass/BoardLayoutRules.cs)、[GlassBoardController.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Glass/GlassBoardController.cs)。
- 修改对局及显示：[BattleShopSceneController.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/ShopPresentation/BattleShopSceneController.cs)、[EnemyBattleController.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Battle/EnemyBattleController.cs)、[PhysicalCollisionBall.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/Gameplay/Physics/PhysicalCollisionBall.cs)、[BattleShopUI.cs](C:/Users/Admin/FreshGuard/Assets/GameScripts/HotFix/GameLogic/UI/BattleShopUI/BattleShopUI.cs)。
- 新增测试：[Maps测试](C:/Users/Admin/FreshGuard/Assets/Tests/Maps)、[布局测试](C:/Users/Admin/FreshGuard/Assets/Tests/MapLayout)，各包含测试程序集及测试类。
- 新增资源：示例地图及Unity自动生成的对应目录/文件`.meta`。没有修改现有场景或Prefab。
- 恢复脚本：[unity-mcp-client.mjs](C:/Users/Admin/FreshGuard/scripts/unity-mcp-client.mjs)、[verify-unity-mcp-gate.mjs](C:/Users/Admin/FreshGuard/scripts/verify-unity-mcp-gate.mjs)，根目录从脚本位置推导，使用MCP 10.1.2本地stdio服务。
- 计划与报告：[实施计划](C:/Users/Admin/FreshGuard/plans/freshguard-map-editor.md)及本文件。用户原有规则、代理、Codex配置和图集修改保留。

## V4验证进度

实施中，实际验证结果将在测试完成后填写；下方V3/V2内容均为历史结果，不能作为V4已通过的证据。

## V3历史验证

- V3运行时代码及新增专项用例已在当前Unity通过七个EditMode程序集：568通过、0失败、0跳过。包含固定相机、极端宽高、不同格距、原缩放恢复、机器人碰撞尺寸、球体速度/拖拽/冻结/暴走以及真实塔/路线子层级保留测试。
- `MapEditorDocumentChecks.Run()`通过。该阶段Unity编译成功，Console错误0。
- 按用户要求，完整运行验收通过`Verify via TEngine Launcher`复用工具栏`SceneHelper.StartScene`，框架初始化并进入业务Main后测试。最终运行报告PASS；此前彩玻命中等待条件已由任意反弹改为指定玻璃实际耐久下降，并完成复验。
- 实际运行通过：11×9倍率1，相机Position/Rotation/Size=5/Rect和所有固定节点不变；双方能源球发射；普通玻璃3次、彩玻5次实际物理命中破碎，转Empty并奖励10/30币；双方隔离和重开恢复正确。
- 实际加载用户`Map_Demo2`的13×13地图通过：倍率9/13、双方中心及塔/路线等固定节点不变、世界坐标往返、球体尺寸与实际速度同步、连续重开无累计、禁用恢复。空地址旧图双方84块玻璃回归通过。
- 验证已自动退出Play。退出后仍停在启动场景，未确认Launcher自动返回业务场景；已通过Editor API恢复业务Main，确认场景未脏、原地址`Map_Demo2`保留。自动返回行为需后续单独确认。
- 本地MCP桥已恢复，最新脚本已在Unity编译加载。最终`node scripts/verify-unity-mcp-gate.mjs`通过：目标工程匹配、编辑器就绪、Console错误0。没有重启Unity进程或继续桌面输入操作。
- 未保存修改场景或Prefab；用户现有`Map_Demo2`、业务Main地址、双方不同的作者位置/缩放及其他已有修改保留。

## V2历史验证

- 当前工程MCP确认：`C:/Users/Admin/FreshGuard`，Unity `2021.3.50f1`；最新脚本完成编译，最终Console错误0。
- EditMode共335项通过、0失败、0跳过。程序集为地图数据、自动布局、Placement、Entities、Shop、TowerBattle、EnemyPlayback。覆盖二进制往返及损坏文件、地形/多格部署、双方副本、奇偶中心净空、横竖长图/不同渲染目标比例/商店避让和恢复等。
- `MapEditorDocumentChecks.Run()`通过：默认参数、独立副本、八象限连续线、矩形填充、拖拽/尺寸Undo与Redo、扩展裁剪、清空。
- 实际EditorWindow输入事件通过：左键拖拽11格、整笔Undo/Redo、右键擦除、原子保存到Library临时`.bytes`并回读一致、保存后无未保存标记。截图观察网格方向、画笔缩略图及校验结果正常。
- 从启动场景进入Play，真实加载`Map_Demo`并验证双方99格和5块玻璃、轨道许可、空位部署、商店避让和能源球发射。实际物理命中普通玻璃3次、彩色玻璃5次，破碎转Empty，分别奖励10/30币；对手地图和奖励不受影响。
- PlayMode重开恢复5块玻璃，布局与相机无漂移；实际禁用布局组件时OnDisable恢复相机；空地址回到旧图双方84块玻璃。验证结束恢复原地图地址、退出Play并回到业务Main场景。
- `node scripts/verify-unity-mcp-gate.mjs`通过：工程匹配、编辑器就绪、Console错误0。MCP缓存状态过期时脚本进行实时只读探测，未跳过工程/编译/Console检查。
- 相机视觉比例调整后，15项布局专项测试全部通过，包含超大背景不影响取景、小图不额外放大单位、商店视口变化时保持作者像素比例，以及横竖地图适配。当前Game视图中，编辑器Size为5，默认地图适配Size由约7.155降至4.919；PlayMode物理、奖励、重开无漂移、禁用恢复和旧图回归再次通过。

验证记录位于 `Library/MapEditorPlayVerification.txt`、`Library/MapEditorWindowVerification.txt`，它们是本机临时产物，不作为资源发布。

尚未执行：操作系统文件选择/关闭窗口保存/取消裁剪对话框的逐项人工点击；独立Player打包、移动设备实际安全区及真机分辨率切换。自动布局中的屏幕比例验证使用Unity测试渲染目标与UI对象。没有把单元测试或门禁当作这些平台验证的替代。

如重启Unity后本地stdio桥未连接，可显式执行 `FreshGuard > Map > Start Local Verification Bridge`；该入口启动后台服务，不打开窗口。点击后会在当前窗口显示通知并在Console记录运行端口或待重试状态；桥已运行时重复点击也有反馈。当前端口为6400。该入口在当前Editor会话跨域重载恢复桥接，不修改全局HTTP传输偏好，也没有修复远端WebSocket服务配置。
