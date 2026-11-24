using UnityEngine;

namespace GameLogic
{
    public static class AoeManager
    {
        /// <summary>判断点是否位于 XY 平面的圆内，圆周也算命中。无效半径返回 false。</summary>
        public static bool ContainsCircle(Vector2 center, float radius, Vector2 point)
        {
            if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius)) return false;

            var dx = (double)point.x - center.x;
            var dy = (double)point.y - center.y;
            return dx * dx + dy * dy <= (double)radius * radius;
        }
    }
}
