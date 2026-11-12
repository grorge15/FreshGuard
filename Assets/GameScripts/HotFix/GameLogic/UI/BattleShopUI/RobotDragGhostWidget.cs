using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class RobotDragGhostWidget : UIWidget
    {
        private Image _icon;
        private RobotIconPresentation _visual;
        protected override void ScriptGenerator()
        {
            _icon = FindChildComponent<Image>("m_img_RobotIcon");
            _visual = new RobotIconPresentation(_icon);
        }
        public void Show(string icon, string portrait, int level, Vector2 position, Camera camera)
        {
            Visible = true;
            _visual.Render(icon, portrait, level, true);
            Move(position, camera);
            transform.SetAsLastSibling();
        }
        public Vector2 Move(Vector2 position, Camera camera)
        {
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
        protected override void OnDestroy() { _visual?.Dispose(); }
    }
}
