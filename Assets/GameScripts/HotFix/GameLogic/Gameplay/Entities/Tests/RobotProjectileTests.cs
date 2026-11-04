using System;
using System.Collections.Generic;
using GameConfig.bullet;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class RobotProjectileTests
    {
        [Test]
        public void StraightSweepHitsFirstContactEvenWhenFramePassesEntireTarget()
        {
            var world = new World();
            world.Add(9, 4f); world.Add(3, 2f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.FirstEnemy, 5, (s, id) => hits.Add(id));
            Assert.IsTrue(state.Advance(1f));
            CollectionAssert.AreEqual(new[] { 3 }, hits);
            Assert.That(state.Position.x, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.AreEqual(1, state.HitCount);
        }

        [Test]
        public void PierceOrdersByContactThenIdAndCountsFirstTargetInLimit()
        {
            var world = new World();
            world.Add(9, 4f); world.Add(7, 2f); world.Add(3, 2f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.PierceEnemies, 2, (s, id) => hits.Add(id));
            Assert.IsTrue(state.Advance(1f));
            CollectionAssert.AreEqual(new[] { 3, 7 }, hits);
            Assert.AreEqual(2, state.HitCount);
            Assert.That(state.Position.x, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void PierceNeverHitsSameInstanceAgainAcrossFrames()
        {
            var world = new World(); world.Add(3, 1f); world.Add(4, 4f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.PierceEnemies, 3, (s, id) => hits.Add(id));
            Assert.IsFalse(state.Advance(0.1f));
            Assert.IsFalse(state.Advance(0.1f));
            Assert.IsFalse(state.Advance(0.3f));
            CollectionAssert.AreEqual(new[] { 3, 4 }, hits);
        }

        [Test]
        public void SweepClipsToBattleBoundsBeforeTestingTargets()
        {
            var world = new World { Bounds = new Bounds(new Vector3(2.5f, 0f), new Vector3(5f, 10f, 0f)) };
            world.Add(3, 4f); world.Add(4, 6f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.PierceEnemies, 5, (s, id) => hits.Add(id));
            Assert.IsTrue(state.Advance(1f));
            CollectionAssert.AreEqual(new[] { 3 }, hits);
            Assert.That(state.Position.x, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void SweepUsesSumOfProjectileAndEnemyRadii()
        {
            var world = new World(); world.Add(3, 2f, 0.7f);
            var hits = new List<int>();
            var model = new BulletModel(3501, "RobotBullet", BulletFlightType.Straight, false,
                BulletHitMode.FirstEnemy, 10f, 0f, 0f, 0.25f);
            var state = new BulletState(new BulletLauncher(model, Action(), Vector3.right), world,
                (s, id) => hits.Add(id));
            Assert.IsTrue(state.Advance(0.4f));
            CollectionAssert.AreEqual(new[] { 3 }, hits);
        }

        [Test]
        public void DeadTargetsAreRevalidatedBeforeEachPiercingCallback()
        {
            var world = new World(); world.Add(3, 2f); world.Add(4, 4f); world.Add(5, 6f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.PierceEnemies, 2, (s, id) =>
            {
                hits.Add(id);
                world.Alive.Remove(4);
            });
            Assert.IsTrue(state.Advance(1f));
            CollectionAssert.AreEqual(new[] { 3, 5 }, hits);
        }

        [Test]
        public void StraightDirectionStaysFixedWhenAimTargetMoves()
        {
            var world = new World(); world.Add(3, 8f, 5f);
            var state = Straight(world, BulletHitMode.FirstEnemy, 1, (s, id) => Assert.Fail("不得追踪"));
            Assert.IsFalse(state.Advance(0.2f));
            Assert.AreEqual(new Vector3(2f, 0f), state.Position);
            Assert.AreEqual(Vector3.right, state.Direction);
        }

        [Test]
        public void PauseAndInvalidBattleDoNotAdvanceOrHit()
        {
            var world = new World { Running = false }; world.Add(3, 2f);
            var state = Straight(world, BulletHitMode.FirstEnemy, 1, (s, id) => Assert.Fail("不得命中"));
            Assert.IsFalse(state.Advance(10f));
            Assert.AreEqual(Vector3.zero, state.Position);
            Assert.AreEqual(0f, state.Elapsed);
            world.Valid = false;
            Assert.IsTrue(state.Advance(10f));
            Assert.AreEqual(Vector3.zero, state.Position);
        }

        [Test]
        public void ReentrantAdvanceAndCancellationCannotProduceExtraHits()
        {
            var world = new World(); world.Add(3, 2f); world.Add(4, 4f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.PierceEnemies, 5, (s, id) =>
            {
                hits.Add(id); s.Advance(10f); s.Cancel();
            });
            Assert.IsTrue(state.Advance(1f));
            CollectionAssert.AreEqual(new[] { 3 }, hits);
        }

        [Test]
        public void BattleEndingInFirstCallbackStopsRemainingPierceHits()
        {
            var world = new World(); world.Add(3, 2f); world.Add(4, 4f);
            var hits = new List<int>();
            var state = Straight(world, BulletHitMode.PierceEnemies, 5,
                (s, id) => { hits.Add(id); world.Valid = false; });
            Assert.IsTrue(state.Advance(1f));
            CollectionAssert.AreEqual(new[] { 3 }, hits);
        }

        [Test]
        public void NonTrackingArcKeepsInitialAimAndStillSkipsDeadTarget()
        {
            var world = new World(); world.Add(3, 5f);
            var model = new BulletModel(3500, "RobotBullet", BulletFlightType.Parabolic, false,
                BulletHitMode.LockedTarget, 0f, 1f, 1f, 0f);
            var hits = 0;
            var state = new BulletState(new BulletLauncher(model, Action(new[] { 3 })), world, (s, id) => hits++);
            Assert.IsFalse(state.Advance(0.5f));
            Assert.AreEqual(new Vector3(5f, 1f), state.Position);
            world.Alive.Remove(3);
            Assert.IsTrue(state.Advance(0.5f));
            Assert.AreEqual(new Vector3(10f, 0f), state.Position);
            Assert.AreEqual(0, hits);
        }

        [Test]
        public void ManagerClearedByHitDoesNotPresentAlreadyReleasedEntry()
        {
            var world = new World(); world.Add(3, 2f);
            var manager = new BulletManager(); var released = 0;
            manager.Add(Straight(world, BulletHitMode.PierceEnemies, 5, (s, id) => manager.Clear()),
                s => Assert.Fail("已归还资源不得再次呈现"), () => released++);
            manager.Advance(1f);
            Assert.AreEqual(1, released);
            Assert.AreEqual(0, manager.Count);
        }

        [Test]
        public void ReentrantManagerAdvanceDoesNotDoubleAdvanceOtherBullets()
        {
            var world = new World(); world.Add(3, 2f);
            var manager = new BulletManager();
            manager.Add(Straight(world, BulletHitMode.FirstEnemy, 1, (s, id) => manager.Advance(1f)),
                null, () => { });
            var other = Straight(world, BulletHitMode.PierceEnemies, 5, (s, id) => { });
            manager.Add(other, null, () => { });
            manager.Advance(1f);
            Assert.That(other.Position.x, Is.EqualTo(10f).Within(0.0001f));
            manager.Dispose();
        }

        private static CombatActionSnapshot Action(int[] ids = null) =>
            new CombatActionSnapshot(1, 1, BattleSide.Player, 1502, ids ?? Array.Empty<int>(),
                CombatOutcomeTag.SingleDamage, 20, Vector3.zero, new Vector3(10f, 0f), 1);

        private static BulletState Straight(World world, BulletHitMode mode, int maxHits, Action<BulletState, int> hit) =>
            new BulletState(new BulletLauncher(new BulletModel(3502, "RobotBullet", BulletFlightType.Straight,
                false, mode, 10f, 0f, 0f, 0f), Action(), Vector3.right, maxHits), world, hit);

        private sealed class World : IProjectileWorld
        {
            internal bool Valid = true, Running = true;
            internal Bounds Bounds = new Bounds(new Vector3(10f, 0f), new Vector3(20f, 20f, 0f));
            internal readonly List<BulletTarget> Targets = new List<BulletTarget>();
            internal readonly HashSet<int> Alive = new HashSet<int>();
            internal void Add(int id, float x, float y = 0f)
            { Targets.Add(new BulletTarget(id, new Vector3(x, y), 0.5f)); Alive.Add(id); }
            public bool IsValid(long battleId) => Valid && battleId == 1;
            public bool IsRunning => Running;
            public Bounds GetBattleBounds(BattleSide side) => Bounds;
            public IReadOnlyList<BulletTarget> GetTargets(BattleSide side) => Targets;
            public bool TryGetPosition(BattleSide side, int id, out Vector3 position)
            {
                foreach (var target in Targets)
                    if (target.InstanceId == id) { position = target.Position; return Alive.Contains(id); }
                position = default; return false;
            }
            public bool TryGetDeathPosition(BattleSide side, int id, out Vector3 position)
            { position = default; return false; }
        }
    }
}
