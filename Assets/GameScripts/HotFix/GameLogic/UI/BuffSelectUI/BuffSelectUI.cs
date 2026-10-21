using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>仅展示候选并通知结果；调用方负责暂停对局、查询配置和执行强化。</summary>
    [Window(UILayer.Top, "BuffSelectUI", fullScreen: false)]
    public sealed class BuffSelectUI : UIWindow
    {
        private BuffSelectItemWidget[] _items;
        private Text _countdown;
        private BuffSelectRequest _request;
        private BuffSelectSession _session;

        protected override void ScriptGenerator()
        {
            _countdown = FindChildComponent<Text>("m_rect_Content/m_text_Countdown");
        }
        protected override void OnCreate()
        {
            _items = new BuffSelectItemWidget[3];
            for (var i = 0; i < _items.Length; i++)
                _items[i] = CreateWidget<BuffSelectItemWidget>("m_rect_Content/m_rect_Cards/m_item_Card" + i);
        }
        protected override void OnRefresh()
        {
            StopSession();
            _request = UserData as BuffSelectRequest;
            if (_request == null)
            {
                // 无参数时保持静态美术预览，不生成一次真实强化选择。
                _countdown.text = "请传入三个强化候选";
                return;
            }
            _session = new BuffSelectSession(_request);
            for (var i = 0; i < _items.Length; i++) _items[i].Render(_request.Options[i], i, Select);
            RefreshCountdown();
        }
        protected override void OnUpdate()
        {
            if (_session == null || !_session.IsActive) return;
            // 暂停战斗 Time.timeScale=0 时，选择倒计时仍然前进。
            if (_session.Advance(Time.unscaledDeltaTime, out var result)) Complete(result);
            else RefreshCountdown();
        }
        private void Select(int index)
        {
            if (_session != null && _session.TrySelect(index, out var result)) Complete(result);
        }
        private void Complete(BuffSelectResult result)
        {
            var callback = _request?.OnSelected;
            StopSession();
            // 先关闭旧窗口，回调可以安全打开下一次选择。
            Close();
            callback?.Invoke(result);
        }
        private void RefreshCountdown()
        {
            _countdown.text = "<color=#E7B722>" + Mathf.CeilToInt(_session.RemainingSeconds) + "</color>秒后自动选择";
        }
        private void StopSession()
        {
            _session?.Cancel();
            _session = null;
            _request = null;
            if (_items != null) foreach (var item in _items) item?.StopSelection();
        }
        // 临时遮挡由 UIWindow 停止更新和射线交互；恢复可见后继续原会话。
        // 真正关闭走 OnDestroy，新的候选请求走 OnRefresh，两者才取消旧会话。
        protected override void OnDestroy()
        {
            StopSession();
        }
    }
}
