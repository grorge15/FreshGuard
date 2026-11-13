using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class RobotDragGhostWidget : UIWidget
    {
        private const float BoardSizeRatio = 0.8f;
        private Image _icon;
        private RobotIconPresentation _visual;
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
            ResizeToBoard(position, camera, screenCellHeight);
            var parent = (RectTransform)transform.parent;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, position, camera, out local))
            {
                // local 是相对父节点 pivot 的坐标，不能当作相对自身锚点的 anchoredPosition。
                rectTransform.localPosition = new Vector3(local.x, local.y, 0);
                var icon = _icon.rectTransform;
                // 将图标底边中心对准触点，偏移随界面缩放和图标大小变化。
                var bottom = parent.InverseTransformPoint(icon.TransformPoint(new Vector3(icon.rect.center.x, icon.rect.yMin, 0)));
                rectTransform.localPosition += new Vector3(local.x - bottom.x, local.y - bottom.y, 0);
            }
            var visual = _icon.rectTransform;
            return RectTransformUtility.WorldToScreenPoint(camera, visual.TransformPoint(visual.rect.center));
        }
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
        protected override void OnDestroy() { _visual?.Dispose(); }
    }
}
