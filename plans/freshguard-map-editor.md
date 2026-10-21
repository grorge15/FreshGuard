---
slug: freshguard-map-editor
版本: 3
状态: completed
创建: 2026-10-21
---

# FreshGuard 地图编辑器计划V3

## 代码逻辑设计

实现 FreshGuard/Map Editor 的 Unity EditorWindow：默认9行11列，格距0.35，行列各1至64。支持新建、画笔、连续绘制、矩形/全图填充、擦除为空位、撤销重做、尺寸调整、二进制加载保存导出及错误定位。缩小确认，扩大保留左下区域并填Empty；未保存修改提示。仅.bytes，不实现JSON或网页旧格式。

复用BoardCoordinate、BoardModel、PlacementBoardView、BoardLayoutRules、GlassBoardController/GlassView、BattleShopSceneController/PlacementBoardsBootstrap。TerrainType:byte固定Track=1、Empty=2、GlassNormal=3、GlassColored=4，0无效。Track不可部署但球可通行；Empty可部署且球可通行；玻璃两项均不可。外圈遵从地形，无隐藏禁部署区。玻璃破碎变Empty，先提交地形与碰撞事实再奖励。

MapData保存Version、Rows、Columns、CellSize、NormalGlassId、ColoredGlassId、Terrains。仅数据不含Prefab引用，玻璃ID映射Luban既有配置；支持任意数量彩玻。双方初始图相同、运行副本独立，破碎不保存回文件。复用现有占用和多格部署逻辑；新地图部署许可由地形推导。

坐标原点左下，列右增、行上增，索引row*columns+column。局部格心=(column+0.5,row+0.5)*cellSize-(width,height)/2；共享坐标数学。提供GetTerrain/CanPlaceUnit/CanEnergyBallPass及边界邻格查询。CanPlaceUnit是地形许可，最终部署仍检查占用。越界布尔false，地形读取报错。球依旧物理反弹而非寻路。

编辑器代码在Assets/Editor/MapEditor，运行时代码在GameLogic/Gameplay/Maps，不能反向依赖UnityEditor。文档SO承载Undo及窗口恢复，颜色/缩略图预览，不生成战斗资源。

二进制V1小端：FGMP(4 bytes)、UInt16版本、Int32列、Int32行、Float32格距、Int32普通玻璃ID、Int32彩玻ID、Int32格数、每格1字节地形。严格拒绝截断/尾随/未知版本/无效地形/尺寸。以后按版本解码转换，编号不改。MapLoader支持Deserialize(byte[])、LoadAssetAsync(location,token)、LoadFileAsync(path,token)，用框架TextAsset异步读取后释放，本地IO异步。默认Configs/Maps，资源Map_前缀。

BattleShopSceneController增加地图资源地址。空地址保留旧配置与旧布局；指定但失败停止开局。流程：读/校验、双方独立副本、格子外墙、布局、玻璃、塔/敌人路线/商店、中心出生检查、发射。球固定几何中心（偶数图中心不是格心）。本期不支持战斗中人为改格或改尺寸。

以当前场景11列×9行、原始格距0.35为视觉基准，只等比缩放PlacementBoardView网格内容。相机Position、Rotation、Orthographic Size、Viewport Rect全部保持场景配置。双方世界根、棋盘中心、背景、边带、塔、路线、商店保持原布局。参考宽高为11×referenceCellSize、9×referenceCellSize；fitScale=Min(referenceWidth/(Columns×CellSize),referenceHeight/(Rows×CellSize))。棋盘实际XY缩放=各方场景原始XY缩放×fitScale，Z不变。非同比例图居中留白；参考图倍率1，22×18倍率0.5，13×13倍率9/13。原场景层级如已非等比变形则报告节点，不横纵拉伸补偿。

BattleMapLayoutController改为网格适配，基准每个实例捕获一次，不能从上一局的尺寸或已缩放结果重采样。停用双方间距平移、背景/边带缩放、塔/路线重定位、FitCamera、LateUpdate动态取景、商店到布局的绑定及相机写入/恢复。清理只恢复自身改动的棋盘和能源球状态，旧地图倍率1。

格子、玻璃和外墙继续按地图逻辑CellSize生成，再由父棋盘统一缩放。机器人Prefab制作格距为0.35；部署、移动、合并刷新共用落位入口，局部缩放=Prefab原始缩放×CellSize/制作格距，使形状偏移、图片、碰撞与占格一致。外墙厚度按参考格距换算。继续使用MapGridMath与TransformPoint/InverseTransformPoint，不另建坐标系统或重复乘缩放。

能源球保持独立Rigidbody2D，不重新挂到棋盘下；尺寸与速度共同跟随cellScale=fitScale×CellSize/referenceCellSize。实际球缩放=球原始缩放×cellScale，基础世界速度=配置基础速度×cellScale；新增运行倍率进入EffectiveSpeed，配置值保持原值，拖拽减速/暴走/反弹继续复用。停止状态应用倍率，中心发射；每局清理恢复基准。运行时按真实半径/缩放和真实碰撞体检查净空。地图窗口使用当前战场基准预检，缺少场景基准时提示运行复检，不再用固定1.1强制判定。文件不增加显示缩放字段。

新图流程：读取校验、捕获原始基准、双方网格/外墙重建、网格与球体倍率、玻璃、固定塔/路线/商店、中心净空、发射。初始化失败、退出、新旧图切换恢复基准。

数据非法/资源缺失禁止导出或开局；绑定战场后出生球与玻璃/外墙重叠禁止开局。通路不连通/无部署格/通道窄于球体警告但可导出。连通性是4邻域提示，不保证真实物理可遍历也不强制闭环。

## 文件边界
- 可写：BoardModel、PlacementBoardView、BoardLayoutRules、GlassBoardController、BattleShopSceneController、必要UI布局接口及相应测试。
- 可新建：Gameplay/Maps、Assets/Editor/MapEditor、地图EditMode/PlayMode测试、scripts门禁及用户使用说明。
- Unity资源仅由主执行者在确认MCP目标工程后通过Editor API修改保存，不手写YAML；保留已有Atlas、配置和规则改动。
- 只读：Luban生成配置、协作管线原始文件、参考网页。

## To-dos
- [x] 1. 地图模型、坐标、校验、严格二进制格式、加载器及数据测试。
- [x] 2. EditorWindow及文档Undo、绘制、加载导出、资源校验。
- [x] 3. BoardModel/玻璃/对局接入，新图与旧图兼容。
- [x] 4. 自动布局、相机/UI安全区、中心出生校验。
- [x] 5. 恢复根目录Unity门禁脚本；编译和功能回归、通过Editor API生成示例地图与使用说明。
- [x] 6. V3固定相机/布局、等比网格缩放及无累计基准恢复。
- [x] 7. V3机器人格距、外墙厚度、球体尺寸/速度与预检同步。
- [x] 8. V3专项测试、Play验证、使用说明、编译/Console与门禁。

## 校验点
- [x] 默认99格、四地形许可、多格部署、外圈Empty可部署。
- [x] 二进制往返一致；尺寸/版本/编号/截断/尾随错误拒绝。
- [x] 绘制拖拽填充擦除/尺寸Undo；窗口实际输入事件和二进制保存回读通过。
- [ ] 取消裁剪、关闭窗口保存及操作系统文件选择对话框逐项人工点击验证。对应保护分支已实现，未执行人工对话框检查。
- [x] 双方破碎隔离、奖励、重开恢复；零及多彩玻。
- [x] 奇偶中心净空数据测试、横竖地图/屏幕比例布局测试、实际重开与禁用恢复通过。真机安全区未验证。
- [x] 空地图地址旧图部署/玻璃/商店/敌人回归通过。
- [x] Unity编译/Console和node scripts/verify-unity-mcp-gate.mjs；未执行项如实记录。

## 执行结果

以下为V2历史验证产物，V3结果单独记录于后文，不沿用旧取景结果。

EditMode七个程序集335项全部通过。Editor文档检查及实际窗口SendEvent拖拽、撤销重做、擦除、原子保存回读通过。真实PlayMode验证Map_Demo加载、双方5块玻璃、物理命中破碎与10/30币奖励、双方隔离、新局恢复、相机商店避让、无漂移和旧图双方84块玻璃。Unity编译完成，Console错误0，根目录MCP只读门禁通过。

通过Unity Editor API新增Map_Demo.bytes示例及自动meta；没有保存修改现有场景或Prefab。业务Main场景保持空地图地址与未脏状态，编辑窗口显示示例地图。原有用户配置、规则及图集改动保留。

文件清单、使用方法、30字节FGMP头格式和验证边界见summaries/map-editor-usage.md。未执行独立Player打包、移动真机及上述人工系统对话框，不宣称覆盖这些验证。原恢复策略仍适用于后续追加验证。

## 恢复策略
V3已完成568项EditMode测试及编辑文档检查。实际场景的塔/路线是棋盘子节点，适配时临时保留世界变换移出，恢复时挂回；专项用例及真实运行验证均通过。

通过`Verify via TEngine Launcher`调用真实`SceneHelper.StartScene`启动，完整运行报告PASS：11×9参考倍率1、相机Size=5及固定布局不变、实际物理普通/彩色玻璃破碎与10/30币奖励、双方隔离、重开恢复、Map_Demo2的13×13倍率9/13、坐标往返、球体速度同步、无累计及禁用恢复、空地址旧图双方84块玻璃。命中等待条件已修正为指定玻璃实际耐久下降，并完成复验。

本地桥已恢复，最新脚本已在Unity编译加载；菜单增加通知与Console状态日志，重复点击也反馈运行端口。最终只读门禁通过，Console错误0。验证自动退出Play后仍停在启动场景，未确认Launcher自动返回业务场景；已通过Editor API恢复`Assets/AssetRaw/Scenes/Main.unity`，确认dirty=false、MapLocation仍为用户的`Map_Demo2`。未保存修改场景或Prefab。后续人工验证需单独确认工具栏Launcher的自动返回行为，不将手动恢复作为该行为通过的证据。

从未完成Todo继续，先检查实际磁盘与Git；不覆盖用户已有改动。Unity资源装配串行且检查现有对象，不能重复生成。测试失败先读日志；MCP不可用则保留脚本并停止资源写入，报告真实验证限制。

## 计划改动
相较V2取消战场重排、背景适配和相机动态取景，改为以原11列×9行显示区域等比缩放网格内容。能源球尺寸与速度同步缩放，并补齐机器人格距换算及出生净空校验。
