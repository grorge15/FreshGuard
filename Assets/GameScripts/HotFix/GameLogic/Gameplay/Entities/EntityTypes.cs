using System;

namespace GameLogic
{
    public enum BattleSide { Player, Opponent }
    public enum RobotLocation { Unplaced, Board, Storage, PurchasedShopSlot }
    public enum EntityRemovalReason { None, Consumed, Killed, Leaked, BattleEnded, Manual, Broken }
    public enum EntityKind { Robot, Enemy, Glass }
    public enum DamageResult { Ignored, Damaged, Killed }
    public enum GlassKind { Normal = 0, Colored = 1 }
    public enum GlassCollisionResult { Ignored, Damaged, Broken }

    internal static class EntityValidation
    {
        internal static void RequirePositive(int value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName, "必须为正整数。");
        }

        internal static void RequireSide(BattleSide side)
        {
            if (side != BattleSide.Player && side != BattleSide.Opponent)
                throw new ArgumentOutOfRangeException(nameof(side));
        }

        internal static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        internal static void RequireFiniteNonNegative(float value, string parameterName)
        {
            if (!IsFiniteNonNegative(value))
                throw new ArgumentOutOfRangeException(parameterName, "必须为有限非负数。");
        }
    }
}
