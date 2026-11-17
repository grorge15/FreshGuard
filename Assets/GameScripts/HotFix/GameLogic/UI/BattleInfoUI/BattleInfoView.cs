using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        [Header("Assists (left to right)")]
        [SerializeField] private Sprite[] _buffQualityFrames = new Sprite[4];
        [SerializeField] private Image[] _playerBuffFrames = new Image[0];
        [SerializeField] private Image[] _playerBuffIcons = new Image[0];
        [SerializeField] private Text[] _playerBuffLevels = new Text[0];
        [SerializeField] private Image[] _enemyBuffFrames = new Image[0];
        [SerializeField] private Image[] _enemyBuffIcons = new Image[0];
        [SerializeField] private Text[] _enemyBuffLevels = new Text[0];
        private CancellationTokenSource _buffLoads;

        public const int BUFF_CAPACITY = 6;
        private const float BUFF_SPACING = 49f;

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

        /// <summary>显示本局携带助力的图标、品质底框及大于一级的等级；空槽不显示。</summary>
        public void SetAssistBuffs(IReadOnlyList<MatchBuff> player, IReadOnlyList<MatchBuff> enemy)
        {
            CancelBuffLoads();
            _buffLoads = CancellationTokenSource.CreateLinkedTokenSource(gameObject.GetCancellationTokenOnDestroy());
            RenderBuffs(_playerBuffFrames, _playerBuffIcons, _playerBuffLevels, player, _buffLoads.Token);
            RenderBuffs(_enemyBuffFrames, _enemyBuffIcons, _enemyBuffLevels, enemy, _buffLoads.Token);
        }

        private void RenderBuffs(Image[] frames, Image[] icons, Text[] levels, IReadOnlyList<MatchBuff> buffs, CancellationToken token)
        {
            var count = Mathf.Min(buffs != null ? buffs.Count : 0, icons.Length);
            var startX = -(count - 1) * BUFF_SPACING * .5f;
            for (var i = 0; i < icons.Length; i++)
            {
                var buff = buffs != null && i < buffs.Count ? buffs[i] : null;
                var quality = buff != null ? (int)buff.Quality : -1;
                var frame = frames[i];
                frame.sprite = quality >= 0 && quality < _buffQualityFrames.Length ? _buffQualityFrames[quality] : null;
                frame.enabled = frame.sprite != null;
                var icon = icons[i];
                var slot = (RectTransform)icon.transform.parent;
                slot.anchoredPosition = new Vector2(startX + i * BUFF_SPACING, slot.anchoredPosition.y);
                icon.sprite = null;
                icon.enabled = buff != null && !string.IsNullOrEmpty(buff.IconLocation);
                if (icon.enabled) icon.SetSprite(buff.IconLocation, cancellationToken: token);
                var level = levels[i];
                var showLevel = buff != null && buff.Layers > 1;
                level.text = showLevel ? buff.Layers + "级" : string.Empty;
                level.gameObject.SetActive(showLevel);
            }
        }

        private void CancelBuffLoads()
        {
            if (_buffLoads == null) return;
            _buffLoads.Cancel();
            _buffLoads.Dispose();
            _buffLoads = null;
        }

        private void OnDestroy() { CancelBuffLoads(); }
        public static string FormatElapsedTime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "00:00";
            var total = (int)System.Math.Min(int.MaxValue, System.Math.Max(0d, seconds));
            // 总分钟数不在 60 分钟处回绕；波次变化不影响累计时长。
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
