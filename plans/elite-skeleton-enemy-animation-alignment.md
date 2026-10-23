---
slug: elite-skeleton-enemy-animation-alignment
版本: 1
状态: completed
创建: 2026-10-22
---

# EliteSkeletonEnemy 动画对齐计划V1

## 代码逻辑设计

当前 EliteSkeletonEnemy 的四个 AnimationClip 都只替换 `Visual` 子节点 SpriteRenderer 的 `m_Sprite`，没有 Transform 曲线。Prefab 根节点负责运行时敌人位置和缩放，Visual 负责渲染；因此校准曲线绑定到 `Visual`，不修改根节点、战斗坐标、碰撞、攻击点或路线逻辑。

四组图片均为单 Sprite、PPU 100、中心 Pivot，但实际画布和 Alpha 内容边界不同：Idle 画布 1254×1254、有效高度约 959～1001；Attack 560×752、有效高度约 449～497；Death 560/589×752、有效高度约 207～345；Move 834×1112、有效高度约 873～881。以 Idle 第一帧 `1 (1).png` 为固定参考，保持 Idle 原始 Transform 不变。

对 Move、Attack、Death 逐帧读取 PNG Alpha 有效边界，以 `targetHeight / frameHeight` 生成等比 `m_LocalScale.x/y` 曲线，并根据有效内容底边和中心生成 `m_LocalPosition.y/x` 曲线。曲线使用常量切线，重复保持帧沿用相同值，避免 Sprite 切换时插值变形和循环跳变。Death 前段进行高度校准，倒地/收缩阶段保留自然变矮效果，但继续校准脚底位置。保留所有现有 Sprite 曲线、Animator 状态和攻击/死亡事件。

## 计划改动

仅修改 `Assets/AssetRaw/Actor/Enemy/Animations/EliteSkeletonEnemy/` 下四个 `.anim` 文件：Move、Attack、Death 增加 Visual 的 Scale/Position 曲线，Idle 保持不变。通过 Unity Editor API 和 PNG 原始字节计算 Alpha 边界，不修改 TextureImporter、源 PNG、Prefab 或 AnimatorController，不手写 YAML。

验收包括：Idle 外观不变；Move/Attack 高度和脚底与 Idle 对齐；Death 前段稳定、倒地自然收缩；循环首尾无跳变；攻击/死亡事件、敌人根节点位置、路线、排序和回收逻辑不变。实施前须通过 Unity MCP 确认目标工程为 `C:/Users/Admin/FreshGuard`，完成后等待编译、检查 Console，并使用 TEngine Launcher 实际播放验证。

## 执行Todo

- [x] 确认 Unity MCP 目标工程和编辑器就绪状态。
- [x] 记录当前资源状态并通过 Editor API 读取四个动画和 PNG Alpha 边界。
- [x] 生成并保存 Move、Attack、Death 的 Visual Transform 曲线。
- [x] 静态检查动画绑定、事件、控制器和 Prefab 未受影响。
- [x] 使用 TEngine Launcher 播放验证并检查 Console。
- [x] 运行相关测试、门禁和 diff 检查，记录未验证项。

## 执行记录

2026-10-23 完成。Idle 的视觉和原始变换保持不变，但因所有状态 Write Defaults 关闭，为其加入原始变换常量键，保证其他状态结束和对象重新启用时恢复 Idle。四个片段统一写入 Visual 的 localScale.xyz、localPosition.xyz，Z 固定为原值，XY 等比；压缩冗余的常量键。Death 全程沿用第一帧倍率 2.814493，后三帧不重新放大，保留倒地自然收缩。原 Sprite 时间、帧引用、片段长度、循环设置和动画事件逐项对比未改变。

保存后发现 Unity 导入缓存仍引用旧曲线；已通过 Editor API 对四个片段定向重新序列化并导入，确认文件、导入对象及域重载后的实际 Animator 均包含曲线。未改动 Prefab、Controller、PNG、导入设置、业务代码和场景文件。

已通过 41 项现有敌人 EditMode 测试、26 个动画帧的 90 次实际采样、TEngine Launcher 内独立临时敌人实例的状态/事件/回收通知测试、1280×720 和 720×1280 离屏渲染对齐检查及 Unity MCP 门禁（Console errors=0）。完整自然波次战斗和设备实机未穷尽验证。详细结果见 `summaries/elite-skeleton-enemy-animation-alignment.md`。
