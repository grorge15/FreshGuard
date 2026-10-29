using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>商店视图与演示会话绑定；所属主菜单负责创建和销毁。</summary>
    public sealed class MysteriousStoreWidget : UIWidget
    {
        private sealed class IconView
        {
            private readonly GameObject[] _qualities = new GameObject[4];
            public IconView(Transform root)
            {
                for (var i = 0; i < _qualities.Length; i++)
                    _qualities[i] = root.Find("m_img_" + (MysteriousStoreQuality)i).gameObject;
            }
            public void Render(MysteriousStoreQuality quality)
            {
                for (var i = 0; i < _qualities.Length; i++) _qualities[i].SetActive(i == (int)quality);
            }
        }

        private sealed class OfferView
        {
            public GameObject Root;
            public IconView Icon;
            public Text Name, Description, Level, Price, SoldOut;
            public GameObject Upgrade, Coin;
            public Button Buy;
            public Image Background;
        }

        private sealed class HeldView
        {
            public GameObject Content;
            public IconView Icon;
            public Text Level;
            public Button Remove;
        }

        private readonly List<(Button button, UnityAction action)> _listeners = new List<(Button, UnityAction)>();
        private readonly OfferView[] _offers = new OfferView[3];
        private readonly HeldView[] _held = new HeldView[MysteriousStorePreviewSession.SlotCount];
        private MysteriousStorePreviewSession _session;
        private Action _close;
        private Text _balance, _feedback, _removeName, _removeLevel;
        private GameObject _confirmation;
        private IconView _removeIcon;
        private Sprite _buySprite, _soldOutSprite;
        private int _removeBuffId;
        private bool _disposed;
        private RectTransform _rootCanvasRect;
        private readonly Vector3[] _canvasCorners = new Vector3[4];

        public MysteriousStorePreviewSession Session => _session;

        protected override void OnCreate()
        {
            var canvas = gameObject.GetComponentInParent<Canvas>(true);
            if (canvas != null) _rootCanvasRect = canvas.rootCanvas.transform as RectTransform;
            FitToRootCanvas();
        }

        protected override void OnUpdate() { FitToRootCanvas(); }

        private void FitToRootCanvas()
        {
            if (_disposed || _rootCanvasRect == null || transform.parent == null) return;
            // UIWindow 初始化会将主菜单根尺寸归零，不能仅靠对子节点的 Stretch。
            // 把根 Canvas 的世界边界换算到宿主局部坐标，保持 Widget 归属不变。
            _rootCanvasRect.GetWorldCorners(_canvasCorners);
            var minimum = transform.parent.InverseTransformPoint(_canvasCorners[0]);
            var maximum = transform.parent.InverseTransformPoint(_canvasCorners[2]);
            var size = (Vector2)(maximum - minimum);
            var center = (minimum + maximum) * 0.5f;
            var centerAnchor = new Vector2(0.5f, 0.5f);
            if (rectTransform.anchorMin != centerAnchor) rectTransform.anchorMin = centerAnchor;
            if (rectTransform.anchorMax != centerAnchor) rectTransform.anchorMax = centerAnchor;
            if (rectTransform.sizeDelta != size) rectTransform.sizeDelta = size;
            if ((rectTransform.localPosition - center).sqrMagnitude > 0.0001f) rectTransform.localPosition = center;
        }

        protected override void ScriptGenerator()
        {
            var content = FindChild("m_rect_Content");
            _balance = content.Find("m_text_Balance").GetComponent<Text>();
            _feedback = content.Find("m_text_Feedback").GetComponent<Text>();
            Listen(content.Find("m_btn_Close").GetComponent<Button>(), () => _close?.Invoke());
            for (var i = 0; i < _offers.Length; i++)
            {
                var index = i;
                var root = content.Find("m_item_Offer" + i);
                var button = root.Find("m_btn_Buy").GetComponent<Button>();
                _offers[i] = new OfferView
                {
                    Root = root.gameObject, Icon = new IconView(root.Find("m_rect_Icon")),
                    Name = root.Find("m_text_Name").GetComponent<Text>(),
                    Description = root.Find("m_text_Description").GetComponent<Text>(),
                    Level = root.Find("m_text_Level").GetComponent<Text>(),
                    Upgrade = root.Find("m_img_Upgrade").gameObject, Buy = button,
                    Background = button.GetComponent<Image>(),
                    Price = button.transform.Find("m_text_Price").GetComponent<Text>(),
                    Coin = button.transform.Find("m_img_Coin").gameObject,
                    SoldOut = button.transform.Find("m_text_SoldOut").GetComponent<Text>()
                };
                Listen(button, () => Buy(index));
            }
            _buySprite = _offers[0].Background.sprite;
            _soldOutSprite = content.Find("m_img_SoldOutTemplate").GetComponent<Image>().sprite;
            for (var i = 0; i < _held.Length; i++)
            {
                var slot = i;
                var root = content.Find("m_item_Held" + i + "/m_go_Content");
                _held[i] = new HeldView
                {
                    Content = root.gameObject, Icon = new IconView(root.Find("m_rect_Icon")),
                    Level = root.Find("m_text_Level").GetComponent<Text>(),
                    Remove = root.Find("m_btn_Remove").GetComponent<Button>()
                };
                Listen(_held[i].Remove, () => OpenRemoval(slot));
            }
            _confirmation = FindChild("m_go_RemoveConfirmation").gameObject;
            var panel = _confirmation.transform.Find("m_rect_Content");
            _removeName = panel.Find("m_text_Name").GetComponent<Text>();
            _removeLevel = panel.Find("m_rect_Icon/m_text_Level").GetComponent<Text>();
            _removeIcon = new IconView(panel.Find("m_rect_Icon"));
            Listen(_confirmation.transform.Find("m_btn_Outside").GetComponent<Button>(), CancelRemoval);
            Listen(panel.Find("m_btn_Close").GetComponent<Button>(), CancelRemoval);
            Listen(panel.Find("m_btn_Remove").GetComponent<Button>(), ConfirmRemoval);
        }

        public void Bind(MysteriousStorePreviewSession session, Action close)
        {
            if (_disposed) return;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _close = close;
            _feedback.text = string.Empty;
            CancelRemoval();
            RefreshView();
        }

        public void RefreshView()
        {
            if (_disposed || _session == null) return;
            FitToRootCanvas();
            _balance.text = "助力币  " + _session.Balance;
            for (var i = 0; i < _offers.Length; i++)
            {
                var view = _offers[i];
                view.Root.SetActive(i < _session.Offers.Count);
                if (i >= _session.Offers.Count) continue;
                var data = _session.Offers[i];
                var heldLevel = _session.GetHeldLevel(data.BuffId);
                view.Icon.Render(data.Quality);
                view.Name.text = data.Name;
                view.Description.text = data.Description;
                view.Level.text = heldLevel > 0 ? $"当前{heldLevel}层 → 购买后{heldLevel + 1}层" : "购买后获得第1层";
                view.Upgrade.SetActive(heldLevel > 0 && !_session.HasPurchased);
                view.Price.text = _session.GetPrice(i).ToString();
                view.Price.gameObject.SetActive(!_session.HasPurchased);
                view.Coin.SetActive(!_session.HasPurchased);
                view.SoldOut.gameObject.SetActive(_session.HasPurchased);
                view.Background.sprite = _session.HasPurchased ? _soldOutSprite : _buySprite;
                view.Buy.interactable = !_session.HasPurchased && _session.GetTargetLevel(i) <= data.MaxLevel;
                if (_session.HasPurchased) view.Level.text = heldLevel > 0 ? $"当前持有{heldLevel}层" : "本店已结束购买";
            }
            for (var slot = 0; slot < _held.Length; slot++)
            {
                var item = FindSlot(slot);
                var view = _held[slot];
                view.Content.SetActive(item != null);
                if (item == null) continue;
                view.Icon.Render(item.Buff.Quality);
                view.Level.text = item.Level + "级";
            }
        }

        private void Buy(int index)
        {
            if (_disposed || _session == null || _confirmation.activeSelf) return;
            var wasHeld = _session.GetHeldLevel(_session.Offers[index].BuffId) > 0;
            switch (_session.TryBuy(index))
            {
                case MysteriousStorePurchaseResult.Success:
                    _feedback.text = (wasHeld ? "升层成功：" : "获得助力：") + _session.Offers[index].Name +
                        " " + _session.GetHeldLevel(_session.Offers[index].BuffId) + "层";
                    break;
                case MysteriousStorePurchaseResult.InsufficientBalance:
                    _feedback.text = "助力币不足";
                    break;
                case MysteriousStorePurchaseResult.SlotsFull:
                    _feedback.text = "当前可装备助力道具已达上限，\n请在下方移除道具后再次购买。";
                    break;
            }
            RefreshView();
        }

        private MysteriousStoreHeldBuffData FindSlot(int slot)
        {
            if (_session != null)
                foreach (var item in _session.HeldBuffs) if (item.Slot == slot) return item;
            return null;
        }

        private void OpenRemoval(int slot)
        {
            if (_disposed) return;
            var item = FindSlot(slot);
            if (item == null) return;
            _removeBuffId = item.BuffId;
            _removeName.text = "移除" + item.Buff.Name;
            _removeIcon.Render(item.Buff.Quality);
            _removeLevel.text = item.Level + "级";
            _confirmation.SetActive(true);
        }

        private void CancelRemoval()
        {
            _removeBuffId = 0;
            _confirmation.SetActive(false);
        }

        private void ConfirmRemoval()
        {
            if (_disposed || _session == null || _removeBuffId == 0) return;
            if (_session.Remove(_removeBuffId)) _feedback.text = "已移除助力，全部层数已清除";
            CancelRemoval();
            RefreshView();
        }

        private void Listen(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _listeners.Add((button, action));
        }

        protected override void OnDestroy()
        {
            _disposed = true;
            foreach (var listener in _listeners)
                if (listener.button != null) listener.button.onClick.RemoveListener(listener.action);
            _listeners.Clear();
            _session = null;
            _close = null;
            _rootCanvasRect = null;
        }
    }
}
