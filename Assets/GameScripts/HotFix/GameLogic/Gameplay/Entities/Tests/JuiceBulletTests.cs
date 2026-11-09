using System;
using System.Collections.Generic;
using System.Linq;
using GameConfig.bullet;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class JuiceBulletTests
    {
        [Test]
        public void DropsFollowCenterLineAroundCornerAndDiscardBeyondEnd()
        {
            var route = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0) };
            var drops = BattleRouteSampler.SampleDrops(route, new Vector3(0.8f, -1, 0), 5, 0.3f);
            Assert.AreEqual(5, drops.Count);
            Assert.That(Vector3.Distance(new Vector3(1, 0.1f, 0), drops[1]), Is.LessThan(0.00001f));
            Assert.That(Vector3.Distance(new Vector3(1, 1, 0), drops[4]), Is.LessThan(0.00001f));
            drops = BattleRouteSampler.SampleDrops(route, new Vector3(2, 0.9f, 0), 5, 0.3f);
            Assert.AreEqual(1, drops.Count, "超出终点不重复堆叠");
        }

        [Test]
        public void ProjectionTieChoosesEarlierDistanceAndZeroSegmentsAreSafe()
        {
            var drops = BattleRouteSampler.SampleDrops(new[] { Vector3.zero, Vector3.zero, Vector3.right,
                Vector3.zero }, Vector3.right * 0.5f, 2, 0.2f);
            Assert.That(drops[1].x, Is.EqualTo(0.7f).Within(0.00001));
        }

        [Test]
        public void EachDropHitsCentersInsideXYRadiusOnceAndDifferentDropsStack()
        {
            using (var battle = new BattleRuntime(704))
            {
                var source = battle.Registry.CreateRobot(704, BattleSide.Player, 1, 1603);
                var first = battle.Registry.CreateEnemy(1, BattleSide.Player, 100, 1);
                var edge = battle.Registry.CreateEnemy(1, BattleSide.Player, 100, 1);
                var outside = battle.Registry.CreateEnemy(1, BattleSide.Player, 100, 1);
                var other = battle.Registry.CreateEnemy(1, BattleSide.Opponent, 100, 1);
                var world = new World(battle);
                world.Targets.Add(new BulletTarget(first, Vector3.zero, 0.3f));
                world.Targets.Add(new BulletTarget(edge, new Vector3(0.2f, 0, 9), 0.3f));
                world.Targets.Add(new BulletTarget(outside, Vector3.right * 0.201f, 0.3f));
                world.Targets.Add(new BulletTarget(other, Vector3.zero, 0.3f));
                var presents = 0;
                var behavior = new SplashBulletBehavior();
                for (var i = 0; i < 2; i++)
                {
                    var projectile = battle.Combat.CaptureProjectileAttack(new CombatActionSnapshot(704, source.InstanceId,
                        BattleSide.Player, 1604, Array.Empty<int>(), CombatOutcomeTag.SingleDamage, 12, Vector3.left, Vector3.zero, i + 1));
                    var model = new BulletModel(3604, "test", BulletFlightType.Parabolic, false, BulletHitMode.AreaAtLanding,
                        0, 0.7f, 1.2f, 0, "splash");
                    BulletHitContext hit = null;
                    var bullet = new BulletState(new BulletLauncher(model, projectile.Action), world, (state, id) =>
                    {
                        hit = new BulletHitContext(state, id, projectile, battle.Combat, world,
                            present: p => { presents++; Assert.AreEqual(Vector3.zero, p[0]); }, splashRadius: 0.2f);
                        behavior.OnHit(hit);
                    });
                    Assert.IsFalse(bullet.Advance(0.69f));
                    Assert.IsTrue(bullet.Advance(0.01f));
                    behavior.OnHit(hit);
                }
                Assert.AreEqual(76, Hp(battle, first));
                Assert.AreEqual(76, Hp(battle, edge));
                Assert.AreEqual(100, Hp(battle, outside));
                Assert.AreEqual(100, Hp(battle, other));
                Assert.AreEqual(2, presents);
            }
        }

        private static int Hp(BattleRuntime battle, int id)
        { battle.Registry.TryGet(id, out var entity); return ((EnemyEntity)entity).CurrentHp; }
        private sealed class World : IProjectileWorld
        {
            private readonly BattleRuntime _battle;
            internal readonly List<BulletTarget> Targets = new List<BulletTarget>();
            internal World(BattleRuntime battle) { _battle = battle; }
            public bool IsRunning => true;
            public bool IsValid(long id) => id == _battle.BattleId && !_battle.IsDisposed;
            public Bounds GetBattleBounds(BattleSide side) => new Bounds(Vector3.zero, Vector3.one * 100);
            public IReadOnlyList<BulletTarget> GetTargets(BattleSide side) => Targets.Where(t =>
                _battle.Registry.TryGet(t.InstanceId, out var e) && e.Side == side && e is EnemyEntity enemy && enemy.IsAlive).ToArray();
            public bool TryGetPosition(BattleSide side, int id, out Vector3 position)
            {
                foreach (var t in GetTargets(side)) if (t.InstanceId == id) { position = t.Position; return true; }
                position = default; return false;
            }
            public bool TryGetDeathPosition(BattleSide side, int id, out Vector3 position) { position = default; return false; }
        }
    }
}
