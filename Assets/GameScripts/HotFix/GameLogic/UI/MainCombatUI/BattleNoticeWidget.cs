using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>战斗内共用提示：中央停留后匀速上浮淡出，使用不受暂停影响的时间。</summary>
    public sealed class BattleNoticeWidget : UIWidget
    {
        public const float HoldDuration = 0.5f;
        public const float FadeDuration = 0.65f;
        public const float RiseDistance = 90f;
        public const int MaxPendingNotices = 3;

        private readonly Queue<string> _pending = new Queue<string>();
        private RectTransform _panel;
        private CanvasGroup _group;
        private Text _label;
        private string _current;
        private float _elapsed;

        protected override void ScriptGenerator()
        {
            _panel = (RectTransform)transform.Find("Notice");
            _group = _panel.GetComponent<CanvasGroup>();
            _label = _panel.GetComponentInChildren<Text>(true);
        }

        protected override void OnCreate() { Clear(); }

        public void Show(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || _panel == null ||
                message == _current || _pending.Contains(message)) return;
            if (_current == null) Begin(message);
            else
            {
                // 限制积压，连续操作时优先保留最新的反馈。
                if (_pending.Count >= MaxPendingNotices) _pending.Dequeue();
                _pending.Enqueue(message);
            }
        }

        private void Begin(string message)
        {
            _current = message;
            _elapsed = 0f;
            _label.text = message;
            var availableWidth = Mathf.Max(1f, rectTransform.rect.width - 96f);
            var width = Mathf.Min(availableWidth, Mathf.Max(560f, _label.preferredWidth + 144f));
            _panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            _panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(132f, _label.preferredHeight + 48f));
            _panel.anchoredPosition = Vector2.zero;
            _group.alpha = 1f;
            _panel.gameObject.SetActive(true);
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            while (_current != null)
            {
                var step = Mathf.Min(deltaTime, HoldDuration + FadeDuration - _elapsed);
                _elapsed += step;
                deltaTime -= step;
                var progress = Mathf.Clamp01((_elapsed - HoldDuration) / FadeDuration);
                _panel.anchoredPosition = Vector2.up * (RiseDistance * progress);
                _group.alpha = 1f - progress;
                if (_elapsed < HoldDuration + FadeDuration) break;
                if (_pending.Count > 0) Begin(_pending.Dequeue());
                else Clear();
                if (deltaTime <= 0f) break;
            }
        }

        public void Clear()
        {
            _pending.Clear();
            _current = null;
            _elapsed = 0f;
            if (_panel == null) return;
            _group.alpha = 0f;
            _panel.anchoredPosition = Vector2.zero;
            _label.text = string.Empty;
            _panel.gameObject.SetActive(false);
        }

        protected override void OnUpdate() { Advance(Time.unscaledDeltaTime); }
        protected override void OnSetVisible(bool visible) { if (!visible) Clear(); }
        protected override void OnDestroy() { Clear(); }
    }
}
