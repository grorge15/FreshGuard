using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class RobotDragGhostWidget : UIWidget
    {
        private RobotIconPresentation _visual;
        protected override void ScriptGenerator()
        {
            _visual = new RobotIconPresentation(FindChildComponent<Image>("m_img_RobotIcon"));
        }
        public void Show(string icon, int level, Vector2 position, Camera camera)
        {
            Visible = true;
            _visual.Render(icon, level, true);
            Move(position, camera);
            transform.SetAsLastSibling();
        }
        public void Move(Vector2 position, Camera camera)
        {
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, position, camera, out local))
                // local 是相对父节点 pivot 的坐标，不能当作相对自身锚点的 anchoredPosition。
                rectTransform.localPosition = new Vector3(local.x, local.y, 0);
        }
        protected override void OnDestroy() { _visual?.Dispose(); }
    }
}
