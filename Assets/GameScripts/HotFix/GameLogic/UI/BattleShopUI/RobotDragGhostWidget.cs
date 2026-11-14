using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class RobotDragGhostWidget : UIWidget
    {
        private const float BoardSizeRatio = 0.8f;
        private const float SnapDuration = 0.05f;
        private Image _icon;
        private RobotIconPresentation _visual;
        private bool _snapping;
        private Vector3 _moveStart;
        private Vector3 _moveTarget;
        private float _moveStartedAt;
        protected override void ScriptGenerator()
        {
            _icon = FindChildComponent<Image>("m_img_RobotIcon");
            _visual = new RobotIconPresentation(_icon);
        }
        public void Show(string icon, string portrait, int level, Vector2 position, Camera camera, float screenCellHeight = 0f)
        {
            Visible = true;
            _visual.Render(icon, portrait, level, true);
            Move(position, camera, screenCellHeight);
            transform.SetAsLastSibling();
        }
        public Vector2 Move(Vector2 position, Camera camera, float screenCellHeight = 0f)
        {
            var center = GetTargetCenter(position, camera, screenCellHeight);
            MoveToCenter(center, camera, false, Time.unscaledTime);
            return center;
        }

        // 计算图标底边对齐触点时的中心，不读取动画中的中心作为摆放输入。
        public Vector2 GetTargetCenter(Vector2 position, Camera camera, float screenCellHeight = 0f)
        {
            ResizeToBoard(position, camera, screenCellHeight);
            var parent = (RectTransform)transform.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, position, camera, out var local)) return position;
            var visual = _icon.rectTransform;
            var bottom = parent.InverseTransformPoint(visual.TransformPoint(new Vector3(visual.rect.center.x, visual.rect.yMin, 0)));
            var center = parent.InverseTransformPoint(visual.TransformPoint(visual.rect.center));
            var target = new Vector3(local.x, local.y, bottom.z) + center - bottom;
            return RectTransformUtility.WorldToScreenPoint(camera, parent.TransformPoint(target));
        }

        public void MoveToCenter(Vector2 position, Camera camera, bool snap, float now)
        {
            var parent = (RectTransform)transform.parent;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, position, camera, out var local)) return;
            var visual = _icon.rectTransform;
            var center = parent.InverseTransformPoint(visual.TransformPoint(visual.rect.center));
            var target = rectTransform.localPosition + new Vector3(local.x - center.x, local.y - center.y, 0f);
            if (!snap)
            {
                _snapping = false;
                rectTransform.localPosition = target;
                return;
            }
            if (!_snapping || (target - _moveTarget).sqrMagnitude > 0.0001f)
            {
                _snapping = true;
                _moveStart = rectTransform.localPosition;
                _moveTarget = target;
                _moveStartedAt = now;
            }
            // 同一目标只推进、不重启动画；逻辑落点始终直接使用预览锚点。
            AdvanceSnap(now);
        }
        public async UniTask FlyToCenterAsync(Vector2 position, Camera camera, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MoveToCenter(position, camera, true, Time.unscaledTime);
            await UniTask.Delay(TimeSpan.FromSeconds(SnapDuration), ignoreTimeScale: true, cancellationToken: cancellationToken);
            MoveToCenter(position, camera, false, Time.unscaledTime);
        }
        private void AdvanceSnap(float now)
        {
            rectTransform.localPosition = Vector3.Lerp(_moveStart, _moveTarget, Mathf.Clamp01((now - _moveStartedAt) / SnapDuration));
        }
        // 提交进入异步资源加载后，仍由 UI 生命周期推进已确定的纯视觉动画。
        protected override void OnUpdate() { if (_snapping) AdvanceSnap(Time.unscaledTime); }
        private void ResizeToBoard(Vector2 position, Camera camera, float screenCellHeight)
        {
            var sprite = _icon.sprite;
            if (sprite == null || screenCellHeight <= 0f) return;
            // 与棋盘 SpriteRenderer 使用同一精灵尺寸；逐帧更新以兼容异步加载和界面缩放。
            var screenSize = sprite.rect.size / sprite.pixelsPerUnit *
                (screenCellHeight / MapGridMath.REFERENCE_CELL_SIZE * BoardSizeRatio);
            var icon = _icon.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(icon, position, camera, out var min) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(icon, position + screenSize, camera, out var max)) return;
            var size = max - min;
            icon.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Abs(size.x));
            icon.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(size.y));
        }
        protected override void OnSetVisible(bool visible) { if (!visible) _snapping = false; }
        protected override void OnDestroy() { _snapping = false; _visual?.Dispose(); }
    }
}
