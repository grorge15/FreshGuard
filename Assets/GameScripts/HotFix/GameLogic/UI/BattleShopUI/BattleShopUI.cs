using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.UI, "BattleShopUI", fullScreen: false)]
    public sealed class BattleShopUI : UIWindow
    {
        private Text _coins;
        private Text _refreshPrice;
        private Text _message;
        private Button _refreshButton;
        private RectTransform _offers;
        private ShopRobotItemWidget[] _items;
        private StorageRobotSlotWidget _storage;
        private RobotDragGhostWidget _ghost;
        private BattleShopSceneController _host;
        private float _flashUntil;
        private float _messageUntil;
        private Font _font;
        public BattleShopContext Context { get; private set; }
        public RobotDragController Drag { get; private set; }
        private Camera EventCamera => Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null :
            (Canvas.worldCamera != null ? Canvas.worldCamera : GameModule.UI.UICamera);

        protected override void ScriptGenerator()
        {
            _coins = FindChildComponent<Text>("m_rect_ShopPanel/m_text_EnergyCoins");
            _refreshPrice = FindChildComponent<Text>("m_rect_ShopPanel/m_btn_Refresh/m_text_Price");
            _message = FindChildComponent<Text>("m_rect_ShopPanel/m_text_Message");
            _offers = FindChildComponent<RectTransform>("m_rect_ShopPanel/m_tf_Offers");
            _refreshButton = FindChildComponent<Button>("m_rect_ShopPanel/m_btn_Refresh");
            _refreshButton.onClick.AddListener(OnRefreshClicked);
            Drag = gameObject.GetComponent<RobotDragController>();
        }
        protected override void RegisterEvent()
        {
            AddUIEvent<int>(BattleShopEvents.Changed, side => { if (side == (int)BattleSide.Player) RefreshView(); });
        }
        protected override void OnCreate()
        {
            _items = new ShopRobotItemWidget[3];
            for (var i = 0; i < _items.Length; i++)
                _items[i] = CreateWidget<ShopRobotItemWidget>("m_rect_ShopPanel/m_tf_Offers/Slot_" + i);
            _storage = CreateWidget<StorageRobotSlotWidget>("m_rect_ShopPanel/m_item_Storage");
            _ghost = CreateWidget<RobotDragGhostWidget>("m_item_DragGhost", visible: false);
            // The current target is Windows; retain Arial as a fallback for other hosts.
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
            foreach (var text in gameObject.GetComponentsInChildren<Text>(true)) text.font = _font;
        }
        protected override void OnRefresh()
        {
            var context = UserData as BattleShopContext;
            var host = UserDatas != null && UserDatas.Length > 1 ? UserDatas[1] as BattleShopSceneController : null;
            if (context == null || host == null) return;
            if (Context != context || _host != host)
            {
                Drag.Cancel();
                Context = context;
                _host = host;
                Drag.Initialize(this, host);
            }
            RefreshView();
        }
        public void RefreshView()
        {
            // 场景卸载时Unity对象可能先于窗口包装销毁，终局通知不再访问展示组件。
            if (Context == null || Context.IsEnded || gameObject == null || _items == null || _storage == null ||
                _storage.gameObject == null || _coins == null || _refreshPrice == null || _offers == null) return;
            foreach (var item in _items) if (item == null || item.gameObject == null) return;
            var state = Context.GetState(BattleSide.Player);
            _coins.text = state.EnergyCoins.ToString();
            _refreshPrice.text = _host != null ? _host.RefreshPrice.ToString() : "";
            _refreshPrice.color = _host != null && state.EnergyCoins < _host.RefreshPrice
                ? new Color(0.8f, 0.12f, 0.1f) : new Color(0.23f, 0.13f, 0.08f);
            for (var i = 0; i < _items.Length; i++) _items[i].Render(this, i, state.Slots[i], state.EnergyCoins);
            _storage.Render(this);
            LayoutRebuilder.MarkLayoutForRebuild(_offers);
        }
        public void PressSlot(int slotId, int pointerId, Vector2 position, float now)
        {
            if (Context == null || Context.IsEnded || Context.IsBusy || Drag.Phase != RobotDragPhase.Idle) return;
            var slot = Context.GetState(BattleSide.Player).Slots[slotId];
            if (slot.Offer != null)
            {
                if (Context.GetState(BattleSide.Player).EnergyCoins < slot.Offer.Price)
                { ShowFailure(ShopOperationResult.InsufficientCoins); return; }
                Drag.Press(RobotDragSource.Offer(slotId, slot.Offer.OfferId), pointerId, position, now, new BoardCoordinate(0, 0));
            }
            else if (slot.InstanceId.HasValue) Drag.Press(slot.InstanceId.Value, pointerId, position, now);
            RefreshView();
        }
        public void PressStorage(int pointerId, Vector2 position, float now)
        {
            if (Context == null || Context.IsEnded) return;
            var id = Context.GetState(BattleSide.Player).StorageInstanceId;
            if (id.HasValue) Drag.Press(id.Value, pointerId, position, now);
        }
        private void OnRefreshClicked()
        {
            if (Context == null || Context.IsBusy || Drag.Phase != RobotDragPhase.Idle) return;
            var result = Context.TryRefresh(BattleSide.Player);
            if (result != ShopOperationResult.Success) ShowFailure(result);
            else { _offers.localScale = Vector3.one * 0.94f; }
        }
        public bool IsOverStorage(Vector2 position)
        {
            return _storage != null && RectTransformUtility.RectangleContainsScreenPoint(_storage.rectTransform, position, EventCamera);
        }
        public void SetStorageHighlighted(bool highlighted) { _storage?.SetHighlighted(highlighted); }
        public void ShowGhost(string icon, int level, Vector2 position) { _ghost.Show(icon, level, position, EventCamera); }
        public void MoveGhost(Vector2 position) { _ghost?.Move(position, EventCamera); }
        public void HideGhost() { if (_ghost != null && _ghost.gameObject != null) _ghost.Visible = false; }
        public void ShowFailure(ShopOperationResult result)
        {
            if (_message == null) return;
            switch (result)
            {
                case ShopOperationResult.InsufficientCoins:
                    _message.text = "能源币不足";
                    _flashUntil = Time.unscaledTime + 0.5f;
                    break;
                case ShopOperationResult.ResourceFailed: _message.text = "资源加载失败，请再次拖拽"; break;
                case ShopOperationResult.BattleEnded: _message.text = "对局已结束"; break;
                case ShopOperationResult.Busy: return;
                case ShopOperationResult.IncompatibleMerge: _message.text = "需要相同机器人、相同等级"; break;
                case ShopOperationResult.MaxLevelReached: _message.text = "Lv5 无法继续合成"; break;
                case ShopOperationResult.EntityNotFound: _message.text = "目标已失效，已返回来源位置"; break;
                case ShopOperationResult.StorageFull: _message.text = "暂存位已满，已返回原位置"; break;
                default: _message.text = "无法放置，已返回来源位置"; break;
            }
            _messageUntil = Time.unscaledTime + 1.8f;
        }
        protected override void OnUpdate()
        {
            Drag.Advance(Time.unscaledTime);
            var flashing = Time.unscaledTime < _flashUntil && Mathf.Repeat(Time.unscaledTime * 10f, 1f) < 0.5f;
            _coins.color = flashing ? new Color(1f, 0.25f, 0.15f) : Color.white;
            if (Time.unscaledTime > _messageUntil) _message.text = "";
            _offers.localScale = Vector3.MoveTowards(_offers.localScale, Vector3.one, Time.unscaledDeltaTime * 0.8f);
        }
        protected override void OnSetVisible(bool visible) { if (!visible) Drag?.Cancel(); }
        protected override void OnDestroy()
        {
            if (_refreshButton != null) _refreshButton.onClick.RemoveListener(OnRefreshClicked);
            Drag?.Cancel();
            Context = null;
            _host = null;
            if (_font != null) Object.Destroy(_font);
        }
    }
}
