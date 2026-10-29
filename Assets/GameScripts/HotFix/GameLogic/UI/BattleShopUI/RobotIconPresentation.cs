using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>底板单独显示等级；立绘保持原色，两层同步加载和显隐。</summary>
    internal sealed class RobotIconPresentation : IDisposable
    {
        private readonly Image _background;
        private readonly Image _portrait;
        private CancellationTokenSource _loading;
        private string _location;
        private bool _visible;

        public RobotIconPresentation(Image background)
        {
            _background = background;
            _portrait = background.transform.Find("m_img_RobotPortrait").GetComponent<Image>();
            _portrait.color = Color.white;
            _portrait.raycastTarget = false;
        }

        public void Render(string location, int level, bool visible)
        {
            _visible = visible && !string.IsNullOrEmpty(location);
            _background.color = RobotLevelStyle.ColorForLevel(level);
            _portrait.color = Color.white;
            if (_location != location)
            {
                CancelLoad();
                _location = location;
                _background.sprite = null;
                _portrait.sprite = null;
                if (!string.IsNullOrEmpty(location))
                {
                    _loading = CancellationTokenSource.CreateLinkedTokenSource(
                        _background.gameObject.GetCancellationTokenOnDestroy());
                    _background.SetSprite(location, callback: _ => RefreshVisibility(), cancellationToken: _loading.Token);
                    _portrait.SetSprite(RobotLevelStyle.PortraitForIcon(location), callback: _ => RefreshVisibility(),
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
