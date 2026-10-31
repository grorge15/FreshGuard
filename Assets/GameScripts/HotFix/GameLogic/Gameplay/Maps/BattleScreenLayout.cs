using UnityEngine;

namespace GameLogic
{
    /// <summary>将固定的世界边界等比容纳到全屏正交相机中的指定像素区域。</summary>
    public static class BattleScreenLayout
    {
        public static bool TryCalculate(Vector2 screenSize, Rect available, Bounds worldBounds,
            out Vector2 cameraCenter, out float orthographicSize)
        {
            cameraCenter = default;
            orthographicSize = 0f;
            if (!Positive(screenSize.x) || !Positive(screenSize.y) ||
                !Positive(available.width) || !Positive(available.height) ||
                !Finite(available.x) || !Finite(available.y) ||
                available.xMin < 0 || available.yMin < 0 ||
                available.xMax > screenSize.x || available.yMax > screenSize.y ||
                !Positive(worldBounds.size.x) || !Positive(worldBounds.size.y) ||
                !Finite(worldBounds.center.x) || !Finite(worldBounds.center.y)) return false;
            var pixelsPerUnit = Mathf.Min(available.width / worldBounds.size.x, available.height / worldBounds.size.y);
            if (!Positive(pixelsPerUnit)) return false;
            cameraCenter = (Vector2)worldBounds.center - (available.center - screenSize * 0.5f) / pixelsPerUnit;
            orthographicSize = screenSize.y / (2f * pixelsPerUnit);
            return Finite(cameraCenter.x) && Finite(cameraCenter.y) && Positive(orthographicSize);
        }

        public static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(
            Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Max(Mathf.Max(a.xMin, b.xMin), Mathf.Min(a.xMax, b.xMax)),
            Mathf.Max(Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.yMax, b.yMax)));

        private static bool Positive(float value) => Finite(value) && value > 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
