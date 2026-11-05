using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public sealed partial class BattleShopUI
    {
        private const float REEL_DELAY = 0.16f;
        private const float REEL_SECONDS = 1.15f;
        private const float REEL_STAGGER = 0.13f;
        private const float LANDING_SECONDS = 0.21f;
        private readonly RectTransform[] _reelWindows = new RectTransform[ShopRules.OfferBatchCount];
        private readonly RectTransform[] _reelStrips = new RectTransform[ShopRules.OfferBatchCount];
        private readonly Image[][] _reelCells = new Image[ShopRules.OfferBatchCount][];
        private readonly bool[] _reelStopped = new bool[ShopRules.OfferBatchCount];
        private Image[] _reelPalette;
        private RectTransform _leverPivot;
        private Quaternion _leverRotation;
        private BattleShopContext _refreshContext;
        private float _refreshStartedAt;
        public bool IsRefreshing { get; private set; }

        private void InitializeSlotMachine()
        {
            var machine = m_rect_ShopPanel.Find("SlotMachine");
            if (machine == null) return;
            _leverPivot = m_btn_Refresh.transform as RectTransform;
            _leverRotation = _leverPivot.localRotation;
            var palette = machine.Find("ReelPalette");
            _reelPalette = new Image[palette.childCount];
            for (var i = 0; i < palette.childCount; i++)
                _reelPalette[i] = palette.GetChild(i).GetComponent<Image>();
            for (var i = 0; i < _reelWindows.Length; i++)
            {
                var window = machine.Find("Reel_" + i) as RectTransform;
                var strip = window.Find("Strip") as RectTransform;
                _reelWindows[i] = window;
                _reelStrips[i] = strip;
                var cells = new Image[25 + i * 3];
                for (var j = 0; j < cells.Length; j++)
                {
                    var cell = Object.Instantiate(_reelPalette[(j * 3 + i) % _reelPalette.Length], strip, false);
                    cell.name = "Cell_" + j;
                    var rect = cell.rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = window.sizeDelta * 0.91f;
                    rect.anchoredPosition = new Vector2(0, -(j + 0.5f) * window.sizeDelta.y);
                    cells[j] = cell;
                }
                _reelCells[i] = cells;
                window.gameObject.SetActive(false);
            }
        }

        private void LayoutSlotItems()
        {
            if (_reelPalette == null) return;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var rect = item.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(0, 1);
                rect.localScale = Vector3.one;
                rect.anchoredPosition = new Vector2(_offers.rect.width * (0.101f + i * 0.277f), -_offers.rect.height * 0.284f);
                PresentSlotItem(i, Time.unscaledTime - _refreshStartedAt);
            }
        }

        private void BeginSlotRefresh()
        {
            if (_reelPalette == null) return;
            IsRefreshing = true;
            _refreshContext = Context;
            _refreshStartedAt = Time.unscaledTime;
            for (var i = 0; i < _reelWindows.Length; i++)
            {
                _reelStopped[i] = false;
                _reelStrips[i].anchoredPosition = Vector2.zero;
                CopyRobotVisual(i < _items.Count ? _items[i].IconBackground : null, _reelCells[i][0]);
                _reelWindows[i].gameObject.SetActive(true);
            }
        }

        private void SetSlotRefreshTargets()
        {
            if (!IsRefreshing) return;
            var slots = Context.GetState(BattleSide.Player).Slots;
            for (var i = 0; i < _reelWindows.Length; i++)
            {
                var offer = slots[i].Offer;
                var icon = ConfigSystem.Instance.Tables.TbRobot.Get(offer.RobotId).RobotIcon;
                Image source = null;
                foreach (var entry in _reelPalette)
                    if (entry.name == icon) { source = entry; break; }
                var cells = _reelCells[i];
                var target = cells[cells.Length - 1];
                CopyRobotVisual(source, target);
                target.color = RobotLevelStyle.ColorForLevel(offer.Level);
                target.transform.Find("m_img_LevelFrame").GetComponent<Image>().color = RobotLevelStyle.FrameColorForLevel(offer.Level);
            }
        }

        private static void CopyRobotVisual(Image source, Image target)
        {
            CopyImage(source, target);
            foreach (var child in new[] { "m_img_LevelFrame", "m_img_RobotPortrait" })
                CopyImage(source != null ? source.transform.Find(child).GetComponent<Image>() : null,
                    target.transform.Find(child).GetComponent<Image>());
        }

        private static void CopyImage(Image source, Image target)
        {
            target.sprite = source != null ? source.sprite : null;
            target.color = source != null ? source.color : Color.white;
            target.enabled = source != null && source.enabled && source.sprite != null;
        }

        private void UpdateSlotRefresh()
        {
            if (!IsRefreshing) return;
            if (Context == null || Context != _refreshContext || Context.IsEnded || Context.IsEndRequested ||
                _host == null || !_host.CanInteractWithRobots || !gameObject.activeInHierarchy || !OwnerWindow.Visible)
            {
                CancelSlotRefresh();
                RefreshView();
                return;
            }
            var elapsed = Time.unscaledTime - _refreshStartedAt;
            var pull = elapsed < 0.18f ? Mathf.SmoothStep(0, 46, elapsed / 0.18f) :
                elapsed < 0.245f ? 46 : elapsed < 0.43f ? Mathf.SmoothStep(46, -4, (elapsed - 0.245f) / 0.185f) :
                elapsed < 0.5f ? Mathf.SmoothStep(-4, 0, (elapsed - 0.43f) / 0.07f) : 0;
            _leverPivot.localRotation = _leverRotation * Quaternion.Euler(0, 0, -pull);
            for (var i = 0; i < _reelWindows.Length; i++)
            {
                var progress = Mathf.Clamp01((elapsed - REEL_DELAY) / (REEL_SECONDS + i * REEL_STAGGER));
                var turns = _reelCells[i].Length - 1;
                // 同一条连续纵向图带，先加速、匀速，再减速到真实商品。
                var offset = progress < 0.15f ? 3f * Mathf.Pow(progress / 0.15f, 2) :
                    progress < 0.68f ? Mathf.Lerp(3, turns - 4, (progress - 0.15f) / 0.53f) :
                    Mathf.Lerp(turns - 4, turns, 1 - Mathf.Pow(1 - (progress - 0.68f) / 0.32f, 3));
                _reelStrips[i].anchoredPosition = new Vector2(0, offset * _reelWindows[i].sizeDelta.y);
                if (progress >= 1f && !_reelStopped[i])
                {
                    _reelStopped[i] = true;
                    _reelWindows[i].gameObject.SetActive(false);
                }
                if (i < _items.Count) PresentSlotItem(i, elapsed);
            }
            if (elapsed < REEL_DELAY + REEL_SECONDS + (_reelWindows.Length - 1) * REEL_STAGGER + LANDING_SECONDS) return;
            CancelSlotRefresh();
            RefreshView();
        }

        private void PresentSlotItem(int index, float elapsed)
        {
            var landed = IsRefreshing && index < _reelStopped.Length && _reelStopped[index];
            var progress = Mathf.Clamp01((elapsed - REEL_DELAY - REEL_SECONDS - index * REEL_STAGGER) / LANDING_SECONDS);
            var scale = !landed ? 1f : progress < 0.6f ?
                Mathf.SmoothStep(0.97f, 1.025f, progress / 0.6f) : Mathf.SmoothStep(1.025f, 1, (progress - 0.6f) / 0.4f);
            _items[index].SetReelPresentation(IsRefreshing && !landed, !IsRefreshing, scale);
        }

        internal void CancelSlotRefresh()
        {
            IsRefreshing = false;
            _refreshContext = null;
            if (_leverPivot != null) _leverPivot.localRotation = _leverRotation;
            foreach (var window in _reelWindows) if (window != null) window.gameObject.SetActive(false);
            foreach (var item in _items) if (item != null && item.gameObject != null) item.SetReelPresentation(false, true);
        }
    }
}
