using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class StorageRobotSlotWidget : UIWidget
    {
        private Image _icon;
        private Image _background;
        private Text _label;
        private ShopPointerInput _input;
        private BattleShopUI _owner;
        private string _location;
        private int _level;
        protected override void ScriptGenerator()
        {
            _icon = FindChildComponent<Image>("m_img_RobotIcon");
            _background = gameObject.GetComponent<Image>();
            _label = FindChildComponent<Text>("m_text_Storage");
            _input = gameObject.GetComponent<ShopPointerInput>();
        }
        protected override void OnCreate()
        {
            _input.Pressed = (id, position, now) => _owner?.PressStorage(id, position, now);
            _input.Moved = (id, position) => _owner?.Drag.Move(id, position);
            _input.Released = (id, position) => _owner?.Drag.Release(id, position);
        }
        public void Render(BattleShopUI owner)
        {
            _owner = owner;
            _icon.color = RobotLevelStyle.IconColor;
            var id = owner.Context.GetState(BattleSide.Player).StorageInstanceId;
            Entity entity;
            var robot = id.HasValue && owner.Context.Registry.TryGet(id.Value, out entity) ? entity as RobotEntity : null;
            _icon.enabled = robot != null && owner.Drag.DraggedInstanceId != robot.InstanceId;
            _label.text = robot != null ? "暂存  Lv" + robot.Level : "暂存位";
            _level = robot != null ? robot.Level : 0;
            SetHighlighted(false);
            if (robot != null && _location != robot.Configuration.RobotIcon)
            {
                _location = robot.Configuration.RobotIcon;
                _icon.SetSprite(_location, cancellationToken: gameObject.GetCancellationTokenOnDestroy());
            }
        }
        public void SetHighlighted(bool highlighted)
        {
            if (_background == null) return;
            _background.color = highlighted ? Color.cyan : _level > 0 ? RobotLevelStyle.ColorForLevel(_level) : new Color(0.76f, 0.68f, 0.58f);
        }
        protected override void OnDestroy()
        {
            _owner = null;
            if (_input != null) { _input.Pressed = null; _input.Moved = null; _input.Released = null; }
        }
    }
}
