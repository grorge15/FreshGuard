using GameConfig.robot;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class ShopRobotItemWidget : UIWidget
    {
        private RobotIconPresentation _visual;
        private Image _quality;
        private Image _background;
        private Image _priceCoin;
        private Text _price;
        private Vector2 _pricePosition;
        private int _priceFontSize;
        private ShopPointerInput _input;
        private CanvasGroup _reelGroup;
        private BattleShopUI _owner;
        private int _slot;
        public int SlotId => _slot;
        internal Image IconBackground => _visualBackground;
        private Image _visualBackground;
        private Color _baseBackgroundColor;

        protected override void ScriptGenerator()
        {
            _visualBackground = FindChildComponent<Image>("m_img_RobotIcon");
            _visual = new RobotIconPresentation(_visualBackground);
            _reelGroup = gameObject.GetComponent<CanvasGroup>();
            if (_reelGroup == null) _reelGroup = gameObject.AddComponent<CanvasGroup>();
            _quality = FindChildComponent<Image>("m_img_Quality");
            _price = FindChildComponent<Text>("m_text_Price");
            _input = gameObject.GetComponent<ShopPointerInput>();
            _background = gameObject.GetComponent<Image>();
            _priceCoin = FindChildComponent<Image>("m_img_PriceCoin");
        }
        protected override void OnCreate()
        {
            _priceFontSize = _price.fontSize;
            _pricePosition = _price.rectTransform.anchoredPosition;
            _input.Pressed = (id, position, now) => _owner?.PressSlot(_slot, id, position, now);
            _input.Moved = (id, position) => _owner?.Drag.Move(id, position);
            _input.Released = (id, position) => _owner?.Drag.Release(id, position);
        }
        public void Render(BattleShopUI owner, int slot, ShopSlot content, int balance)
        {
            _owner = owner;
            _slot = slot;
            Robot config = null;
            var level = 1;
            var hidden = false;
            if (content.IsPurchased)
            {
                Entity entity;
                if (owner.Context.Registry.TryGet(content.InstanceId.Value, out entity) && entity is RobotEntity robot)
                {
                    config = robot.Configuration;
                    level = robot.Level;
                    hidden = owner.Drag.DraggedInstanceId == robot.InstanceId;
                }
                _price.text = "已购买";
                _price.color = new Color32(155, 238, 185, 255);
                _price.fontSize = 29;
                _price.rectTransform.anchoredPosition = new Vector2(0, _pricePosition.y);
            }
            else if (content.Offer != null)
            {
                config = ConfigSystem.Instance.Tables.TbRobot.Get(content.Offer.RobotId);
                level = content.Offer.Level;
                hidden = owner.Drag.DraggedOfferId == content.Offer.OfferId;
                _price.text = content.Offer.Price.ToString();
                _price.color = balance >= content.Offer.Price ? new Color32(255, 246, 222, 255) : new Color(1f, 0.3f, 0.2f);
                _price.fontSize = _priceFontSize;
                _price.rectTransform.anchoredPosition = _pricePosition;
            }
            // Slot 的位置由 BattleShopUI Prefab 保存；运行时只更新内容和可见状态。
            Visible = true;
            if (_background != null)
            {
                _background.enabled = config != null;
                _baseBackgroundColor = Color.clear;
                _background.color = _baseBackgroundColor;
            }
            _visual.Render(config?.RobotIcon, config?.RobotPortrait, level, config != null && !hidden,
                !content.IsPurchased && content.Offer != null && balance < content.Offer.Price);
            _quality.enabled = false;
            _price.enabled = config != null;
            _priceCoin.enabled = config != null && !content.IsPurchased;
        }
        public void SetMergeCandidate(bool eligible, bool hovered)
        {
            if (_background == null) return;
            _background.color = hovered ? new Color(0f, 1f, 1f, 0.4f) : eligible
                ? new Color(0f, 1f, 1f, 0.18f) : _baseBackgroundColor;
        }
        internal void SetReelPresentation(bool hidden, bool interactable, float scale = 1f)
        {
            if (_reelGroup == null) return;
            _reelGroup.alpha = hidden ? 0f : 1f;
            _reelGroup.blocksRaycasts = interactable;
            _reelGroup.interactable = interactable;
            if (_visualBackground != null) _visualBackground.rectTransform.localScale = Vector3.one * scale;
        }
        internal static Color QualityColor(int id)
        {
            switch (id)
            {
                case 2: return new Color(0.37f, 0.67f, 0.42f);
                case 3: return new Color(0.28f, 0.57f, 0.86f);
                case 4: return new Color(0.65f, 0.39f, 0.83f);
                case 5: return new Color(0.91f, 0.57f, 0.17f);
                case 6: return new Color(0.88f, 0.25f, 0.25f);
                default: return Color.gray;
            }
        }
        protected override void OnDestroy()
        {
            _visual?.Dispose();
            _owner = null;
            if (_input != null) { _input.Pressed = null; _input.Moved = null; _input.Released = null; }
        }
    }
}
