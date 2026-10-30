using UnityEngine;

namespace GameLogic
{
    /// <summary>等级底板与同色边框，立绘保持原色；底部金色星星表示局内等级。</summary>
    public static class RobotLevelStyle
    {
        public const int MaxStars = 5;
        public const float StarSpacing = 0.16f;
        // 仅底部一格的机器人缩小 20%，宽底板保留确认的尺寸与星距。
        public const float StarSpriteSize = 0.46f;
        public const float StarEdgeInset = 0.10f;
        public static readonly Color IconColor = new Color(0.16f, 0.19f, 0.24f);
        public static string PortraitForIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon) || !icon.EndsWith("Icon", System.StringComparison.Ordinal))
                throw new System.ArgumentException("机器人底板地址必须以 Icon 结尾。", nameof(icon));
            return icon.Substring(0, icon.Length - 4) + "Portrait";
        }

        public static string FrameForIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon) || !icon.EndsWith("Icon", System.StringComparison.Ordinal))
                throw new System.ArgumentException("机器人底板地址必须以 Icon 结尾。", nameof(icon));
            return icon.Substring(0, icon.Length - 4) + "LevelFrame";
        }

        public static Color ColorForLevel(int level)
        {
            switch (Mathf.Clamp(level, 1, MaxStars))
            {
                case 2: return new Color32(173, 221, 186, 255);
                case 3: return new Color32(176, 213, 247, 255);
                case 4: return new Color32(212, 185, 239, 255);
                case 5: return new Color32(244, 211, 132, 255);
                default: return new Color32(238, 243, 247, 255);
            }
        }

        public static Color FrameColorForLevel(int level)
        {
            switch (Mathf.Clamp(level, 1, MaxStars))
            {
                case 2: return new Color32(52, 134, 81, 255);
                case 3: return new Color32(40, 108, 176, 255);
                case 4: return new Color32(128, 80, 180, 255);
                case 5: return new Color32(181, 125, 25, 255);
                default: return new Color32(167, 181, 195, 255);
            }
        }

        /// <summary>最底一排实际占格内的安全区，预留外框和相邻机器人之间的间隔。</summary>
        public static Rect StarArea(int[][] cells)
        {
            if (cells == null || cells.Length == 0)
                return new Rect(-0.5f + StarEdgeInset, -0.5f + StarEdgeInset,
                    1f - StarEdgeInset * 2f, 1f - StarEdgeInset * 2f);
            var bottom = int.MaxValue;
            foreach (var cell in cells) bottom = Mathf.Min(bottom, cell[1]);
            var left = int.MaxValue;
            var right = int.MinValue;
            foreach (var cell in cells)
            {
                if (cell[1] != bottom) continue;
                left = Mathf.Min(left, cell[0]);
                right = Mathf.Max(right, cell[0]);
            }
            return new Rect(left - 0.5f + StarEdgeInset, bottom - 0.5f + StarEdgeInset,
                right - left + 1f - StarEdgeInset * 2f, 1f - StarEdgeInset * 2f);
        }

        /// <summary>始终按五星适配，保证同一机器人的 1～5 星大小与间距一致。</summary>
        public static float StarScaleForCells(int[][] cells)
        {
            return StarArea(cells).width < 1f ? 0.8f : 1f;
        }

        public static float StarSpacingForCells(int[][] cells)
        {
            var area = StarArea(cells);
            return Mathf.Min(StarSpacing, Mathf.Max(0f,
                (area.width - StarSpriteSize * StarScaleForCells(cells)) / (MaxStars - 1)));
        }

        public static Vector2 StarAnchor(int[][] cells)
        {
            var area = StarArea(cells);
            return new Vector2(area.center.x, area.yMin + StarSpriteSize * StarScaleForCells(cells) * 0.5f);
        }
    }
}
