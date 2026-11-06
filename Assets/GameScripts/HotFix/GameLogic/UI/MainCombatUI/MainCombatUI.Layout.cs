using UnityEngine;

namespace GameLogic
{
    public sealed partial class MainCombatUI
    {
        private BattleScreenLayoutController _screenLayout;
        private RectTransform _layoutShop;
        private RectTransform _layoutInfo;
        private Vector2 _shopBasePosition;
        private Vector3 _shopBaseScale;
        private Vector3 _infoBasePosition;
        private Vector3 _infoBaseScale;
        private Vector2 _shopDesignSize;
        private Vector2 _infoUnitDesignSize;
        private readonly Vector3[] _layoutCorners = new Vector3[4];

        public float ScreenLayoutScale => Canvas.rootCanvas.scaleFactor;
        private Camera LayoutEventCamera => Canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : Canvas.rootCanvas.worldCamera;

        public void AttachScreenLayout(BattleScreenLayoutController controller)
        {
            if (_screenLayout != null && _screenLayout != controller) _screenLayout.Unbind();
            _screenLayout = controller;
            if (_layoutShop != null) return;
            _layoutShop = Shop.rectTransform;
            _layoutInfo = Info.rectTransform;
            _shopBasePosition = _layoutShop.anchoredPosition;
            _shopBaseScale = _layoutShop.localScale;
            _infoBasePosition = _layoutInfo.localPosition;
            _infoBaseScale = _layoutInfo.localScale;
        }

        public void DetachScreenLayout(BattleScreenLayoutController controller)
        {
            if (_screenLayout != controller) return;
            _screenLayout = null;
            if (_layoutShop != null)
            {
                _layoutShop.anchoredPosition = _shopBasePosition;
                _layoutShop.localScale = _shopBaseScale;
            }
            if (_layoutInfo != null)
            {
                _layoutInfo.localPosition = _infoBasePosition;
                _layoutInfo.localScale = _infoBaseScale;
            }
        }

        public Rect GetShopScreenRect() => GetScreenRect(Shop.rectTransform);
        public Rect GetInfoScreenRect() => GetScreenRect(Info.rectTransform);

        public Vector2 ShopDesignSize
        {
            get
            {
                if (_shopDesignSize == Vector2.zero)
                    _shopDesignSize = GetDesignSize(_layoutShop, _shopBaseScale);
                return _shopDesignSize;
            }
        }

        public bool TryCalculateShopLayout(Rect safeArea, out Rect shopScreenRect, out Vector3 position, out Vector3 scale)
        {
            shopScreenRect = default;
            position = scale = default;
            if (_layoutShop == null || safeArea.width <= 24f * ScreenLayoutScale || safeArea.height <= 0f) return false;
            // 固定设计尺寸，避免从已移动节点的投影四角相减引入舍入漂移。
            var baseline = ShopDesignSize * ScreenLayoutScale;
            if (baseline.x <= 0f) return false;
            var fit = Mathf.Min(1f, (safeArea.width - 24f * ScreenLayoutScale) / baseline.x);
            scale = new Vector3(_shopBaseScale.x * fit, _shopBaseScale.y * fit, _shopBaseScale.z);
            var size = baseline * fit;
            shopScreenRect = new Rect(safeArea.center.x - size.x * 0.5f, safeArea.yMin + 12f * ScreenLayoutScale, size.x, size.y);
            return shopScreenRect.yMax < safeArea.yMax && TryPreparePosition(_layoutShop, scale, shopScreenRect.center, out position);
        }

        public bool TryCalculateInfoLayout(Rect screenRect, out Vector3 position, out Vector3 scale)
        {
            position = scale = default;
            if (_layoutInfo == null || screenRect.width <= 0 || screenRect.height <= 0) return false;
            if (_infoUnitDesignSize == Vector2.zero)
                _infoUnitDesignSize = GetDesignSize(_layoutInfo, Vector3.one);
            var baseline = _infoUnitDesignSize * ScreenLayoutScale;
            if (baseline.x <= 0 || baseline.y <= 0) return false;
            var fit = Mathf.Min(screenRect.width / baseline.x, screenRect.height / baseline.y);
            scale = new Vector3(fit, fit, _infoBaseScale.z);
            return TryPreparePosition(_layoutInfo, scale, screenRect.center, out position);
        }

        public void ApplyScreenLayout(Vector3 shopPosition, Vector3 shopScale, Vector3 infoPosition, Vector3 infoScale)
        {
            _layoutShop.localScale = shopScale;
            _layoutShop.localPosition = shopPosition;
            _layoutInfo.localScale = infoScale;
            _layoutInfo.localPosition = infoPosition;
        }

        public bool TryLayoutShop(Rect safeArea, out Rect shopScreenRect)
        {
            Vector3 position, scale;
            if (!TryCalculateShopLayout(safeArea, out shopScreenRect, out position, out scale)) return false;
            _layoutShop.localScale = scale;
            _layoutShop.localPosition = position;
            return true;
        }

        public bool TryLayoutInfo(Camera sceneCamera, Bounds upper, Bounds lower, Rect safeArea)
        {
            var parent = _layoutInfo.parent as RectTransform;
            Rect upperLocal, lowerLocal, safeLocal;
            if (!TryProjectBounds(sceneCamera, upper, parent, out upperLocal) ||
                !TryProjectBounds(sceneCamera, lower, parent, out lowerLocal) ||
                !TryScreenRectToLocal(safeArea, parent, out safeLocal)) return false;
            Vector2 center;
            float scale;
            if (!BattleInfoLayout.TryCalculate(upperLocal, lowerLocal, safeLocal, out center, out scale)) return false;
            _layoutInfo.localPosition = new Vector3(center.x, center.y, _infoBasePosition.z);
            // BattleInfoLayout 使用当前窗口 1040x156 的设计尺寸。
            _layoutInfo.localScale = new Vector3(scale, scale, _infoBaseScale.z);
            return true;
        }

        private Rect GetScreenRect(RectTransform rect)
        {
            rect.GetWorldCorners(_layoutCorners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in _layoutCorners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(LayoutEventCamera, corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Rect GetScreenRect(RectTransform rect, Vector3 localScale)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? rect.rect.xMin : rect.rect.xMax,
                    (i & 2) == 0 ? rect.rect.yMin : rect.rect.yMax, 0);
                var world = rect.parent.TransformPoint(rect.localPosition + rect.localRotation * Vector3.Scale(corner, localScale));
                var screen = RectTransformUtility.WorldToScreenPoint(LayoutEventCamera, world);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Vector2 GetDesignSize(RectTransform rect, Vector3 localScale)
        {
            var x = rect.localRotation * Vector3.Scale(Vector3.right * rect.rect.width, localScale);
            var y = rect.localRotation * Vector3.Scale(Vector3.up * rect.rect.height, localScale);
            // 仅累计到根Canvas的线性变换，不让节点位置或UI相机浮点投影参与参考尺寸。
            var rootCanvas = rect.GetComponentInParent<Canvas>().rootCanvas.transform;
            for (var parent = rect.parent; parent != null && parent != rootCanvas; parent = parent.parent)
            {
                x = parent.localRotation * Vector3.Scale(x, parent.localScale);
                y = parent.localRotation * Vector3.Scale(y, parent.localScale);
            }
            return new Vector2(Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y));
        }

        private bool TryPreparePosition(RectTransform target, Vector3 scale, Vector2 screenCenter, out Vector3 position)
        {
            var delta = screenCenter - GetScreenRect(target, scale).center;
            var pivot = RectTransformUtility.WorldToScreenPoint(LayoutEventCamera, target.position);
            Vector2 local;
            var success = RectTransformUtility.ScreenPointToLocalPointInRectangle(target.parent as RectTransform,
                pivot + delta, LayoutEventCamera, out local);
            position = new Vector3(local.x, local.y, target.localPosition.z);
            return success;
        }

        private bool MoveInScreen(RectTransform target, Vector2 delta)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(LayoutEventCamera, target.position);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(target.parent as RectTransform,
                screen + delta, LayoutEventCamera, out local)) return false;
            target.localPosition = new Vector3(local.x, local.y, target.localPosition.z);
            return true;
        }

        private bool TryScreenRectToLocal(Rect screen, RectTransform parent, out Rect local)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < 4; i++)
            {
                Vector2 point;
                var corner = new Vector2((i & 1) == 0 ? screen.xMin : screen.xMax, (i & 2) == 0 ? screen.yMin : screen.yMax);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, corner, LayoutEventCamera, out point))
                { local = default; return false; }
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            local = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private bool TryProjectBounds(Camera camera, Bounds bounds, RectTransform parent, out Rect local)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector2 point;
                var screen = camera.WorldToScreenPoint(corner);
                if (screen.z <= 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen,
                    LayoutEventCamera, out point)) { local = default; return false; }
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            local = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }
    }
}
