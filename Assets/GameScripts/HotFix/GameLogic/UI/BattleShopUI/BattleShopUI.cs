using System.Collections.Generic;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed partial class BattleShopUI : UIWidget
    {
        private Text _coins;
        private Text _refreshPrice;
        private Text _message;
        private Button _refreshButton;
        private RectTransform _offers;
        private readonly List<ShopRobotItemWidget> _items = new List<ShopRobotItemWidget>();
        private StorageRobotSlotWidget _storage;
        private RobotDragGhostWidget _ghost;
        private BerserkBarWidget _berserkBar;
        private BattleShopSceneController _host;
        private float _flashUntil;
        private float _messageUntil;
        private RobotDragSource? _offerMergeSource;
        private int _hoveredOfferSlot = -1;
        public BattleShopContext Context { get; private set; }
        public RobotDragController Drag { get; private set; }
        private Camera EventCamera
        {
            get
            {
                var canvas = OwnerWindow.Canvas.rootCanvas;
                return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null :
                    (canvas.worldCamera != null ? canvas.worldCamera : GameModule.UI.UICamera);
            }
        }

        public void Bind(BattleShopContext context, BattleShopSceneController host)
        {
            _userDatas = new object[] { context, host };
            OnRefresh();
        }

        protected override void RegisterEvent()
        {
            AddUIEvent<int>(BattleShopEvents.Changed, side => { if (side == (int)BattleSide.Player) RefreshView(); });
        }
        protected override void OnCreate()
        {
            _coins = m_text_EnergyCoins;
            _refreshPrice = m_text_Price;
            _message = m_text_Message;
            _offers = m_tf_Offers as RectTransform;
            _refreshButton = m_btn_Refresh;
            Drag = gameObject.GetComponent<RobotDragController>();
            m_item_Template.SetActive(false);
            _storage = CreateWidget<StorageRobotSlotWidget>("m_rect_ShopPanel/m_item_Storage");
            _ghost = CreateWidget<RobotDragGhostWidget>("m_item_DragGhost", visible: false);
            _berserkBar = CreateWidget<BerserkBarWidget>("m_rect_ShopPanel/m_item_BerserkBar", visible: false);
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
            RefreshBerserkBar();
        }
        public void RefreshBerserkBar()
        {
            var bootstrap = _host != null && Context != null && !Context.IsEnded && !Context.IsEndRequested
                ? _host.Bootstrap : null;
            var remaining = bootstrap != null && bootstrap.IsBerserk(BattleSide.Player)
                ? bootstrap.BerserkRemaining(BattleSide.Player) : 0f;
            _berserkBar?.Render(remaining, bootstrap != null ? bootstrap.BerserkDuration : 0f);
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
            while (_items.Count < state.Slots.Count && _items.Count < ShopRules.ShopCapacity)
                _items.Add(CreateWidgetByPrefab<ShopRobotItemWidget>(m_item_Template, _offers));
            for (var i = 0; i < _items.Count; i++)
            {
                if (i < state.Slots.Count) _items[i].Render(this, state.Slots[i].SlotId, state.Slots[i], state.EnergyCoins);
                else _items[i].Visible = false;
            }
            _refreshButton.interactable = !Context.IsBusy && Drag.Phase == RobotDragPhase.Idle;
            _storage.Render(this);
            ApplyOfferMergeHighlights();
        }
        public void PressSlot(int slotId, int pointerId, Vector2 position, float now)
        {
            if (_host == null || !_host.CanInteractWithRobots || Context == null || Context.IsEnded || Context.IsBusy ||
                Drag.Phase != RobotDragPhase.Idle) return;
            var slot = Context.GetState(BattleSide.Player).FindSlot(slotId);
            if (slot == null) return;
            if (slot.Offer != null)
            {
                Drag.Press(RobotDragSource.Offer(slotId, slot.Offer.OfferId), pointerId, position, now, new BoardCoordinate(0, 0));
            }
            else if (slot.InstanceId.HasValue) Drag.Press(slot.InstanceId.Value, pointerId, position, now);
            RefreshView();
        }
        public void PressStorage(int pointerId, Vector2 position, float now)
        {
            if (_host == null || !_host.CanInteractWithRobots || Context == null || Context.IsEnded) return;
            var id = Context.GetState(BattleSide.Player).StorageInstanceId;
            if (id.HasValue) Drag.Press(id.Value, pointerId, position, now);
        }
        private partial void OnClick_RefreshBtn()
        {
            if (_host == null || !_host.CanInteractWithRobots || Context == null || Context.IsBusy ||
                Drag.Phase != RobotDragPhase.Idle) return;
            var result = Context.TryRefresh(BattleSide.Player);
            if (result != ShopOperationResult.Success) ShowFailure(result);
            
        }
        public bool IsOverShop(Vector2 position) => RectTransformUtility.RectangleContainsScreenPoint(m_rect_ShopPanel, position, EventCamera);

        public bool IsOverStorage(Vector2 position)
        {
            return _storage != null && RectTransformUtility.RectangleContainsScreenPoint(_storage.rectTransform, position, EventCamera);
        }
        public bool TryGetShopSlot(Vector2 position, out int slotId)
        {
            slotId = -1;
            if (_items == null) return false;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item != null && item.gameObject != null && item.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(item.rectTransform, position, EventCamera))
                { slotId = item.SlotId; return true; }
            }
            return false;
        }
        public void UpdateOfferMergeCandidates(RobotDragSource source, Vector2 position)
        {
            _offerMergeSource = source;
            int slot;
            _hoveredOfferSlot = TryGetShopSlot(position, out slot) ? slot : -1;
            ApplyOfferMergeHighlights();
        }
        public void ClearOfferMergeCandidates()
        {
            _offerMergeSource = null;
            _hoveredOfferSlot = -1;
            ApplyOfferMergeHighlights();
        }
        private void ApplyOfferMergeHighlights()
        {
            if (_items == null) return;
            var state = Context != null && !Context.IsEnded ? Context.GetState(BattleSide.Player) : null;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item == null || item.gameObject == null) continue;
                var slot = state?.FindSlot(item.SlotId);
                var eligible = item.Visible && _offerMergeSource.HasValue && slot != null &&
                    Context.CheckShopDrop(BattleSide.Player, _offerMergeSource.Value, slot.SlotId) == ShopOperationResult.Success;
                item.SetMergeCandidate(eligible, eligible && item.SlotId == _hoveredOfferSlot);
            }
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
                case ShopOperationResult.ShopFull: _message.text = "商店已满，无法放入"; break;
                case ShopOperationResult.StorageFull: _message.text = "暂存位已满，已返回原位置"; break;
                default: _message.text = "无法放置，已返回来源位置"; break;
            }
            _messageUntil = Time.unscaledTime + 1.8f;
        }
        protected override void OnUpdate()
        {
            RefreshBerserkBar();
            Drag.Advance(Time.unscaledTime);
            var flashing = Time.unscaledTime < _flashUntil && Mathf.Repeat(Time.unscaledTime * 10f, 1f) < 0.5f;
            _coins.color = flashing ? new Color(1f, 0.25f, 0.15f) : Color.white;
            if (Time.unscaledTime > _messageUntil) _message.text = "刷新会清除店内所有机器人，已购机器人不退款。";
        }
        protected override void OnSetVisible(bool visible) { if (!visible) Drag?.Cancel(); }
        protected override void OnDestroy()
        {
            if (_refreshButton != null) _refreshButton.onClick.RemoveListener(OnClick_RefreshBtn);
            Drag?.Cancel();
            Context = null;
            _host = null;
            _userDatas = null;
        }
    }
}
