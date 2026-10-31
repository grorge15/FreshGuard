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

        public bool TryLayoutShop(Rect safeArea, out Rect shopScreenRect)
        {
            shopScreenRect = default;
            if (_layoutShop == null || safeArea.width <= 24f * ScreenLayoutScale || safeArea.height <= 0f) return false;
            _layoutShop.localScale = _shopBaseScale;
            var baseline = GetScreenRect(_layoutShop);
            if (baseline.width <= 0f) return false;
            var fit = Mathf.Min(1f, (safeArea.width - 24f * ScreenLayoutScale) / baseline.width);
            _layoutShop.localScale = new Vector3(_shopBaseScale.x * fit, _shopBaseScale.y * fit, _shopBaseScale.z);
            shopScreenRect = GetScreenRect(_layoutShop);
            var delta = new Vector2(safeArea.center.x - shopScreenRect.center.x,
                safeArea.yMin + 12f * ScreenLayoutScale - shopScreenRect.yMin);
            if (!MoveInScreen(_layoutShop, delta)) return false;
            shopScreenRect = GetScreenRect(_layoutShop);
            return shopScreenRect.yMax < safeArea.yMax;
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
