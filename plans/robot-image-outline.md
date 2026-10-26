---
slug: robot-image-outline
版本: 2
状态: completed
创建: 2026-10-23
---

# 机器人图片描边计划V2

## 一、代码逻辑设计

使用现有 `Assets/AssetRaw/Actor/Robots/Icons/heibian.png` 拼接八种机器人实际占格外边缘，不生成轮廓图片、Mesh 或 Shader，不修改源图、商店、暂存位或拖拽 UI。

黑条为 54×3 像素的完全不透明纯黑横条，单 Sprite，PPU 100，中心 Pivot，Bilinear/Clamp，无 Mipmap，Border 为零。保留导入设置，将其作为边段调整长度、厚度和旋转，不能放大覆盖机器人整体。

以 RobotScatter 为样例：Cell_0_0、Cell_1_0、Cell_0_1 构成 L 形，与图标和配置核对。格距 .35，现有格子可见边长 .329。每格四方向没有邻格时添加外边段，有邻格时不添加边段。L 形八条单格外边保留右上凹陷及现有格间缝隙。边段在占格边界内侧，初始厚度 .018；转角衔接，必要时用同一黑条补片，但不得填充未占格区域。

每个 Prefab 根下新增 ShapeOutline 容器，局部位置零、缩放一；其 SpriteRenderer 边段使用与 Cell 相同的 Sorting Layer，Order 高于格子、低于 LevelLabel。无碰撞、刚体和输入组件，自然继承机器人及棋盘缩放。

RobotView 区分实际 Cell_* 与 ShapeOutline 渲染器。实际格子沿用等级色、合成高亮和反馈；黑边只同步现有可见条件（未移除、位于棋盘、未拖拽），不参与染色。销毁随根节点清理，攻击原点、占格与碰撞逻辑不变。

八个 Prefab 已通过 Editor 装配完成。按用户后续确认，为减少代码改动，移除新增的 RobotOutlinePrefabBuilder.cs 及其 meta，不再保留图片描边重建菜单。Prefab 中的 ShapeOutline、黑条图片、RobotView、碰撞装配过滤及相关测试全部保留。碰撞装配工具及内容测试只统计实际 Cell，避免将描边当作实体格。

验收：八种形状外边清晰、L/T/折线凹陷保留；等级/合成不改黑边；拖拽隐藏和恢复、移除无残影；棋盘缩放下描边对齐；碰撞、攻击与部署保持原样。资源操作仅通过目标工程 Unity MCP/Editor API；等待编译、Console 检查，并使用 TEngine Launcher 实际验证。

## 二、计划改动

相较 V1，描边装配完成后删除新增 Editor 工具及其 meta；保留八个已装配 Prefab 和其余配套代码，以减少长期维护的代码。

## 执行Todo

- [x] 确认 MCP 目标和就绪状态，记录当前资源基准。
- [x] 分离 RobotView 实体格与描边显示，调整碰撞装配及相关检查。
- [x] 新增幂等 Editor 图片描边装配工具，装配八个 Prefab。
- [x] 编译、相关测试、Launcher 显示/生命周期验证和门禁。
- [x] 根据后续指令删除 Editor 图片描边装配工具，确认其余文件不变、编译及门禁通过。

## 文件边界

V1 允许修改：八个机器人 Prefab；RobotView.cs；RobotCollisionPrefabBuilder.cs；相关机器人内容/显示测试；新增 Editor 装配脚本及其 Unity 生成的 meta。
V2 执行边界：仅删除 RobotOutlinePrefabBuilder.cs 及 meta；更新本计划执行记录。其他已完成产物只读保留。
只读：源 PNG 和导入设置、配置表、场景、商店及拖拽 UI、碰撞/战斗业务逻辑。保留实施前用户已有图标和 Prefab 修改。

## 校验与恢复

V1 实施前备份八个当前 Prefab 到 Library/RobotOutlineBackup，仅用于恢复原有用户资源。V2 删除工具前记录已装配 Prefab、图片与保留代码哈希；通过 AssetDatabase.DeleteAsset 删除脚本及 meta。中断后检查脚本是否仍存在，再恢复验证；不重装描边、不恢复旧 Prefab，不使用 git reset。新边段必须引用原 heibian Sprite，无额外 Collider/Rigidbody；形状、Cell Transform、Prefab 根、LevelLabel 与基准一致。

## 执行记录

2026-10-23 完成。通过目标为 C:/Users/Admin/FreshGuard 的 Unity MCP execute_code 调用 Editor API 装配八个 Prefab。每个 Prefab 新增 ShapeOutline，边段数（含凹角补片）为 Cannon 6、Scatter 9、Piercing 8、RapidFire 6、Impact 12、Frost 8、Tractor 9、Poison 12。厚度 .018，排序 Order 1，原等级文字 Order 15。所有边段直接引用现有 heibian Sprite，复用 Sprites/Default 材质，无新纹理、材质或碰撞组件。

RobotView 只缓存根下 Cell_* 作为染色格子，单独缓存 ShapeOutline 渲染器并同步 visible。碰撞装配及内容测试同样区分实际 Cell。Editor 工具复用同名边段，清理过期边段；重复执行图片装配和碰撞装配均保持八个文件字节不变。

验证：67 项 RobotContentTests、RobotMergePresentationTests、RobotCombatTests、RobotCombatControllerTests 完成且无失败；八种机器人覆盖拖拽、暂存、升级高亮、移除及凹陷采样。临时 PreviewScene 验证三种格距×四种棋盘缩放，共 840 次边段位置/缩放检查，通过且重复 ApplyBoardScale 不累计。通过 TEngine 工具栏 Launcher 实际进入主战斗；现有 RobotMergePlayVerification 的10组真实商店/部署/合成/回位/暂存/终局检查全部通过。运行监测覆盖 config 501、504，共 17532 次边段显示/颜色检查（含84次隐藏），无异常；其他六种的生命周期由 EditMode 测试覆盖，未逐种执行 Launcher 购买流程。

Unity 编译完成，最终门禁 PASS，Main 场景恢复为停止运行、无脏改动，Console errors=0。原有 Prefab 序列化对象逐项与 Library/RobotOutlineBackup 对比一致，仅根子列表扩展；源 PNG 及 meta 哈希全部保持实施前值。源码 diff 空白检查通过；全量 git diff --check 有 Unity 重排原有 RobotImpact MonoBehaviour 空字段产生的4条尾空格提示，未手写清理资源。

本地证据：Library/robot-outlines-comparison.png（上原始、下描边）、Library/RobotMergeVerification.json、Library/robot-outline-runtime-monitor.txt、Library/robot-outline-scale-verification.txt。未验证移动设备实机和全部分辨率。

V2 后续精简完成：按用户明确要求，通过 Unity MCP 调用 AssetDatabase.DeleteAsset 删除 RobotOutlinePrefabBuilder.cs 及 meta。删除前后哈希核对，八个已装配 Prefab、所有机器人图片及导入设置、RobotView、碰撞装配工具和两份测试代码逐字节不变。Unity 完成重新编译，MCP 门禁 PASS，Console errors=0。没有重新执行 Launcher 或功能测试；其余运行时和预制体文件未改，V1 的功能验证记录保留。以后不再提供 FreshGuard/Robots/Build Image Outlines 菜单，修改机器人形状时需自行维护预制体中的边段。
