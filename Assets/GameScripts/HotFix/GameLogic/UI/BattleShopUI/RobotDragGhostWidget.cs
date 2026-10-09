using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class RobotDragGhostWidget : UIWidget
    {
        private Image _icon;
        protected override void ScriptGenerator() { _icon = gameObject.GetComponent<Image>(); }
        public void Show(string icon, Vector2 position, Camera camera)
        {
            Visible = true;
            _icon.SetSprite(icon, cancellationToken: gameObject.GetCancellationTokenOnDestroy());
            Move(position, camera);
            transform.SetAsLastSibling();
        }
        public void Move(Vector2 position, Camera camera)
        {
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, position, camera, out local))
                rectTransform.anchoredPosition = local;
        }
    }
}
