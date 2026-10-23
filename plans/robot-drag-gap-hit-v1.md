---
任务: 棋盘机器人缝隙拖拽命中修复
版本: 1
状态: executing
---

# 棋盘机器人缝隙拖拽命中修复计划V1

## 目标

让棋盘 Robot 的 Cell_* 视觉缝隙可以开始拖拽，同时保持 L/T 形凹陷区域不可命中。拖拽命中 Collider 直接保存到八个 Robot Prefab；运行时只读取并同步启停，不动态创建、销毁或修改命中区域几何。

## 代码逻辑设计

当前链路为 Physics2DRaycaster 命中 Cell_* 的 BoxCollider2D，经 EventSystem 调用根节点的 RobotBoardPointerInput，再进入 BattleShopSceneController.PressBoardRobot、RobotDragController 和 PlacementController。机器人根节点没有覆盖完整形状的 Collider，ShapeOutline 只有显示组件。Cell 间距约为 0.35，Cell 实际显示/碰撞宽度约为 0.329，缝隙没有 Collider，所以点击缝隙时输入链路不会启动。

直接在 Prefab 根节点下增加 DragHitArea，并为 PlaceableDefinition.Cells 中每个真实占格保存一个 HitCell_*。每个 HitCell_* 包含 RobotDragHitCollider、Trigger BoxCollider2D，并从对应 Cell_* 的实际 Collider 边界计算位置和尺寸。相邻真实占格之间的缝隙由两侧各扩展一半覆盖；包围盒凹陷没有对应 HitCell，因此 L/T 凹陷仍不可命中。

RobotView 运行时只缓存 Prefab 中的 DragHitArea 和实体 Cell Collider。Render 根据棋盘、拖拽、暂存、移除状态同步启停命中区域，但不创建、销毁或改变其几何配置。GetPhysicalColliders 只返回未标记为 RobotDragHitCollider 的实体碰撞体。PhysicalCollisionBall 的保护、接触、距离检测和碰撞处理统一使用该接口，并跳过拖拽辅助 Collider。

命中后，PressBoardRobot 将指针屏幕位置转换为世界位置，使用机器人实际 Cell_* 中心选择最近的 PlaceableDefinition.Cells 坐标作为 grabbedCell。现有 PlacementController 的完整形状包围盒中心吸附、网格预览、合成、提交和非法回位逻辑保持不变。

## 计划改动

- 新增 RobotDragHitCollider 标记组件。
- 修改 RobotView，分离实体碰撞与拖拽命中、提供最近占格查询并同步命中区域生命周期。
- 修改 BattleShopSceneController.PressBoardRobot，使用最近真实占格作为 grabbedCell。
- 修改 PhysicalCollisionBall，排除拖拽辅助 Collider。
- 新增 Unity Editor Prefab 工具，使用 PrefabUtility.LoadPrefabContents 和 SaveAsPrefabAsset 批量保存八个 Robot Prefab 的 DragHitArea/HitCell_*。
- 更新 Prefab 检查和碰撞测试，使拖拽辅助 Collider 不计入实体物理 Collider 数量。

不修改机器人占格配置、现有 Cell Sprite 和业务 Collider、场景、地图、相机、部署判断、合成和攻击逻辑。实施前通过 Unity MCP 确认工程为 C:/Users/Admin/FreshGuard，保存 Prefab 后等待编译、检查 Console，并使用 TEngine Launcher 验证典型形状、缝隙、凹陷、中心对齐、非法回位和能源球碰撞。
