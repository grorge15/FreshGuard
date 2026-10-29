using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed class StorageRobotSlotWidget : UIWidget
    {
        private RobotIconPresentation _visual;
        private Image _background;
        private Text _label;
        private ShopPointerInput _input;
        private BattleShopUI _owner;
        private Color _baseBackgroundColor;
        protected override void ScriptGenerator()
        {
            _visual = new RobotIconPresentation(FindChildComponent<Image>("m_img_RobotIcon"));
            _background = gameObject.GetComponent<Image>();
            _baseBackgroundColor = _background.color;
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
            var id = owner.Context.GetState(BattleSide.Player).StorageInstanceId;
            Entity entity;
            var robot = id.HasValue && owner.Context.Registry.TryGet(id.Value, out entity) ? entity as RobotEntity : null;
            _visual.Render(robot?.Configuration.RobotIcon, robot != null ? robot.Level : 1,
                robot != null && owner.Drag.DraggedInstanceId != robot.InstanceId);
            _label.text = robot != null ? "暂存" : "暂存位";
            SetHighlighted(false);
        }
        public void SetHighlighted(bool highlighted)
        {
            if (_background == null) return;
            _background.color = highlighted ? Color.cyan : _baseBackgroundColor;
        }
        protected override void OnDestroy()
        {
            _visual?.Dispose();
            _owner = null;
            if (_input != null) { _input.Pressed = null; _input.Moved = null; _input.Released = null; }
        }
    }
}
