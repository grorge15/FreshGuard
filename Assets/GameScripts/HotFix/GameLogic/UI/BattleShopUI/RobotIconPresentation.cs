using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>等级底板、边框与原色立绘同步加载。</summary>
    internal sealed class RobotIconPresentation : IDisposable
    {
        private readonly Image _background;
        private readonly Image _portrait;
        private readonly Image _frame;
        private Image _unaffordableOverlay;
        private CancellationTokenSource _loading;
        private string _location;
        private bool _visible;
        private bool _unaffordable;

        public RobotIconPresentation(Image background)
        {
            _background = background;
            _portrait = background.transform.Find("m_img_RobotPortrait").GetComponent<Image>();
            _portrait.color = Color.white;
            _portrait.raycastTarget = false;
            _frame = background.transform.Find("m_img_LevelFrame").GetComponent<Image>();
            _frame.raycastTarget = false;
        }

        public void Render(string location, int level, bool visible, bool unaffordable = false)
        {
            _visible = visible && !string.IsNullOrEmpty(location);
            _unaffordable = unaffordable;
            var visualLevel = Mathf.Clamp(level, 1, RobotLevelStyle.MaxLevel);
            _background.color = RobotLevelStyle.ColorForLevel(level);
            _portrait.color = Color.white;
            _frame.color = RobotLevelStyle.FrameColorForLevel(visualLevel);
            if (_location != location)
            {
                CancelLoad();
                _location = location;
                _background.sprite = null;
                _portrait.sprite = null;
                _frame.sprite = null;
                if (!string.IsNullOrEmpty(location))
                {
                    _loading = CancellationTokenSource.CreateLinkedTokenSource(
                        _background.gameObject.GetCancellationTokenOnDestroy());
                    _background.SetSprite(location, callback: _ => RefreshVisibility(), cancellationToken: _loading.Token);
                    _portrait.SetSprite(RobotLevelStyle.PortraitForIcon(location), callback: _ => RefreshVisibility(),
                        cancellationToken: _loading.Token);
                    _frame.SetSprite(RobotLevelStyle.FrameForIcon(location), callback: _ => RefreshVisibility(),
                        cancellationToken: _loading.Token);
                }
            }
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (_background != null) _background.enabled = _visible && _background.sprite != null;
            if (_portrait != null) _portrait.enabled = _visible && _background != null &&
                _background.sprite != null && _portrait.sprite != null;
            if (_frame != null) _frame.enabled = _visible && _background != null &&
                _background.sprite != null && _frame.sprite != null;
            RefreshUnaffordableOverlay();
        }

        private void RefreshUnaffordableOverlay()
        {
            if (_unaffordableOverlay == null && _unaffordable && _background != null && _background.enabled)
            {
                // 复用占格底板的轮廓，只压暗机器人区域，价格和货币图标保持原样。
                var overlay = new GameObject("m_img_UnaffordableOverlay", typeof(RectTransform), typeof(Image));
                overlay.layer = _background.gameObject.layer;
                overlay.transform.SetParent(_background.transform, false);
                _unaffordableOverlay = overlay.GetComponent<Image>();
                var rect = _unaffordableOverlay.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _unaffordableOverlay.color = new Color(0f, 0f, 0f, 0.3f);
                _unaffordableOverlay.raycastTarget = false;
                _unaffordableOverlay.preserveAspect = _background.preserveAspect;
                _unaffordableOverlay.useSpriteMesh = _background.useSpriteMesh;
                _unaffordableOverlay.maskable = _background.maskable;
            }
            if (_unaffordableOverlay == null) return;
            _unaffordableOverlay.sprite = _background != null ? _background.sprite : null;
            _unaffordableOverlay.enabled = _unaffordable && _background != null && _background.enabled;
        }

        private void CancelLoad()
        {
            if (_loading == null) return;
            _loading.Cancel();
            _loading.Dispose();
            _loading = null;
        }

        public void Dispose()
        {
            _visible = false;
            CancelLoad();
            RefreshVisibility();
        }
    }
}
