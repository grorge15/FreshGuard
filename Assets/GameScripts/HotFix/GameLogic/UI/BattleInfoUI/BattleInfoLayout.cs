using UnityEngine;

namespace GameLogic
{
    /// <summary>所有输入均为同一 UI 容器的局部坐标；保留两战场之间的空隙和安全区边距。</summary>
    public static class BattleInfoLayout
    {
        public static bool TryCalculate(Rect upperBattle, Rect lowerBattle, Rect safeArea,
            out Vector2 center, out float scale)
        {
            var bottom = Mathf.Max(lowerBattle.yMax, safeArea.yMin);
            var top = Mathf.Min(upperBattle.yMin, safeArea.yMax);
            center = new Vector2((upperBattle.center.x + lowerBattle.center.x) * 0.5f, (bottom + top) * 0.5f);
            var width = 2f * Mathf.Min(center.x - safeArea.xMin, safeArea.xMax - center.x) - 32f;
            var height = top - bottom - 32f;
            scale = Mathf.Clamp01(Mathf.Min(width / 1040f, height / 156f));
            return width > 0f && height > 0f;
        }
    }
}
