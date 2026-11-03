using System;
using System.Collections.Generic;
using GameConfig.bullet;
using UnityEngine;

namespace GameLogic
{
    /// <summary>共享只读弹道配置；伤害和穿透人数由本次施法快照提供。</summary>
    public sealed class BulletModel
    {
        public int Id { get; }
        public string Prefab { get; }
        public BulletFlightType FlightType { get; }
        public bool TrackTarget { get; }
        public BulletHitMode HitMode { get; }
        public float Speed { get; }
        public float FlightTime { get; }
        public float ArcHeight { get; }
        public float HitRadius { get; }

        public BulletModel(int id, string prefab, BulletFlightType flightType, bool trackTarget,
            BulletHitMode hitMode, float speed, float flightTime, float arcHeight, float hitRadius)
        {
            EntityValidation.RequirePositive(id, nameof(id));
            if (string.IsNullOrWhiteSpace(prefab)) throw new ArgumentException(nameof(prefab));
            if (flightType != BulletFlightType.Straight && flightType != BulletFlightType.Parabolic)
                throw new ArgumentOutOfRangeException(nameof(flightType));
            if (hitMode != BulletHitMode.LockedTarget && hitMode != BulletHitMode.FirstEnemy &&
                hitMode != BulletHitMode.PierceEnemies) throw new ArgumentOutOfRangeException(nameof(hitMode));
            EntityValidation.RequireFiniteNonNegative(speed, nameof(speed));
            EntityValidation.RequireFiniteNonNegative(flightTime, nameof(flightTime));
            EntityValidation.RequireFiniteNonNegative(arcHeight, nameof(arcHeight));
            EntityValidation.RequireFiniteNonNegative(hitRadius, nameof(hitRadius));
            if (flightType == BulletFlightType.Straight && speed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(speed));
            if (flightType == BulletFlightType.Parabolic && flightTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(flightTime));
            Id = id; Prefab = prefab; FlightType = flightType; TrackTarget = trackTarget;
            HitMode = hitMode; Speed = speed; FlightTime = flightTime; ArcHeight = arcHeight; HitRadius = hitRadius;
        }

        // 保留旧调用的追踪抛物线语义，不伪造配置表ID。
        public BulletModel(float flightTime, float arcHeight)
        {
            EntityValidation.RequireFiniteNonNegative(flightTime, nameof(flightTime));
            EntityValidation.RequireFiniteNonNegative(arcHeight, nameof(arcHeight));
            if (flightTime <= 0f) throw new ArgumentOutOfRangeException(nameof(flightTime));
            Prefab = string.Empty; FlightType = BulletFlightType.Parabolic; TrackTarget = true;
            HitMode = BulletHitMode.LockedTarget; FlightTime = flightTime; ArcHeight = arcHeight;
        }
    }

    public readonly struct BulletTarget
    {
        public int InstanceId { get; }
        public Vector3 Position { get; }
        public float HitRadius { get; }
        public BulletTarget(int instanceId, Vector3 position, float hitRadius)
        {
            EntityValidation.RequirePositive(instanceId, nameof(instanceId));
            EntityValidation.RequireFiniteNonNegative(hitRadius, nameof(hitRadius));
            InstanceId = instanceId; Position = position; HitRadius = hitRadius;
        }
    }

    public interface IProjectileWorld : IBulletWorld
    {
        Bounds GetBattleBounds(BattleSide side);
        IReadOnlyList<BulletTarget> GetTargets(BattleSide side);
    }

    public interface IBulletWorld
    {
        bool IsValid(long battleId);
        bool IsRunning { get; }
        bool TryGetPosition(BattleSide side, int targetId, out Vector3 position);
        bool TryGetDeathPosition(BattleSide side, int targetId, out Vector3 position);
    }

    public sealed class BulletLauncher
    {
        public BulletModel Model { get; }
        public CombatActionSnapshot Action { get; }
        public Vector3 Direction { get; }
        public int MaxHitTargets { get; }
        public BulletLauncher(BulletModel model, CombatActionSnapshot action, Vector3? direction = null,
            int maxHitTargets = 1)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Action = action ?? throw new ArgumentNullException(nameof(action));
            if (model.FlightType == BulletFlightType.Parabolic && action.TargetInstanceIds.Count != 1)
                throw new ArgumentException("抛物线子弹必须是单目标。", nameof(action));
            EntityValidation.RequirePositive(maxHitTargets, nameof(maxHitTargets));
            var heading = direction ?? (action.AimPoint - action.Origin);
            if (!Finite(heading.x) || !Finite(heading.y) || !Finite(heading.z))
                throw new ArgumentOutOfRangeException(nameof(direction));
            heading.z = 0f;
            if (model.FlightType == BulletFlightType.Straight && heading.sqrMagnitude <= 0f)
                throw new ArgumentException("直线弹丸需要有效发射方向。", nameof(direction));
            Direction = heading.normalized;
            MaxHitTargets = model.HitMode == BulletHitMode.PierceEnemies ? maxHitTargets : 1;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
