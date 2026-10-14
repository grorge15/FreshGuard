using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class BuffSelectItemWidget : UIWidget
    {
        private Image _background;
        private Image _icon;
        private Text _placeholder;
        private Text _level;
        private Text _name;
        private Text _description;
        private Button _button;
        private BuffSelectItemStyle _style;
        private Action<int> _onSelected;
        private int _index;

        protected override void ScriptGenerator()
        {
            _background = gameObject.GetComponent<Image>();
            _button = gameObject.GetComponent<Button>();
            _style = gameObject.GetComponent<BuffSelectItemStyle>();
            _icon = FindChildComponent<Image>("m_img_Icon");
            _placeholder = FindChildComponent<Text>("m_text_IconPlaceholder");
            _level = FindChildComponent<Text>("m_text_Level");
            _name = FindChildComponent<Text>("m_text_Name");
            _description = FindChildComponent<Text>("m_text_Description");
            _button.onClick.AddListener(OnClicked);
        }

        public void Render(BuffSelectOption option, int index, Action<int> onSelected)
        {
            _index = index;
            _onSelected = onSelected;
            _button.interactable = true;
            _background.sprite = _style.BackgroundFor(option.Quality);
            _level.text = "等级" + option.Level;
            _name.text = option.Name;
            _description.text = option.Description;
            _icon.sprite = option.Icon;
            _icon.enabled = option.Icon != null;
            _placeholder.enabled = option.Icon == null;
        }

        private void OnClicked() { _onSelected?.Invoke(_index); }
        public void StopSelection()
        {
            _onSelected = null;
            if (_button != null) _button.interactable = false;
        }
        protected override void OnDestroy()
        {
            StopSelection();
            if (_icon != null) _icon.sprite = null;
            if (_button != null) _button.onClick.RemoveListener(OnClicked);
        }
    }
}
