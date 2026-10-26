using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>延迟状态只保存标识和发射快照，不保存池化实体引用。</summary>
    public sealed class BulletState
    {
        private readonly IBulletWorld _world;
        private readonly Action<BulletState> _onHit;
        public BulletLauncher Launcher { get; }
        public CombatActionSnapshot Action => Launcher.Action;
        public Vector3 Position { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsResolved { get; private set; }
        public BulletState(BulletLauncher launcher, IBulletWorld world, Action<BulletState> onHit)
        {
            Launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _onHit = onHit ?? throw new ArgumentNullException(nameof(onHit));
            Position = launcher.Action.Origin;
        }
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsResolved) return true;
            if (!_world.IsValid(Action.BattleId)) { Cancel(); return true; }
            if (!_world.IsRunning) return false;
            if (!_world.TryGetPosition(Action.SourceSide, Action.TargetInstanceIds[0], out var target))
            { Cancel(); return true; }
            Elapsed = Mathf.Min(Launcher.Model.FlightTime, Elapsed + deltaTime);
            var u = Elapsed / Launcher.Model.FlightTime;
            Position = Vector3.Lerp(Action.Origin, target, u) +
                Vector3.up * (4f * Launcher.Model.ArcHeight * u * (1f - u));
            if (Elapsed < Launcher.Model.FlightTime) return false;
            IsResolved = true; // 命中回调之前封住重入。
            _onHit(this);
            return true;
        }
        public void Cancel() { IsResolved = true; }
    }
}
