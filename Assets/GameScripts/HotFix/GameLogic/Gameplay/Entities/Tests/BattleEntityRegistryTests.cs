using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TEngine;

namespace GameLogic.Tests
{
    // Unity's custom NUnit omits parallel attributes; synchronous EditMode tests run on the editor thread.
    public class BattleEntityRegistryTests
    {
        private BattleEntityRegistry _registry;
        private int _initialUsingCount;

        [SetUp]
        public void SetUp()
        {
            _initialUsingCount = PoolInfo().UsingMemoryCount;
            _registry = new BattleEntityRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Dispose();
            Assert.AreEqual(_initialUsingCount, PoolInfo().UsingMemoryCount, "怪物必须全部归还框架池。");
        }

        [Test]
        public void SharedSequenceDistinguishesSameConfigAcrossKindsAndSides()
        {
            var first = _registry.CreateRobot(101, BattleSide.Player, 1, 1001);
            var second = _registry.CreateRobot(101, BattleSide.Opponent, 1, 1001);
            var enemyId = _registry.CreateEnemy(101, BattleSide.Player, 100, 1f);
            Assert.AreEqual(1, first.InstanceId);
            Assert.AreEqual(2, second.InstanceId);
            Assert.AreEqual(3, enemyId);
            Assert.IsTrue(first.TryUpgrade());
            Assert.AreEqual(1, second.Level);
            Assert.AreEqual(100, Enemy(enemyId).CurrentHp);
        }

        [Test]
        public void PooledReuseChangesIdentityAndCannotBeHitByOldId()
        {
            // Hold only this type's idle objects, without clearing any global pool.
            var idle = new List<EnemyEntity>();
            var unused = PoolInfo().UnusedMemoryCount;
            for (var i = 0; i < unused; i++) idle.Add(MemoryPool.Acquire<EnemyEntity>());
            try
            {
                var before = PoolInfo();
                var oldId = _registry.CreateEnemy(1200, BattleSide.Player, 100, 1.5f);
                var oldEnemy = Enemy(oldId);
                Assert.IsTrue(oldEnemy.TrySetRouteProgress(12f));
                EntityRemovalSnapshot snapshot;
                Assert.AreEqual(DamageResult.Killed, _registry.ApplyDamage(oldId, 100, out snapshot));
                var newId = _registry.CreateEnemy(1201, BattleSide.Opponent, 250, 0f);
                var newEnemy = Enemy(newId);
                Assert.AreSame(oldEnemy, newEnemy);
                Assert.AreNotEqual(oldId, newId);
                Assert.AreEqual(1201, newEnemy.ConfigId);
                Assert.AreEqual(BattleSide.Opponent, newEnemy.Side);
                Assert.AreEqual(250, newEnemy.CurrentHp);
                Assert.AreEqual(250, newEnemy.MaxHp);
                Assert.AreEqual(0f, newEnemy.RouteProgress);
                Assert.AreEqual(0f, newEnemy.BaseSpeed);
                Assert.AreEqual(EntityRemovalReason.None, newEnemy.RemovalReason);
                Assert.IsTrue(newEnemy.IsAlive);
                Entity entity;
                Assert.IsFalse(_registry.TryGet(oldId, out entity));
                EntityRemovalSnapshot ignored;
                Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(oldId, 50, out ignored));
                Assert.AreEqual(0, ignored.InstanceId);
                Assert.AreEqual(250, newEnemy.CurrentHp);
                Assert.AreEqual(oldId, snapshot.InstanceId);
                Assert.AreEqual(1200, snapshot.ConfigId);
                Assert.AreEqual(EntityRemovalReason.Killed, snapshot.RemovalReason);
                Assert.AreEqual(0, snapshot.EnemyHp);
                Assert.AreEqual(12f, snapshot.EnemyRouteProgress);
                Assert.IsNull(snapshot.RobotLevel);
                Assert.AreEqual(before.AcquireMemoryCount + 2, PoolInfo().AcquireMemoryCount);
                Assert.AreEqual(before.ReleaseMemoryCount + 1, PoolInfo().ReleaseMemoryCount);
            }
            finally { foreach (var held in idle) MemoryPool.Release(held); }
        }

        [Test]
        public void ClearResetsLiveEnemyBeforeItIsInitializedAgain()
        {
            var idle = new List<EnemyEntity>();
            var unused = PoolInfo().UnusedMemoryCount;
            for (var i = 0; i < unused; i++) idle.Add(MemoryPool.Acquire<EnemyEntity>());
            EnemyEntity acquired = null;
            try
            {
                var id = _registry.CreateEnemy(1200, BattleSide.Opponent, 100, 1.5f);
                Enemy(id).TrySetRouteProgress(12f);
                EntityRemovalSnapshot snapshot;
                Assert.IsTrue(_registry.Remove(id, EntityRemovalReason.Leaked, out snapshot));
                acquired = MemoryPool.Acquire<EnemyEntity>();
                // This is a fresh pool acquisition, not access through the released reference.
                Assert.IsFalse(acquired.IsInitialized);
                Assert.IsTrue(acquired.IsRemoved);
                Assert.IsFalse(acquired.IsAlive);
                Assert.AreEqual(0, acquired.InstanceId);
                Assert.AreEqual(0, acquired.ConfigId);
                Assert.AreEqual(default(BattleSide), acquired.Side);
                Assert.AreEqual(EntityRemovalReason.None, acquired.RemovalReason);
                Assert.AreEqual(0, acquired.CurrentHp);
                Assert.AreEqual(0, acquired.MaxHp);
                Assert.AreEqual(0f, acquired.BaseSpeed);
                Assert.AreEqual(0f, acquired.RouteProgress);
                Assert.IsFalse(acquired.TrySetRouteProgress(1f));
                Assert.AreEqual(100, snapshot.EnemyHp);
                Assert.AreEqual(12f, snapshot.EnemyRouteProgress);
            }
            finally
            {
                if (acquired != null) MemoryPool.Release(acquired);
                foreach (var held in idle) MemoryPool.Release(held);
            }
        }

        [Test]
        public void DamageAndRemovalRespectKindsAndAreIdempotent()
        {
            var robot = _registry.CreateRobot(101, BattleSide.Player, 1, 1001);
            var id = _registry.CreateEnemy(1200, BattleSide.Player, 100, 1f);
            EntityRemovalSnapshot removal;
            Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(robot.InstanceId, 10, out removal));
            Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(id, 0, out removal));
            Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(id, -1, out removal));
            Assert.IsFalse(_registry.Remove(id, EntityRemovalReason.Killed, out removal));
            Assert.IsFalse(_registry.Remove(id, EntityRemovalReason.Consumed, out removal));
            Assert.IsFalse(_registry.Remove(robot.InstanceId, EntityRemovalReason.Leaked, out removal));
            Assert.IsFalse(_registry.Remove(id, EntityRemovalReason.None, out removal));
            Assert.IsFalse(_registry.Remove(id, (EntityRemovalReason)99, out removal));
            Assert.AreEqual(DamageResult.Damaged, _registry.ApplyDamage(id, 10, out removal));
            Assert.AreEqual(0, removal.InstanceId);
            Assert.AreEqual(90, Enemy(id).CurrentHp);
            var before = PoolInfo();
            Assert.AreEqual(DamageResult.Killed, _registry.ApplyDamage(id, int.MaxValue, out removal));
            Assert.AreEqual(EntityKind.Enemy, removal.Kind);
            Assert.AreEqual(0, removal.EnemyHp);
            Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(id, 10, out removal));
            Assert.IsFalse(_registry.Remove(id, EntityRemovalReason.Leaked, out removal));
            Assert.AreEqual(before.ReleaseMemoryCount + 1, PoolInfo().ReleaseMemoryCount);
        }

        [Test]
        public void LeakSnapshotsPreserveLivingHpAndCurrentRouteProgress()
        {
            var id = _registry.CreateEnemy(1200, BattleSide.Player, 100, 1f);
            var enemy = Enemy(id);
            Assert.IsTrue(enemy.TrySetRouteProgress(5f));
            Assert.IsTrue(enemy.TrySetRouteProgress(2f));
            Assert.IsFalse(enemy.TrySetRouteProgress(-1f));
            Assert.IsFalse(enemy.TrySetRouteProgress(float.NaN));
            Assert.IsFalse(enemy.TrySetRouteProgress(float.PositiveInfinity));
            EntityRemovalSnapshot removal;
            Assert.IsTrue(_registry.Remove(id, EntityRemovalReason.Leaked, out removal));
            Assert.AreEqual(EntityRemovalReason.Leaked, removal.RemovalReason);
            Assert.AreEqual(100, removal.EnemyHp);
            Assert.AreEqual(2f, removal.EnemyRouteProgress);
        }

        [Test]
        public void InvalidCreationDoesNotAcquireFromPoolOrConsumeIdentity()
        {
            var before = PoolInfo();
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateEnemy(0, BattleSide.Player, 100, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateEnemy(1, (BattleSide)99, 100, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateEnemy(1, BattleSide.Player, 0, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateEnemy(1, BattleSide.Player, 100, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateEnemy(1, BattleSide.Player, 100, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateEnemy(1, BattleSide.Player, 100, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(0, BattleSide.Player, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(1, (BattleSide)99, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(1, BattleSide.Player, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(1, BattleSide.Player, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(1, BattleSide.Player, 1, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(1, BattleSide.Player, 1, 1, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateRobot(1, BattleSide.Player, 1, 1, 1, 0));
            Assert.AreEqual(before.AcquireMemoryCount, PoolInfo().AcquireMemoryCount);
            Assert.AreEqual(1, _registry.CreateEnemy(1, BattleSide.Player, 100, 1f));
        }

        [Test]
        public void RegistrationFailureReturnsAcquiredEnemyWithoutRemovingExistingEntry()
        {
            var robot = _registry.CreateRobot(101, BattleSide.Player, 1, 1001);
            // Inject an ID collision to exercise rollback after acquiring from the real framework pool.
            var sequence = typeof(BattleEntityRegistry).GetField("_nextInstanceId", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(sequence);
            sequence.SetValue(_registry, 1);
            var before = PoolInfo();
            Assert.Throws<ArgumentException>(() => _registry.CreateEnemy(1200, BattleSide.Player, 100, 1f));
            Assert.AreEqual(before.UsingMemoryCount, PoolInfo().UsingMemoryCount);
            Assert.AreEqual(before.AcquireMemoryCount + 1, PoolInfo().AcquireMemoryCount);
            Assert.AreEqual(before.ReleaseMemoryCount + 1, PoolInfo().ReleaseMemoryCount);
            Entity existing;
            Assert.IsTrue(_registry.TryGet(1, out existing));
            Assert.AreSame(robot, existing);
            sequence.SetValue(_registry, 2);
            Assert.AreEqual(2, _registry.CreateEnemy(1200, BattleSide.Player, 100, 1f));
        }

        [Test]
        public void DisposeReturnsAllEnemiesOnceAndClosesOnlyThatBattle()
        {
            var robot = _registry.CreateRobot(101, BattleSide.Player, 1, 1001);
            var oldId = _registry.CreateEnemy(1200, BattleSide.Player, 100, 1f);
            _registry.CreateEnemy(1200, BattleSide.Opponent, 100, 1f);
            var before = PoolInfo();
            _registry.Dispose();
            _registry.Dispose();
            Assert.AreEqual(before.ReleaseMemoryCount + 2, PoolInfo().ReleaseMemoryCount);
            Assert.AreEqual(EntityRemovalReason.BattleEnded, robot.RemovalReason);
            Entity entity;
            EntityRemovalSnapshot snapshot;
            Assert.IsFalse(_registry.TryGet(oldId, out entity));
            Assert.IsNull(entity);
            Assert.IsFalse(_registry.Remove(oldId, EntityRemovalReason.Manual, out snapshot));
            Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(oldId, 100, out snapshot));
            Assert.Throws<ObjectDisposedException>(() => _registry.CreateRobot(1, BattleSide.Player, 1, 1));
            Assert.Throws<ObjectDisposedException>(() => _registry.CreateEnemy(1, BattleSide.Player, 100, 1f));
            Assert.Throws<ObjectDisposedException>(() => _registry.AdvanceTime(0f));
            using (var nextBattle = new BattleEntityRegistry())
            {
                var nextId = nextBattle.CreateEnemy(1200, BattleSide.Player, 100, 1f);
                Assert.AreEqual(1, nextId);
                Assert.AreEqual(DamageResult.Ignored, _registry.ApplyDamage(nextId, 100, out snapshot));
                Assert.IsTrue(nextBattle.TryGet(nextId, out entity));
                Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
            }
        }

        private EnemyEntity Enemy(int id)
        {
            Entity entity;
            Assert.IsTrue(_registry.TryGet(id, out entity));
            Assert.IsInstanceOf<EnemyEntity>(entity);
            return (EnemyEntity)entity;
        }

        private static MemoryPoolInfo PoolInfo()
        {
            foreach (var info in MemoryPool.GetAllMemoryPoolInfos())
                if (info.Type == typeof(EnemyEntity)) return info;
            return default(MemoryPoolInfo);
        }
    }
}
