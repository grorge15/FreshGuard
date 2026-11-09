using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>订阅按钮确认事件，兼容触摸和键盘提交；不为禁用按钮或按下拖离播放。</summary>
    [DisallowMultipleComponent]
    public sealed class GameButtonSound : MonoBehaviour
    {
        private Button _button;
        public void Bind(Button button)
        {
            if (_button != null) return;
            _button = button;
            _button.onClick.AddListener(OnClick);
        }
        private void OnClick() { GameSoundService.Play(GameSound.UiClick); }
        private void OnDestroy() { if (_button != null) _button.onClick.RemoveListener(OnClick); }
    }
}
