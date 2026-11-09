using System;
using System.Collections.Generic;
using GameConfig.bullet;
using UnityEngine;

namespace GameLogic
{
    /// <summary>延迟状态只保存标识和发射快照，不保存池化实体引用。</summary>
    public sealed class BulletState
    {
        private readonly IBulletWorld _world;
        private readonly Action<BulletState, int> _onHit;
        private readonly HashSet<int> _hitTargets = new HashSet<int>();
        private readonly List<Contact> _contacts = new List<Contact>();
        private Vector3 _targetPosition;
        private bool _targetLost;
        private bool _advancing;
        public BulletLauncher Launcher { get; }
        public CombatActionSnapshot Action => Launcher.Action;
        public Vector3 Position { get; private set; }
        public Vector3 Direction { get; private set; }
        public int HitCount => _hitTargets.Count;
        public float Elapsed { get; private set; }
        public bool IsResolved { get; private set; }
        public BulletState(BulletLauncher launcher, IBulletWorld world, Action<BulletState> onHit)
            : this(launcher, world, Adapt(onHit)) { }

        public BulletState(BulletLauncher launcher, IBulletWorld world, Action<BulletState, int> onHit)
        {
            Launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _onHit = onHit ?? throw new ArgumentNullException(nameof(onHit));
            if (launcher.Model.FlightType == BulletFlightType.Straight && !(world is IProjectileWorld))
                throw new ArgumentException("直线弹丸需要区域与受击目标查询。", nameof(world));
            Position = launcher.Action.Origin;
            Direction = launcher.Direction;
            _targetPosition = launcher.Action.AimPoint;
        }
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsResolved) return true;
            if (_advancing) return false;
            if (!_world.IsValid(Action.BattleId)) { Cancel(); return true; }
            if (!_world.IsRunning) return false;
            _advancing = true;
            try
            {
                return Launcher.Model.FlightType == BulletFlightType.Straight
                    ? AdvanceStraight(deltaTime) : AdvanceParabolic(deltaTime);
            }
            finally { _advancing = false; }
        }

        private bool AdvanceParabolic(float deltaTime)
        {
            var landing = Launcher.Model.HitMode == BulletHitMode.AreaAtLanding;
            if (!landing && !_targetLost)
            {
                if (_world.TryGetPosition(Action.SourceSide, Action.TargetInstanceIds[0], out var target))
                {
                    if (Launcher.Model.TrackTarget) _targetPosition = target;
                }
                else
                {
                    // 死亡坐标独立于已回收的实体/表现；无死亡记录时沿用最后一次瞄准点。
                    if (Launcher.Model.TrackTarget &&
                        _world.TryGetDeathPosition(Action.SourceSide, Action.TargetInstanceIds[0], out var deathPosition))
                        _targetPosition = deathPosition;
                    _targetLost = true;
                }
            }
            Elapsed = Mathf.Min(Launcher.Model.FlightTime, Elapsed + deltaTime);
            var u = Elapsed / Launcher.Model.FlightTime;
            var previousPosition = Position;
            Position = Vector3.Lerp(Action.Origin, _targetPosition, u) +
                Vector3.up * (4f * Launcher.Model.ArcHeight * u * (1f - u));
            var movement = Position - previousPosition;
            if (movement.sqrMagnitude > 0f) Direction = movement.normalized;
            if (Elapsed < Launcher.Model.FlightTime) return false;
            IsResolved = true; // 命中回调之前封住重入。
            if (landing)
            {
                if (_world.IsValid(Action.BattleId)) _onHit(this, 0); // 位置事件，不表示实体命中。
                return true;
            }
            if (!_targetLost && _world.IsValid(Action.BattleId) &&
                _world.TryGetPosition(Action.SourceSide, Action.TargetInstanceIds[0], out _))
            {
                var id = Action.TargetInstanceIds[0];
                _hitTargets.Add(id);
                _onHit(this, id);
            }
            return true;
        }

        private bool AdvanceStraight(float deltaTime)
        {
            if (deltaTime <= 0f) return false;
            var world = (IProjectileWorld)_world;
            var start = Position;
            var bounds = world.GetBattleBounds(Action.SourceSide);
            // 战斗为XY平面；Bounds的Z厚度可以为0。
            if (!ContainsXY(bounds, start)) { Cancel(); return true; }
            var distance = Launcher.Model.Speed * deltaTime;
            var fullEnd = start + Direction * distance;
            var exitFraction = 1f;
            ClipExit(start.x, fullEnd.x - start.x, bounds.min.x, bounds.max.x, ref exitFraction);
            ClipExit(start.y, fullEnd.y - start.y, bounds.min.y, bounds.max.y, ref exitFraction);
            var end = Vector3.Lerp(start, fullEnd, exitFraction);
            var exits = !ContainsXY(bounds, fullEnd);
            _contacts.Clear();
            var targets = world.GetTargets(Action.SourceSide);
            if (targets != null)
                for (var i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (_hitTargets.Contains(target.InstanceId)) continue;
                    if (TryContact(start, end, target.Position, Launcher.Model.HitRadius + target.HitRadius,
                        out var fraction)) _contacts.Add(new Contact(target.InstanceId, fraction));
                }
            _contacts.Sort((a, b) =>
            {
                var comparison = a.Fraction.CompareTo(b.Fraction);
                return comparison != 0 ? comparison : a.InstanceId.CompareTo(b.InstanceId);
            });
            var previousElapsed = Elapsed;
            foreach (var contact in _contacts)
            {
                if (IsResolved) return true;
                if (!_world.IsValid(Action.BattleId)) { Cancel(); return true; }
                if (!_world.IsRunning) return false;
                if (_hitTargets.Contains(contact.InstanceId) ||
                    !_world.TryGetPosition(Action.SourceSide, contact.InstanceId, out _)) continue;
                Position = Vector3.Lerp(start, end, contact.Fraction);
                Elapsed = previousElapsed + deltaTime * exitFraction * contact.Fraction;
                _hitTargets.Add(contact.InstanceId);
                if (HitCount >= Launcher.MaxHitTargets) IsResolved = true;
                _onHit(this, contact.InstanceId);
                if (!_world.IsValid(Action.BattleId)) Cancel();
                if (IsResolved) return true;
                if (!_world.IsRunning) return false;
            }
            Position = end;
            Elapsed = previousElapsed + deltaTime * exitFraction;
            if (exits) Cancel();
            return IsResolved;
        }

        private readonly struct Contact
        {
            internal int InstanceId { get; }
            internal float Fraction { get; }
            internal Contact(int instanceId, float fraction) { InstanceId = instanceId; Fraction = fraction; }
        }

        private static bool TryContact(Vector3 start, Vector3 end, Vector3 center, float radius,
            out float fraction)
        {
            var dx = end.x - start.x; var dy = end.y - start.y;
            var ox = start.x - center.x; var oy = start.y - center.y;
            var c = ox * ox + oy * oy - radius * radius;
            fraction = 0f;
            if (c <= 0f) return true;
            var a = dx * dx + dy * dy;
            if (a <= 0f) return false;
            var b = ox * dx + oy * dy;
            var discriminant = b * b - a * c;
            if (discriminant < 0f) return false;
            fraction = (-b - Mathf.Sqrt(discriminant)) / a;
            return fraction >= 0f && fraction <= 1f;
        }

        private static bool ContainsXY(Bounds bounds, Vector3 point) =>
            point.x >= bounds.min.x && point.x <= bounds.max.x &&
            point.y >= bounds.min.y && point.y <= bounds.max.y;

        private static void ClipExit(float origin, float delta, float min, float max, ref float fraction)
        {
            if (delta > 0f) fraction = Mathf.Min(fraction, (max - origin) / delta);
            else if (delta < 0f) fraction = Mathf.Min(fraction, (min - origin) / delta);
        }

        private static Action<BulletState, int> Adapt(Action<BulletState> onHit)
        {
            if (onHit == null) throw new ArgumentNullException(nameof(onHit));
            return (state, id) => onHit(state);
        }

        public void Cancel() { IsResolved = true; }
    }
}
