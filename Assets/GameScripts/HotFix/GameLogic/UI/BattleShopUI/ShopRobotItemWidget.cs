using Cysharp.Threading.Tasks;
using GameConfig.robot;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class ShopRobotItemWidget : UIWidget
    {
        private Image _icon;
        private Image _quality;
        private Image _background;
        private Image _priceCoin;
        private Text _price;
        private ShopPointerInput _input;
        private BattleShopUI _owner;
        private int _slot;
        private string _iconLocation;
        private Color _baseBackgroundColor;

        protected override void ScriptGenerator()
        {
            _icon = FindChildComponent<Image>("m_img_RobotIcon");
            _quality = FindChildComponent<Image>("m_img_Quality");
            _price = FindChildComponent<Text>("m_text_Price");
            _input = gameObject.GetComponent<ShopPointerInput>();
            _background = gameObject.GetComponent<Image>();
            _priceCoin = FindChildComponent<Image>("m_img_PriceCoin");
        }
        protected override void OnCreate()
        {
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
                _price.color = new Color(0.16f, 0.43f, 0.28f);
                _price.fontSize = 24;
            }
            else if (content.Offer != null)
            {
                config = ConfigSystem.Instance.Tables.TbRobot.Get(content.Offer.RobotId);
                level = content.Offer.Level;
                hidden = owner.Drag.DraggedOfferId == content.Offer.OfferId;
                _price.text = content.Offer.Price.ToString();
                _price.color = balance >= content.Offer.Price ? new Color(0.23f, 0.16f, 0.12f) : new Color(0.8f, 0.12f, 0.1f);
                _price.fontSize = 48;
            }
            // Slot 的位置由 BattleShopUI Prefab 保存；运行时只更新内容和可见状态。
            Visible = true;
            if (_background != null)
            {
                _background.enabled = config != null;
                _baseBackgroundColor = Color.clear;
                _background.color = _baseBackgroundColor;
            }
            _icon.enabled = config != null && !hidden;
            _quality.enabled = false;
            _price.enabled = config != null;
            _priceCoin.enabled = config != null && !content.IsPurchased;
            if (config == null) return;
            _icon.color = RobotLevelStyle.ColorForLevel(level);
            _icon.enabled = !hidden;
            if (_iconLocation != config.RobotIcon)
            {
                _iconLocation = config.RobotIcon;
                _icon.SetSprite(config.RobotIcon, cancellationToken: gameObject.GetCancellationTokenOnDestroy());
            }
        }
        public void SetMergeCandidate(bool eligible, bool hovered)
        {
            if (_background == null) return;
            _background.color = hovered ? new Color(0f, 1f, 1f, 0.4f) : eligible
                ? new Color(0f, 1f, 1f, 0.18f) : _baseBackgroundColor;
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
            _owner = null;
            if (_input != null) { _input.Pressed = null; _input.Moved = null; _input.Released = null; }
        }
    }
}
