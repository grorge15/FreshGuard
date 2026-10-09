using UnityEngine;

namespace GameLogic
{
    /// <summary>局内等级配色；固定品质仍由机器人配置决定。</summary>
    public static class RobotLevelStyle
    {
        public static readonly Color IconColor = new Color(0.16f, 0.19f, 0.24f);
        public static Color ColorForLevel(int level)
        {
            switch (level)
            {
                case 2: return new Color(0.37f, 0.78f, 0.42f);
                case 3: return new Color(0.28f, 0.57f, 0.95f);
                case 4: return new Color(0.70f, 0.39f, 0.88f);
                case 5: return new Color(1f, 0.76f, 0.20f);
                default: return Color.white;
            }
        }
    }
}
