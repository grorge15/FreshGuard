using UnityEngine;

namespace GameLogic
{
    /// <summary>等级底板与同色边框，立绘保持原色。</summary>
    public static class RobotLevelStyle
    {
        public const int MaxLevel = 5;
        public static readonly Color IconColor = new Color(0.16f, 0.19f, 0.24f);
        public static string FrameForIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon) || !icon.EndsWith("Icon", System.StringComparison.Ordinal))
                throw new System.ArgumentException("机器人底板地址必须以 Icon 结尾。", nameof(icon));
            return icon.Substring(0, icon.Length - 4) + "LevelFrame";
        }

        public static Color ColorForLevel(int level)
        {
            switch (Mathf.Clamp(level, 1, MaxLevel))
            {
                case 2: return new Color32(173, 221, 186, 255);
                case 3: return new Color32(176, 213, 247, 255);
                case 4: return new Color32(212, 185, 239, 255);
                case 5: return new Color32(244, 211, 132, 255);
                default: return new Color32(238, 243, 247, 255);
            }
        }

        /// <summary>碰撞描边使用同色系高亮色，提升瞬间反馈的辨识度。</summary>
        public static Color ImpactColorForLevel(int level)
        {
            switch (Mathf.Clamp(level, 1, MaxLevel))
            {
                case 2: return new Color32(82, 255, 139, 255);
                case 3: return new Color32(80, 195, 255, 255);
                case 4: return new Color32(202, 125, 255, 255);
                case 5: return new Color32(255, 216, 79, 255);
                default: return Color.white;
            }
        }

        public static Color FrameColorForLevel(int level)
        {
            switch (Mathf.Clamp(level, 1, MaxLevel))
            {
                case 2: return new Color32(52, 134, 81, 255);
                case 3: return new Color32(40, 108, 176, 255);
                case 4: return new Color32(128, 80, 180, 255);
                case 5: return new Color32(181, 125, 25, 255);
                default: return new Color32(167, 181, 195, 255);
            }
        }

    }
}
