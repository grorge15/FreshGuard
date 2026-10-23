---
slug: robot-drag-center-alignment
版本: 1
状态: completed
创建: 2026-10-23
---

# 机器人拖拽中心对齐计划V1

## 一、代码逻辑设计

RobotDragGhostWidget 已将图像中心放在鼠标处，但 PlacementController 使用“鼠标格减被抓取格”计算锚点。统一为鼠标对应图像中心，围绕完整占格形状的包围盒中心计算最近整数部署锚点。边缘拿起时允许一次居中变化，不保留抓取偏移。

PlacementController.BeginDrag 遍历 PlaceableDefinition.Cells，缓存 Vector2((minColumn+maxColumn)/2f,(minRow+maxRow)/2f)。保留 BeginDrag 重载和抓取格属于形状的校验。中心允许半格，不要求位于实际占格。

MapGridMath 增加 GetCenteredAnchor(Vector3 localPoint,int columns,int rows,float cellSize,Vector2 footprintCenter)。连续格中心坐标为 localPoint/cellSize + dimensions/2 - 0.5；减去形状中心后使用 Floor(value+0.5) 获取最近锚点，平局选较大行列。世界点通过目标棋盘 InverseTransformPoint 转为局部坐标，保留实际格距及缩放。不夹紧越界锚点，仍由 BoardModel.Evaluate 和 PlacementBoardView.ShowPreview 判断、显示。

RobotDragController 四来源统一使用中心定位。Release 先按 PointerUp 最终坐标刷新图像和预览，再提交同一次计算的目标；SubmitAsync 不再重复计算锚点。异步部署沿用现有事务再校验，失效时失败或回位，不自动换位置。合成继续查询鼠标实际格，不查部署锚点。暂存、购买、长按、取消回位和球拖拽状态保持现有流程。非法最终位置不退回之前有效预览。

Ghost 沿用 Image 几何中心和图标大小，不读取纹理像素、不修改图片。复用全部占格、部署、碰撞和合成逻辑，不修改场景、Prefab、相机或地图。

验收覆盖横条、竖条、L/T 形和奇偶尺寸，半格中心、不同格距及棋盘缩放；同一鼠标位置不因抓取格不同改变锚点；中心与鼠标局部各轴误差不超过半格；PointerUp 未先收到 Move 时仍与最终预览一致；边界、障碍、占用变化、敌方棋盘、合成及非法回位保持正确。Launcher 脚本的部署点投影锚点加形状中心，合成点保留实际命中格。

## 二、计划改动

初始版本。

## 执行Todo

- [x] 实现共享中心吸附、缓存形状中心和最终松手刷新。
- [x] 更新占格测试和 Launcher 验证点，增加中心与最终指针回归。
- [x] Unity 编译、Console、必要测试和 TEngine Launcher 验证。
- [x] 运行 Unity MCP 门禁，记录实际结果及未验证项。

## 文件边界与恢复

修改 PlacementController、MapGridMath、RobotDragController 及相关测试/Launcher 验证脚本。RobotDragGhostWidget 和 PlacementBoardView 复用现有接口。保留实施前 .codex 下四个配置文件及 AGENTS.md 的用户修改。不修改 Unity 序列化资源。

中断后检查已有 diff 与本清单，续做未完成项，不覆盖用户改动。模板文件 plans/_TEMPLATE.md 及 .cursor/rules/plan-file-protocol.mdc 在当前检出中不存在，采用已有计划的 frontmatter 和交接结构。

## 执行记录

2026-10-23 完成。PlacementController 在拿起时缓存完整占格实际 min/max 中心，抓取格只做形状校验；UpdateDrag 使用棋盘 InverseTransformPoint 和 MapGridMath.GetCenteredAnchor 获取最近锚点。鼠标命中格继续单独用于合成。RobotDragController.Release 刷新最终图像/预览，提交阶段不再重算锚点。RobotDragGhostWidget、场景、Prefab、相机、地图及战斗业务未修改。

修改的代码共五份：MapGridMath.cs、PlacementController.cs、RobotDragController.cs、BoardModelTests.cs、RobotMergePlayVerification.cs。后者统一部署点为锚点加形状中心，保留合成点的实际格投影，并增加无 Move 的 PointerUp 图像中心检查和非法最终落点回位检查。

Unity 2021.3.50f1 编译完成。BoardModelTests、RobotDragTransactionTests、RobotDragBallTests、ShopBoardModelTests 共92项 EditMode 测试全部通过（MCP job 129fd561f7f940f08a0c673f1deb9af1，passed=92，failed=0）。覆盖奇偶行列、三种格距、横竖条/矩形/L/T形、半格中心与平局、非零最小偏移、旋转缩放棋盘、抓取格不影响锚点、鼠标格合成、占用变化与原事务回归。

通过 TEngine 工具栏真实 Launcher 的 SceneHelper.StartScene(main) 启动框架和业务 Main；RobotMergePlayVerification 10组真实流程全部通过，包括四来源、扣费、合成、暂存、取消晚到加载、终局/新局隔离，以及最终 PointerUp 图像/预览/落地一致。独立临时 Library 夹具再检查配置501、502、503、504、505、601、602、603的实际形状，每种遍历所有真实占格作为抓取格，确认中心锚点一致并正确落地。补充夹具曾因过早冻结而忽略玻璃碰撞，及因凹形空缺命中已放置的其他机器人而触发原合成分支；调整夹具冻结顺序、使用空闲区域后，剩余检查通过，无需改生产代码。补充验证期间出现 PlayMode 退出，中断的检查未计为通过；501～601六种与602/603两种分批完成。

本地证据：Library/RobotMergeVerification.json、Library/robot-center-shapes-summary.txt、Library/robot-center-shapes.txt、Library/robot-center-last-shapes.txt。临时夹具只改当前运行实例，不保存任何资源。

最终 node scripts/verify-unity-mcp-gate.mjs 通过（快照过期时使用门禁自带只读实时探针确认），Console errors=0。Main 已停止运行、未暂停、未编译、无场景脏改动；本次文件 git diff --check 通过。实施前 .codex 和 AGENTS.md 修改保持原样。

未验证：移动设备实机、所有屏幕分辨率与全部地图尺寸的 Launcher 交互组合；不同格距/奇偶棋盘与缩放旋转由自动化几何及控制器测试覆盖。
