using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace GameLogic
{
    /// <summary>商店视图与演示会话绑定；所属主菜单负责创建和销毁。</summary>
    public sealed class MysteriousStoreWidget : UIWidget
    {
        private sealed class IconView
        {
            private readonly GameObject[] _qualities = new GameObject[4];
            private string _location;
            private int _quality = -1;
            public IconView(Transform root)
            {
                for (var i = 0; i < _qualities.Length; i++)
                    _qualities[i] = root.Find("m_img_" + (MysteriousStoreQuality)i).gameObject;
            }
            public void Render(MysteriousStoreQuality quality, string location = null)
            {
                for (var i = 0; i < _qualities.Length; i++) _qualities[i].SetActive(i == (int)quality);
                if (!string.IsNullOrEmpty(location) && (_location != location || _quality != (int)quality))
                {
                    _location = location; _quality = (int)quality;
                    var image = _qualities[(int)quality].GetComponent<Image>();
                    image.SetSprite(location, cancellationToken: image.gameObject.GetCancellationTokenOnDestroy());
                }
            }
            public void Clear()
            {
                foreach (var quality in _qualities) quality.SetActive(false);
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
        private Action _purchased;
        private Text _balance, _feedback, _removeName, _removeLevel;
        private GameObject _confirmation;
        private IconView _removeIcon;
        private Sprite _buySprite, _soldOutSprite;
        private int _removeBuffId;
        private bool _disposed;
        private bool _refreshingExpiry;
        private float _nextExpiryCheck;
        private RectTransform _rootCanvasRect;
        private readonly Vector3[] _canvasCorners = new Vector3[4];

        public MysteriousStorePreviewSession Session => _session;

        protected override void OnCreate()
        {
            var canvas = gameObject.GetComponentInParent<Canvas>(true);
            if (canvas != null) _rootCanvasRect = canvas.rootCanvas.transform as RectTransform;
            FitToRootCanvas();
        }

        protected override void OnUpdate()
        {
            FitToRootCanvas();
            if (_session != null && !_disposed) RefreshCoins();
            if (_session is MysteriousStoreSession && !_refreshingExpiry && !_session.IsBusy &&
                Time.unscaledTime >= _nextExpiryCheck)
            {
                _nextExpiryCheck = Time.unscaledTime + 1f;
                RefreshExpiryAsync().Forget();
            }
        }

        private async UniTask RefreshExpiryAsync()
        {
            _refreshingExpiry = true;
            try
            {
                var session = _session;
                await session.RefreshExpiredAsync();
                if (!_disposed && _session == session) RefreshView();
            }
            finally { _refreshingExpiry = false; }
        }

        private void RefreshCoins()
        {
            var text = "助力币  " + _session.PlayerState.Coins;
            if (_balance.text != text) _balance.text = text;
        }

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
            Listen(content.Find("m_btn_Close").GetComponent<Button>(), () => { if (_session?.IsBusy != true) _close?.Invoke(); });
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
                Listen(button, () => BuyAsync(index).Forget());
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
            Listen(panel.Find("m_btn_Remove").GetComponent<Button>(), () => ConfirmRemovalAsync().Forget());
        }

        public void Bind(MysteriousStorePreviewSession session, Action close, Action purchased = null)
        {
            if (_disposed) return;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            GameSoundService.BindButtons(gameObject);
            GameSoundService.Play(GameSound.UiOpen);
            _close = close;
            _purchased = purchased;
            _feedback.text = string.Empty;
            var resetHint = transform.Find("m_rect_Content/ResetHint")?.GetComponent<Text>();
            if (resetHint != null) resetHint.text = "助力于北京时间次日零点到期，已开战对局保留本局效果。\n助力币保留。";
            CancelRemoval();
            RefreshView();
        }

        public void RefreshView()
        {
            if (_disposed || _session == null) return;
            FitToRootCanvas();
            RefreshCoins();
            for (var i = 0; i < _offers.Length; i++)
            {
                var view = _offers[i];
                view.Root.SetActive(i < _session.Offers.Count);
                if (i >= _session.Offers.Count) continue;
                var data = _session.Offers[i];
                var heldLevel = _session.GetHeldLevel(data.BuffId);
                view.Icon.Render(data.Quality, data.IconLocation);
                view.Name.text = data.Name;
                var displayLevel = _session.HasPurchased && heldLevel > 0 ? heldLevel : _session.GetTargetLevel(i);
                view.Description.text = data.DescriptionAt(displayLevel);
                view.Level.gameObject.SetActive(data.HasLevel && !data.IsInstantReward);
                view.Level.text = displayLevel + "级";
                view.Upgrade.SetActive(data.HasLevel && heldLevel > 0 && heldLevel < data.MaxLevel && !_session.HasPurchased);
                view.Price.text = _session.GetPrice(i).ToString();
                view.Price.gameObject.SetActive(!_session.HasPurchased);
                view.Coin.SetActive(!_session.HasPurchased);
                view.SoldOut.gameObject.SetActive(_session.HasPurchased);
                view.Background.sprite = _session.HasPurchased ? _soldOutSprite : _buySprite;
                view.Buy.interactable = !_session.IsBusy && !_session.HasPurchased && _session.GetTargetLevel(i) <= data.MaxLevel;
            }
            for (var slot = 0; slot < _held.Length; slot++)
            {
                var item = FindSlot(slot);
                var view = _held[slot];
                view.Content.SetActive(item != null);
                if (item == null)
                {
                    view.Icon.Clear();
                    view.Level.text = string.Empty;
                    continue;
                }
                view.Icon.Render(item.Buff.Quality, item.Buff.IconLocation);
                view.Level.text = item.Level + "级";
                view.Remove.interactable = !_session.IsBusy;
            }
        }

        private async UniTask BuyAsync(int index)
        {
            if (_disposed || _session == null || _session.IsBusy || _confirmation.activeSelf || index < 0 || index >= _session.Offers.Count) return;
            var session = _session;
            var offer = session.Offers[index];
            var wasHeld = session.GetHeldLevel(offer.BuffId) > 0;
            var purchase = session.TryBuyAsync(index);
            RefreshView();
            var result = await purchase;
            if (_disposed || _session != session) return;
            switch (result)
            {
                case MysteriousStorePurchaseResult.Success:
                    GameSoundService.Play(wasHeld ? GameSound.BuffStack : GameSound.BuffAcquired);
                    _purchased?.Invoke();
                    _feedback.text = offer.IsInstantReward ? "已补充体力" :
                        (wasHeld ? "升级成功：" : "获得助力：") + offer.Name + " " + session.GetHeldLevel(offer.BuffId) + "级";
                    break;
                case MysteriousStorePurchaseResult.InsufficientBalance:
                    GameSoundService.Play(GameSound.Error);
                    _feedback.text = "助力币不足";
                    break;
                case MysteriousStorePurchaseResult.SlotsFull:
                    GameSoundService.Play(GameSound.Error);
                    _feedback.text = "当前可装备助力道具已达上限，\n请在下方移除道具后再次购买。";
                    break;
                case MysteriousStorePurchaseResult.SaveFailed:
                    GameSoundService.Play(GameSound.Error);
                    _feedback.text = "保存失败，请重试";
                    break;
                case MysteriousStorePurchaseResult.Unavailable:
                    _feedback.text = "该商品当前不可购买";
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
            if (_disposed || _session?.IsBusy == true) return;
            var item = FindSlot(slot);
            if (item == null) return;
            _removeBuffId = item.BuffId;
            _removeName.text = "移除" + item.Buff.Name;
            _removeIcon.Render(item.Buff.Quality, item.Buff.IconLocation);
            _removeLevel.text = item.Level + "级";
            _confirmation.SetActive(true);
            GameSoundService.Play(GameSound.UiOpen);
        }

        private void CancelRemoval()
        {
            if (_session?.IsBusy == true) return;
            if (_confirmation.activeSelf) GameSoundService.Play(GameSound.UiClose);
            _removeBuffId = 0;
            _confirmation.SetActive(false);
        }

        private async UniTask ConfirmRemovalAsync()
        {
            if (_disposed || _session == null || _session.IsBusy || _removeBuffId == 0) return;
            var session = _session;
            var removal = session.RemoveAsync(_removeBuffId);
            RefreshView();
            var removed = await removal;
            if (_disposed || _session != session) return;
            if (removed)
            {
                _feedback.text = "已移除助力，全部层数已清除";
                GameSoundService.Play(GameSound.BuffRemove);
                _purchased?.Invoke();
            }
            else if (!string.IsNullOrEmpty(session.LastError)) _feedback.text = "保存失败，请重试";
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
            _purchased = null;
            _rootCanvasRect = null;
        }
    }
}
