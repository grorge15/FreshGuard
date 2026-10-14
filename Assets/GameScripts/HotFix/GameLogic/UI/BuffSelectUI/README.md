# 强化选择 UI

预制体在 `Assets/AssetRaw/UI/BuffSelect/`：

- `BuffSelectUI.prefab`：固定标题、三张嵌套卡片、固定倒计时和阻挡点击的遮罩。
- `BuffSelectItem.prefab`：共用卡片模板。`BuffSelectItemStyle` 保存蓝、紫、金三张背景引用。

位置、尺寸、字体和文字区域都可以在 Prefab 中调整；运行时代码只更新候选内容，不重设布局。坐标沿用工程 UIRoot 的 1080×1920 参考分辨率。

调用方查询配置后，把三个候选转换成展示数据，再打开窗口：

```csharp
var options = new[]
{
    new BuffSelectOption(101, 3, BuffQuality.Gold, "强化名称", "强化介绍", icon),
    new BuffSelectOption(102, 1, BuffQuality.Purple, "强化名称", "强化介绍", icon),
    new BuffSelectOption(103, 2, BuffQuality.Blue, "强化名称", "强化介绍", icon)
};
var request = new BuffSelectRequest(options, result =>
{
    // result.Option.BuffId：交给业务层执行强化。
    // result.Index：0、1、2；result.IsAutomatic：是否由倒计时结束选中。
}, durationSeconds: 5f);
await GameModule.UI.ShowUIAsyncAwait<BuffSelectUI>(request);
```

上面的 ID 仅为接口示例；当前未新增 Luban 表或实际强化效果。等级与品质独立，后续表中的品质 ID 由调用方映射为 `BuffQuality`。`icon` 是由调用方准备好的 `UnityEngine.Sprite`，调用方负责保留其资源至窗口关闭；没有图标时显示占位文字。

整张卡片可点击。倒计时使用不受 `Time.timeScale` 影响的时间；到时选第一张。单次会话只返回一次结果，关闭窗口后不再选择。临时被上层全屏窗口遮挡时暂停更新，恢复可见后继续原会话。UI 在通知之前关闭自身，调用方可以在回调中打开下一次选择。无请求参数时只展示静态预览，不启动倒计时。

UI 不负责暂停对局、随机抽取候选、修改属性或创建强化效果对象。这些由业务层处理。

中文字体使用 `Assets/AssetRaw/Fonts/NotoSansSC-Regular.otf`，授权文件放在同目录。生成资源的编辑器菜单为 `FreshGuard/Buff Select/Build Prefabs`，仅创建缺失预制体，保留已有布局和字体。
