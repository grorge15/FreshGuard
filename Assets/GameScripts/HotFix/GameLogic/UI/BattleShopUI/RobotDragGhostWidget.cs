using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class RobotDragGhostWidget : UIWidget
    {
        private Image _icon;
        private Image _background;
        private Text _level;
        protected override void ScriptGenerator()
        {
            _icon = FindChildComponent<Image>("m_img_RobotIcon");
            _background = FindChildComponent<Image>("m_img_LevelBackground");
            _level = FindChildComponent<Text>("m_text_Level");
        }
        public void Show(string icon, int level, Vector2 position, Camera camera)
        {
            Visible = true;
            _icon.color = RobotLevelStyle.IconColor;
            _background.color = RobotLevelStyle.ColorForLevel(level);
            _level.text = "Lv" + level;
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
