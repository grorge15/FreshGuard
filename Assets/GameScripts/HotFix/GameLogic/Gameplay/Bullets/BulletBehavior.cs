using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public interface IBulletBehavior
    {
        void OnHit(BulletHitContext context);
    }

    /// <summary>同步命中上下文；延迟状态仍由BulletState与冻结快照持有。</summary>
    public sealed class BulletHitContext
    {
        public BulletState Bullet { get; }
        public int? TargetId { get; }
        public Vector3 HitPosition => Bullet.Position;
        public float SplashRadius { get; }
        public ProjectileAttackSnapshot Attack { get; }
        public CombatRuntime Combat { get; }
        public IProjectileWorld World { get; }
        public ChainBulletSnapshot Chain { get; }
        public Action<IReadOnlyList<Vector3>> Present { get; }
        public BulletHitContext(BulletState bullet, int targetId, ProjectileAttackSnapshot attack,
            CombatRuntime combat, IProjectileWorld world, ChainBulletSnapshot chain = null,
            Action<IReadOnlyList<Vector3>> present = null, float splashRadius = 0f)
        {
            Bullet = bullet ?? throw new ArgumentNullException(nameof(bullet));
            if (bullet.Launcher.Model.HitMode == GameConfig.bullet.BulletHitMode.AreaAtLanding)
            {
                if (targetId != 0) throw new ArgumentException("位置命中不应携带实体ID。", nameof(targetId));
                TargetId = null;
            }
            else { EntityValidation.RequirePositive(targetId, nameof(targetId)); TargetId = targetId; }
            EntityValidation.RequireFiniteNonNegative(splashRadius, nameof(splashRadius));
            SplashRadius = splashRadius;
            Attack = attack ?? throw new ArgumentNullException(nameof(attack));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Chain = chain; Present = present;
        }
    }

    public sealed class BulletBehaviorRegistry
    {
        private readonly Dictionary<string, Func<IBulletBehavior>> _factories =
            new Dictionary<string, Func<IBulletBehavior>>();
        public BulletBehaviorRegistry()
        {
            Register("direct", () => new DirectBulletBehavior());
            Register("chain", () => new ChainBulletBehavior());
            Register("splash", () => new SplashBulletBehavior());
        }
        public void Register(string key, Func<IBulletBehavior> factory)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException(nameof(key));
            _factories.Add(key, factory ?? throw new ArgumentNullException(nameof(factory)));
        }
        public IBulletBehavior Create(string key)
        {
            if (key == null || !_factories.TryGetValue(key, out var factory))
                throw new InvalidOperationException("未注册弹丸行为：" + key);
            return factory() ?? throw new InvalidOperationException("弹丸行为工厂返回空：" + key);
        }
    }

    public sealed class DirectBulletBehavior : IBulletBehavior
    {
        public void OnHit(BulletHitContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var targetId = context.TargetId ?? throw new InvalidOperationException("普通弹缺少目标。");
            // 扣血可能移除目标，表现必须先冻结命中位置。
            var hasPosition = context.World.TryGetPosition(context.Attack.Action.SourceSide, targetId, out var position);
            var result = context.Combat.ResolveProjectileHit(context.Attack, targetId);
            if (hasPosition && result.DamageResult != DamageResult.Ignored)
                context.Present?.Invoke(new[] { context.Attack.Action.Origin, position });
        }
    }

    public sealed class ChainBulletBehavior : IBulletBehavior
    {
        public void OnHit(BulletHitContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var chain = context.Chain ?? throw new InvalidOperationException("链式弹丸缺少发射参数快照。");
            var world = context.World;
            var action = context.Attack.Action;
            if (!world.IsValid(action.BattleId) || !world.IsRunning ||
                !context.TargetId.HasValue || !world.TryGetPosition(action.SourceSide, context.TargetId.Value, out var position) ||
                !context.Attack.TryBeginChain()) return;

            var ids = new List<int> { context.TargetId.Value };
            // 起点只参与连线，不计入连锁人数或伤害衰减。
            var positions = new List<Vector3> { action.Origin, position };
            var selected = new HashSet<int> { context.TargetId.Value };
            // 世界查询返回共享缓存也可安全使用：所有选择都先于任意扣血回调。
            var candidates = world.GetTargets(action.SourceSide);
            var rangeSquared = (double)chain.Range * chain.Range;
            while (candidates != null && ids.Count < chain.TargetCount)
            {
                var bestId = 0;
                var bestDistance = double.PositiveInfinity;
                var bestPosition = default(Vector3);
                for (var i = 0; i < candidates.Count; i++)
                {
                    var target = candidates[i];
                    if (selected.Contains(target.InstanceId) ||
                        !world.TryGetPosition(action.SourceSide, target.InstanceId, out var targetPosition)) continue;
                    var dx = (double)targetPosition.x - position.x;
                    var dy = (double)targetPosition.y - position.y;
                    var dz = (double)targetPosition.z - position.z;
                    var distance = dx * dx + dy * dy + dz * dz;
                    if (distance > rangeSquared || distance > bestDistance ||
                        (distance == bestDistance && target.InstanceId >= bestId)) continue;
                    bestId = target.InstanceId; bestDistance = distance; bestPosition = targetPosition;
                }
                if (bestId == 0) break;
                selected.Add(bestId); ids.Add(bestId); positions.Add(bestPosition); position = bestPosition;
            }
            var frozenPositions = positions.AsReadOnly();
            context.Combat.ResolveChainHit(context.Attack, ids, chain.DamageRatio);
            context.Present?.Invoke(frozenPositions);
        }
    }

    public sealed class SplashBulletBehavior : IBulletBehavior
    {
        public void OnHit(BulletHitContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var action = context.Attack.Action;
            if (context.TargetId.HasValue || context.SplashRadius <= 0f ||
                !context.World.IsValid(action.BattleId) || !context.World.IsRunning || !context.Attack.TryBeginSplash()) return;
            var ids = new SortedSet<int>();
            var center = context.HitPosition;
            var radiusSquared = (double)context.SplashRadius * context.SplashRadius;
            var candidates = context.World.GetTargets(action.SourceSide);
            if (candidates != null) foreach (var candidate in candidates)
            {
                if (!context.World.TryGetPosition(action.SourceSide, candidate.InstanceId, out var position)) continue;
                var dx = (double)position.x - center.x; var dy = (double)position.y - center.y;
                if (dx * dx + dy * dy <= radiusSquared) ids.Add(candidate.InstanceId);
            }
            foreach (var id in ids)
            {
                if (!context.World.IsValid(action.BattleId) || !context.World.IsRunning) break;
                context.Combat.ResolveProjectileHit(context.Attack, id);
            }
            context.Present?.Invoke(new[] { center });
        }
    }
}
