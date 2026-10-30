using UnityEngine;

namespace GameLogic
{
    /// <summary>纯色底板按局内等级配色；立绘保持原色，固定品质由配置决定。</summary>
    public static class RobotLevelStyle
    {
        public static readonly Color IconColor = new Color(0.16f, 0.19f, 0.24f);
        public static string PortraitForIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon) || !icon.EndsWith("Icon", System.StringComparison.Ordinal))
                throw new System.ArgumentException("机器人底板地址必须以 Icon 结尾。", nameof(icon));
            return icon.Substring(0, icon.Length - 4) + "Portrait";
        }

        public static Color ColorForLevel(int level)
        {
            switch (level)
            {
                case 2: return new Color32(109, 187, 128, 255);
                case 3: return new Color32(90, 160, 229, 255);
                case 4: return new Color32(167, 121, 215, 255);
                case 5: return new Color32(237, 186, 69, 255);
                default: return new Color32(238, 243, 247, 255);
            }
        }
    }
}
