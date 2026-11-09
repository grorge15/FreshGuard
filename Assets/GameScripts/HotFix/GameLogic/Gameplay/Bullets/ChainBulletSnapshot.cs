using System;

namespace GameLogic
{
    /// <summary>发射时冻结链参数，命中不访问施法者或当前配置。</summary>
    public sealed class ChainBulletSnapshot
    {
        public int TargetCount { get; }
        public float Range { get; }
        public float DamageRatio { get; }
        public ChainBulletSnapshot(int targetCount, float range, float damageRatio)
        {
            EntityValidation.RequirePositive(targetCount, nameof(targetCount));
            EntityValidation.RequireFiniteNonNegative(range, nameof(range));
            EntityValidation.RequireFiniteNonNegative(damageRatio, nameof(damageRatio));
            if (range <= 0f) throw new ArgumentOutOfRangeException(nameof(range));
            if (damageRatio <= 0f || damageRatio > 1f) throw new ArgumentOutOfRangeException(nameof(damageRatio));
            TargetCount = targetCount; Range = range; DamageRatio = damageRatio;
        }
    }
}
