using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>战斗信息展示组件。波次与整局时间由调用方传入，不自行计时。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleInfoView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private RectTransform _panel;
        [Header("Player")]
        [SerializeField] private Image _playerAvatar;
        [SerializeField] private Text _playerAvatarPlaceholder;
        [SerializeField] private Text _playerName;
        [SerializeField] private Text _playerTrophies;
        [Header("Enemy")]
        [SerializeField] private Image _enemyAvatar;
        [SerializeField] private Text _enemyAvatarPlaceholder;
        [SerializeField] private Text _enemyName;
        [SerializeField] private Text _enemyTrophies;
        [Header("Battle")]
        [SerializeField] private Text _wave;
        [SerializeField] private Text _elapsedTime;

        private void Awake() { FindPanel(); }

        /// <summary>设置参考尺寸 1040×156 的信息栏在窗口中心坐标系中的位置和缩放。</summary>
        public void SetLayoutPosition(Vector2 localPosition, float scale)
        {
            FindPanel();
            if (_panel == null) return;
            _panel.anchorMin = _panel.anchorMax = Vector2.one * 0.5f;
            _panel.pivot = Vector2.one * 0.5f;
            _panel.sizeDelta = new Vector2(1040, 156);
            _panel.anchoredPosition = localPosition;
            _panel.localScale = Vector3.one * Mathf.Clamp01(float.IsNaN(scale) ? 0f : scale);
        }

        private void FindPanel()
        {
            // 兼容尚未通过 Editor 补齐新序列化字段的旧 Prefab。
            if (_panel == null)
                _panel = transform.Find("m_rect_BattleInfo") as RectTransform;
        }

        public void SetPlayerInfo(string name, int trophies, Sprite avatar = null)
        {
            _playerName.text = name ?? string.Empty;
            _playerTrophies.text = Mathf.Max(0, trophies).ToString();
            _playerAvatar.sprite = avatar;
            _playerAvatar.enabled = avatar != null;
            _playerAvatarPlaceholder.enabled = avatar == null;
        }
        public void SetEnemyInfo(string name, int trophies, Sprite avatar = null)
        {
            _enemyName.text = name ?? string.Empty;
            _enemyTrophies.text = Mathf.Max(0, trophies).ToString();
            _enemyAvatar.sprite = avatar;
            _enemyAvatar.enabled = avatar != null;
            _enemyAvatarPlaceholder.enabled = avatar == null;
        }
        public void SetBattleProgress(int currentWave, float totalElapsedSeconds)
        {
            _wave.text = "波次 " + Mathf.Max(1, currentWave);
            _elapsedTime.text = FormatElapsedTime(totalElapsedSeconds);
        }
        public static string FormatElapsedTime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "00:00";
            var total = (int)System.Math.Min(int.MaxValue, System.Math.Max(0d, seconds));
            // 总分钟数不在 60 分钟处回绕；波次变化不影响累计时长。
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
