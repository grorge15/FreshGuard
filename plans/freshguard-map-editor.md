---
slug: freshguard-map-editor
版本: 4
状态: executing
创建: 2026-10-21
---

# FreshGuard 地图编辑器计划V4

## 代码逻辑设计

本版本为用户确认的V4分层改造。每格由基础地形与障碍共同描述；玻璃破碎只移除障碍，保留底层。复用现有坐标、多格部署、占用、实体、物理碰撞、耐久和奖励。保留默认9行11列、格距0.35、1～64尺寸、固定相机及11列×9行基准等比缩放。

新增BaseTerrainType:byte（Track=1、Empty=2），ObstacleType:byte（None=0、GlassNormal=1、GlassColored=2），[Serializable]值类型MapCellData，包含BaseTerrain与Obstacle。MapData使用MapCellData[] Cells替代Terrains，其他元数据保留，索引Row*Columns+Column；Clone深复制，Resize保留左下完整两层，新建/重置/扩展均Empty+None。

MapData提供GetBaseTerrain、GetObstacle、SetBaseTerrain、SetObstacle。写入只改指定层。CanPlaceUnit仅Empty+None为true、不混入机器人占用；CanEnergyBallPass要求有效基础地形且无障碍。越界布尔false、读取及无效写入抛明确异常。删除业务旧GetTerrain/SetTerrain，四种旧编号只用于V1解码。

BoardModel继续独立复制地图，IsOpen使用综合许可，Evaluate复用完整形状/边界/占用。新增地图模式分层读取与internal TryRemoveObstacle；移除轨道上的障碍也算成功。地图模式SetOpen只判断请求与许可一致，不再修改两层。无地图地址的旧Luban模式保留原SetOpen与保留边框规则；无完整分层数据时读取新层接口明确报错。

BoardLayoutRules提供初始基础地形和障碍，HasInitialGlass/GetGlassConfigId依障碍查询。GlassBoardController在原实体确认Broken后：BeginBreak立即禁碰撞、仅清除本方障碍、更新索引/底图、原奖励事件、原淡出销毁。旧布局仍SetOpen(true)。保留实体去重/奖励去重，不新增破碎事件。双方副本独立，新局从模板恢复，破碎不写文件。PlacementBoardView底色按基础地形显示，玻璃覆盖其上，部署预览复用综合许可。

编辑器复用MapEditorWindow/Document/GlassAssets、Bresenham、矩形与Undo。基础画笔Track/Empty、障碍画笔None/普通/彩色、重置格子工具。左键只改当前层，右键统一只擦障碍；矩形/全图填充遵循当前工具。重置全图及重置格子同时Empty+None；手势开始锁定画笔和操作，整笔一步Undo。预览基础底色+居中玻璃图标，保留底色边框；坐标信息同时显示两层。ContentEquals、裁剪、Undo/Redo、窗口恢复完整保存两层；任一层修改均正确标记未保存。

FGMP文件升级V2，保留30字节小端头：标识、UInt16版本、Int32列/行、Float32格距、Int32普通/彩玻ID、Int32格数。V2行优先每格2字节（基础、障碍），总长30+2*Rows*Columns，不直接序列化结构体内存。V1四编号迁移为Track+None、Empty+None、Empty+普通、Empty+彩色；读取统一返回Version=2，保存仅V2。严格拒绝未知版本/无效编号/尺寸/截断/尾随。Deserialize(byte[])保留，新增out源版本重载；编辑器显示V1内存迁移提示，不因加载标脏、不自动写回。MapLoader异步接口不变，调整文件长度上限支持最大V2。禁止批量覆盖用户地图，不生成JSON。

MapValidator分别校验两层编号，可部署统计用综合查询，连通/净空/窄通道继续现有规则。修改MapData/Serializer/Loader/Validator、BoardModel/LayoutRules/GlassController/PlacementBoardView、EditorWindow/Document/GlassAssets及现有验证；不新建部署、碰撞、奖励服务，不改缩放公式。

验收覆盖两种玻璃分别在Track/Empty四组合：破碎前禁部署并阻挡；Track破碎后仍禁部署但球通行，Empty破碎后可部署且仍受占用/形状约束。验证原耐久/奖励/去重/双方隔离/新局恢复；两层独立画笔、擦除、重置、填充、裁剪、Undo；V1四类型无写回迁移、V2六组合/最大尺寸往返及损坏拒绝。使用TEngine Launcher真实验证四组合及旧布局、固定相机与缩放；独立临时测试资源不覆盖用户图。

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

- [ ] 9. V4两层数据、V2格式及V1兼容。
- [ ] 10. V4部署/破碎/底图接入。
- [ ] 11. V4编辑器两层工具、预览、撤销与源版本提示。
- [ ] 12. V4自动测试、Launcher验证、文档与门禁。

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
相较V3，将互斥的四类地形改为“基础地形＋障碍物”，玻璃破碎只清除障碍；文件升级为V2，并兼容读取V1。保留固定相机、11列×9行基准缩放及现有部署、碰撞和奖励流程。
