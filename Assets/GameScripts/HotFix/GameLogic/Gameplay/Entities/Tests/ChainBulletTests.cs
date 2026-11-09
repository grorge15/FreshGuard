using System;
using System.Collections.Generic;
using GameConfig.bullet;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class ChainBulletTests
    {
        private BattleEntityRegistry _registry;
        private BattleRuntime _battle;
        private RobotEntity _source;
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _battle = new BattleRuntime(704, _registry, new CombatRuntime(704));
            _source = _registry.CreateRobot(701, BattleSide.Player, 1, 1605);
            _world = new World(_registry);
        }
        [TearDown] public void TearDown() { _battle.Dispose(); _registry.Dispose(); }

        [Test]
        public void NearestHopUsesInclusiveRangeStableIdsAndNoRepeatedTargets()
        {
            var first = Enemy(Vector3.zero);
            var lowerId = Enemy(Vector3.right);
            var upperId = Enemy(Vector3.left);
            var third = Enemy(Vector3.right * 2);
            Enemy(Vector3.right * 3.01f);
            Enemy(Vector3.zero, BattleSide.Opponent);
            // 故意倒序并重复候选，顺序必须由距离、实例ID决定。
            _world.Targets.Reverse(); _world.Targets.Add(new BulletTarget(lowerId, Vector3.right, 0));
            var seen = new List<int>(); IReadOnlyList<Vector3> points = null;
            _battle.Combat.TargetResolved += c => seen.Add(c.Result.TargetInstanceId);
            var context = Context(first, new ChainBulletSnapshot(5, 1, 0.7f), p => points = p);
            new BulletBehaviorRegistry().Create("chain").OnHit(context);
            CollectionAssert.AreEqual(new[] { first, lowerId, third }, seen);
            CollectionAssert.AreEqual(new[] { Vector3.zero, Vector3.right, Vector3.right * 2 }, points);
            Assert.IsFalse(seen.Contains(upperId));
        }

        [Test]
        public void InvalidCandidateIsSkippedEvenWhenReturnedByWorldQuery()
        {
            var first = Enemy(Vector3.zero);
            var invalid = Enemy(Vector3.right * .25f);
            var valid = Enemy(Vector3.right * .5f);
            _world.InvalidPositions.Add(invalid);
            var seen = new List<int>();
            _battle.Combat.TargetResolved += c => seen.Add(c.Result.TargetInstanceId);
            new ChainBulletBehavior().OnHit(Context(first, new ChainBulletSnapshot(3, 1, .7f)));
            CollectionAssert.AreEqual(new[] { first, valid }, seen);
            _registry.TryGet(invalid, out var entity);
            Assert.That(((EnemyEntity)entity).CurrentHp, Is.EqualTo(1000));
        }

        [Test]
        public void LocksAllTargetsAndPositionsBeforeDamageAndIgnoresReentry()
        {
            var first = Enemy(Vector3.zero);
            var second = Enemy(Vector3.right);
            var third = Enemy(Vector3.right * 2);
            var behavior = new BulletBehaviorRegistry().Create("chain");
            var seen = new List<int>(); IReadOnlyList<Vector3> points = null;
            var context = Context(first, new ChainBulletSnapshot(3, 1, 0.7f), p => points = p);
            var replacement = 0;
            _battle.Combat.TargetResolved += c =>
            {
                seen.Add(c.Result.TargetInstanceId);
                if (c.Result.TargetInstanceId != first) return;
                behavior.OnHit(context);
                _registry.Remove(second, EntityRemovalReason.Manual, out _);
                _world.Targets.Clear();
                replacement = Enemy(Vector3.right);
            };
            behavior.OnHit(context); behavior.OnHit(context);
            CollectionAssert.AreEqual(new[] { first, third }, seen);
            CollectionAssert.AreEqual(new[] { Vector3.zero, Vector3.right, Vector3.right * 2 }, points);
            _registry.TryGet(third, out var last); Assert.AreEqual(990, ((EnemyEntity)last).CurrentHp);
            _registry.TryGet(replacement, out var added); Assert.AreEqual(1000, ((EnemyEntity)added).CurrentHp);
        }

        [Test]
        public void CountIncludesFirstAndDirectBehaviorRetainsOriginalDamage()
        {
            var first = Enemy(Vector3.zero); var second = Enemy(Vector3.right);
            new BulletBehaviorRegistry().Create("chain").OnHit(Context(first, new ChainBulletSnapshot(1, 10, 0.7f)));
            _registry.TryGet(first, out var a); Assert.AreEqual(980, ((EnemyEntity)a).CurrentHp);
            _registry.TryGet(second, out var b); Assert.AreEqual(1000, ((EnemyEntity)b).CurrentHp);
            new BulletBehaviorRegistry().Create("direct").OnHit(Context(first));
            Assert.AreEqual(960, ((EnemyEntity)a).CurrentHp);
        }

        [Test]
        public void FirstEnemySealsStateBeforeHitCallbackAndOnlyRunsOnce()
        {
            var first = Enemy(Vector3.right); Enemy(Vector3.right * 2);
            var action = Context(first).Attack.Action;
            var state = new BulletState(new BulletLauncher(new BulletModel(3600, "test", BulletFlightType.Straight,
                false, BulletHitMode.FirstEnemy, 20, 0, 0, 0.05f, "chain"), action), _world, (s, id) =>
                { Assert.IsTrue(s.IsResolved); Assert.IsTrue(s.Advance(1)); });
            Assert.IsTrue(state.Advance(1)); Assert.AreEqual(1, state.HitCount);
        }

        [Test]
        public void RegistryCreatesIndependentInstancesAndSnapshotRejectsInvalidParameters()
        {
            var registry = new BulletBehaviorRegistry();
            Assert.AreNotSame(registry.Create("chain"), registry.Create("chain"));
            Assert.Throws<InvalidOperationException>(() => registry.Create("missing"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChainBulletSnapshot(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChainBulletSnapshot(1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChainBulletSnapshot(1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChainBulletSnapshot(1, 1, 1.1f));
        }

        private int Enemy(Vector3 position, BattleSide side = BattleSide.Player)
        {
            var id = _registry.CreateEnemy(1, side, 1000, 1);
            _world.Targets.Add(new BulletTarget(id, position, 0)); return id;
        }
        private BulletHitContext Context(int first, ChainBulletSnapshot chain = null,
            Action<IReadOnlyList<Vector3>> present = null)
        {
            var attack = _battle.Combat.CaptureProjectileAttack(new CombatActionSnapshot(704, _source.InstanceId,
                BattleSide.Player, 1605, new[] { first }, CombatOutcomeTag.SingleDamage, 20, Vector3.left,
                Vector3.right, 1));
            var bullet = new BulletState(new BulletLauncher(new BulletModel(1, 0), attack.Action), _world, s => { });
            return new BulletHitContext(bullet, first, attack, _battle.Combat, _world, chain, present);
        }
        private sealed class World : IProjectileWorld
        {
            private readonly BattleEntityRegistry _registry;
            internal readonly List<BulletTarget> Targets = new List<BulletTarget>();
            internal readonly HashSet<int> InvalidPositions = new HashSet<int>();
            internal World(BattleEntityRegistry registry) { _registry = registry; }
            public bool IsRunning => true;
            public bool IsValid(long battleId) => battleId == 704;
            public Bounds GetBattleBounds(BattleSide side) => new Bounds(Vector3.zero, Vector3.one * 100);
            public IReadOnlyList<BulletTarget> GetTargets(BattleSide side)
            {
                var result = new List<BulletTarget>();
                foreach (var candidate in Targets)
                    if (_registry.TryGet(candidate.InstanceId, out var entity) && entity.Side == side &&
                        entity is EnemyEntity enemy && enemy.IsAlive) result.Add(candidate);
                return result;
            }
            public bool TryGetPosition(BattleSide side, int id, out Vector3 position)
            {
                if (InvalidPositions.Contains(id)) { position = default; return false; }
                foreach (var target in GetTargets(side))
                    if (target.InstanceId == id) { position = target.Position; return true; }
                position = default; return false;
            }
            public bool TryGetDeathPosition(BattleSide side, int id, out Vector3 position)
            { position = default; return false; }
        }
    }
}
