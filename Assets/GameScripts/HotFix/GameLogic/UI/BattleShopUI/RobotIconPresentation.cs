using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>等级底板、边框与原色立绘同步加载；底部金色星星显示局内等级。</summary>
    internal sealed class RobotIconPresentation : IDisposable
    {
        private readonly Image _background;
        private readonly Image _portrait;
        private readonly Image _frame;
        private readonly Image[] _stars;
        private CancellationTokenSource _loading;
        private string _location;
        private bool _visible;
        private int _level;
        private int[][] _cells;

        public RobotIconPresentation(Image background)
        {
            _background = background;
            _portrait = background.transform.Find("m_img_RobotPortrait").GetComponent<Image>();
            _portrait.color = Color.white;
            _portrait.raycastTarget = false;
            _frame = background.transform.Find("m_img_LevelFrame").GetComponent<Image>();
            _frame.raycastTarget = false;
            _stars = background.transform.Find("m_rect_LevelStars").GetComponentsInChildren<Image>(true);
        }

        public void Render(string location, int level, bool visible)
        {
            _visible = visible && !string.IsNullOrEmpty(location);
            _level = Mathf.Clamp(level, 1, RobotLevelStyle.MaxStars);
            _background.color = RobotLevelStyle.ColorForLevel(level);
            _portrait.color = Color.white;
            _frame.color = RobotLevelStyle.FrameColorForLevel(_level);
            if (_location != location)
            {
                CancelLoad();
                _location = location;
                _cells = null;
                _background.sprite = null;
                _portrait.sprite = null;
                _frame.sprite = null;
                if (!string.IsNullOrEmpty(location))
                {
                    foreach (var config in ConfigSystem.Instance.Tables.TbRobot.DataList)
                        if (config.RobotIcon == location) { _cells = config.ShapeId_Ref.CellOffsets; break; }
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
            RefreshStars();
        }

        private void RefreshStars()
        {
            var visible = _visible && _background != null && _background.sprite != null &&
                _portrait != null && _portrait.sprite != null;
            foreach (var star in _stars) if (star != null) star.enabled = visible;
            if (!visible) return;
            var sprite = _background.sprite;
            var rect = _background.GetPixelAdjustedRect();
            var size = rect.size;
            if (_background.preserveAspect)
            {
                var scale = Mathf.Min(size.x / sprite.rect.width, size.y / sprite.rect.height);
                size = sprite.rect.size * scale;
            }
            var pixelScale = new Vector2(size.x / sprite.rect.width, size.y / sprite.rect.height);
            var pixelsPerCell = sprite.pixelsPerUnit * MapGridMath.REFERENCE_CELL_SIZE;
            var origin = rect.center - size * 0.5f;
            var area = RobotLevelStyle.StarArea(_cells);
            var starSize = RobotLevelStyle.StarSpriteSize * RobotLevelStyle.StarScaleForCells(_cells)
                * pixelsPerCell * Mathf.Min(pixelScale.x, pixelScale.y);
            var spacing = RobotLevelStyle.StarSpacingForCells(_cells) * pixelsPerCell * pixelScale.x;
            var center = origin + Vector2.Scale(sprite.pivot + new Vector2(area.center.x, area.yMin) * pixelsPerCell, pixelScale)
                + Vector2.up * (starSize * 0.5f);
            for (var i = 0; i < _stars.Length; i++)
            {
                var star = _stars[i];
                star.enabled = i < _level;
                star.color = Color.white;
                var transform = star.rectTransform;
                transform.anchorMin = transform.anchorMax = _background.rectTransform.pivot;
                transform.anchoredPosition = center + Vector2.right * ((i - (_level - 1) * 0.5f) * spacing);
                transform.sizeDelta = Vector2.one * starSize;
            }
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
