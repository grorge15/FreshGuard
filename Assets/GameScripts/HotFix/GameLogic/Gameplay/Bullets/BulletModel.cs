using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>共享的单目标追踪抛物线配置；没有伤害或技能业务。</summary>
    public sealed class BulletModel
    {
        public float FlightTime { get; }
        public float ArcHeight { get; }
        public BulletModel(float flightTime, float arcHeight)
        {
            EntityValidation.RequireFiniteNonNegative(flightTime, nameof(flightTime));
            EntityValidation.RequireFiniteNonNegative(arcHeight, nameof(arcHeight));
            if (flightTime <= 0f) throw new ArgumentOutOfRangeException(nameof(flightTime));
            FlightTime = flightTime; ArcHeight = arcHeight;
        }
    }

    public interface IBulletWorld
    {
        bool IsValid(long battleId);
        bool IsRunning { get; }
        bool TryGetPosition(BattleSide side, int targetId, out Vector3 position);
    }

    public sealed class BulletLauncher
    {
        public BulletModel Model { get; }
        public CombatActionSnapshot Action { get; }
        public BulletLauncher(BulletModel model, CombatActionSnapshot action)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Action = action ?? throw new ArgumentNullException(nameof(action));
            if (action.TargetInstanceIds.Count != 1) throw new ArgumentException("子弹必须是单目标。", nameof(action));
        }
    }
}
