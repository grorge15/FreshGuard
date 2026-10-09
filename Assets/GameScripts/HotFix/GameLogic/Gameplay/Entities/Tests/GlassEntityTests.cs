using System;
using GameConfig.glass;
using Luban;
using NUnit.Framework;
using TEngine;

namespace GameLogic.Tests
{
    public sealed class GlassEntityTests
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
            Assert.AreEqual(_initialUsingCount, PoolInfo().UsingMemoryCount, "玻璃必须全部归还框架池。");
        }

        [Test]
        public void NormalBallCollisionConsumesOneDurability()
        {
            var glass = CreateGlass();
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            Assert.AreEqual(3, glass.MaxDurability);
            Assert.AreEqual(2, glass.CurrentDurability);
            Assert.AreEqual(1, glass.HitDurabilityLoss);
            Assert.AreEqual(0.25f, glass.CooldownInterval);
            Assert.AreEqual(0.25f, glass.DamageCooldownRemaining);
            Assert.AreEqual(0, snapshot.InstanceId);
        }

        [Test]
        public void SixParameterFactoryKeepsDraftReflectionProtocol()
        {
            var create = typeof(BattleEntityRegistry).GetMethod("CreateGlass");
            Assert.IsNotNull(create);
            Assert.AreEqual(6, create.GetParameters().Length);
            var glass = (GlassEntity)create.Invoke(_registry, new object[] {
                1, BattleSide.Player, new BoardCoordinate(1, 1), GlassKind.Normal, 3, 0.25f });
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            Assert.AreEqual(2, glass.CurrentDurability);
        }

        [TestCase(GlassKind.Normal, 3)]
        [TestCase(GlassKind.Colored, 5)]
        public void OnlyFinalEffectiveHitBreaksGlass(GlassKind kind, int durability)
        {
            var glass = _registry.CreateGlass(1, BattleSide.Player, new BoardCoordinate(1, 1),
                kind, durability, 0.25f);
            var id = glass.InstanceId;
            for (var hit = 1; hit < durability; hit++)
            {
                GlassBreakSnapshot partial;
                Assert.AreEqual(GlassCollisionResult.Damaged,
                    _registry.ApplyGlassCollision(id, false, out partial));
                Assert.AreEqual(durability - hit, glass.CurrentDurability);
                Assert.AreEqual(0, partial.InstanceId);
                _registry.AdvanceTime(0.25f);
            }
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Broken,
                _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(id, snapshot.InstanceId);
            Assert.AreEqual(kind, snapshot.GlassKind);
            Entity removed;
            Assert.IsFalse(_registry.TryGet(id, out removed));
            GlassEntity indexed;
            Assert.IsFalse(_registry.TryGetGlass(BattleSide.Player, new BoardCoordinate(1, 1), out indexed));
        }

        [Test]
        public void AllBallsShareCooldownAndIgnoredHitsDoNotRefreshIt()
        {
            var glass = CreateGlass();
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            Assert.AreEqual(GlassCollisionResult.Ignored,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            _registry.AdvanceTime(0.125f);
            Assert.AreEqual(GlassCollisionResult.Ignored,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            Assert.AreEqual(0.125f, glass.DamageCooldownRemaining);
            Assert.AreEqual(2, glass.CurrentDurability);
            Assert.AreEqual(0, snapshot.InstanceId);
            _registry.AdvanceTime(0.125f);
            Assert.AreEqual(0f, glass.DamageCooldownRemaining);
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            Assert.AreEqual(1, glass.CurrentDurability);
        }

        [Test]
        public void BerserkBallNeitherConsumesDurabilityNorStartsOrRefreshesCooldown()
        {
            var glass = CreateGlass();
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Ignored,
                _registry.ApplyGlassCollision(glass.InstanceId, true, out snapshot));
            Assert.AreEqual(3, glass.CurrentDurability);
            Assert.AreEqual(0f, glass.DamageCooldownRemaining);
            Assert.AreEqual(0, snapshot.InstanceId);
            _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot);
            _registry.AdvanceTime(0.125f);
            Assert.AreEqual(GlassCollisionResult.Ignored,
                _registry.ApplyGlassCollision(glass.InstanceId, true, out snapshot));
            Assert.AreEqual(2, glass.CurrentDurability);
            Assert.AreEqual(0.125f, glass.DamageCooldownRemaining);
            _registry.AdvanceTime(0.125f);
            Assert.AreEqual(GlassCollisionResult.Ignored,
                _registry.ApplyGlassCollision(glass.InstanceId, true, out snapshot));
            Assert.AreEqual(0f, glass.DamageCooldownRemaining);
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
        }

        [Test]
        public void SeparateGlassesKeepIndependentDurabilityAndCooldown()
        {
            var first = CreateGlass();
            var second = _registry.CreateGlass(1, BattleSide.Player, new BoardCoordinate(2, 1),
                GlassKind.Colored, 5, 0.5f);
            GlassBreakSnapshot snapshot;
            _registry.ApplyGlassCollision(first.InstanceId, false, out snapshot);
            _registry.AdvanceTime(0.125f);
            Assert.AreEqual(5, second.CurrentDurability);
            Assert.AreEqual(0f, second.DamageCooldownRemaining);
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(second.InstanceId, false, out snapshot));
            Assert.AreEqual(2, first.CurrentDurability);
            Assert.AreEqual(0.125f, first.DamageCooldownRemaining);
            Assert.AreEqual(4, second.CurrentDurability);
            Assert.AreEqual(0.5f, second.DamageCooldownRemaining);
        }

        [Test]
        public void AdvanceTimeUpdatesGlassAndRobotAndClampsAtZero()
        {
            var glass = CreateGlass();
            var robot = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            robot.TryConsumeAttackTrigger(1f);
            GlassBreakSnapshot snapshot;
            _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot);
            _registry.AdvanceTime(0f);
            Assert.AreEqual(0.25f, glass.DamageCooldownRemaining);
            _registry.AdvanceTime(0.125f);
            Assert.AreEqual(0.125f, glass.DamageCooldownRemaining);
            Assert.AreEqual(0.875f, robot.TriggerCooldownRemaining);
            _registry.AdvanceTime(10f);
            Assert.AreEqual(0f, glass.DamageCooldownRemaining);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
        }

        [Test]
        public void ZeroIntervalAllowsConsecutiveEffectiveHits()
        {
            var glass = _registry.CreateGlass(1, BattleSide.Player, new BoardCoordinate(0, 0),
                GlassKind.Normal, 3, 0f);
            var id = glass.InstanceId;
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Damaged, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(GlassCollisionResult.Damaged, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(GlassCollisionResult.Broken, _registry.ApplyGlassCollision(id, false, out snapshot));
        }

        [Test]
        public void BothSidesAndAllEntityKindsShareOneIdSequence()
        {
            var player = CreateGlass();
            var opponent = _registry.CreateGlass(1, BattleSide.Opponent, player.Coordinate,
                GlassKind.Colored, 5, 0.25f);
            var robot = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            var enemyId = _registry.CreateEnemy(1, BattleSide.Opponent, 5, 1f);
            Assert.AreEqual(1, player.InstanceId);
            Assert.AreEqual(2, opponent.InstanceId);
            Assert.AreEqual(3, robot.InstanceId);
            Assert.AreEqual(4, enemyId);
            Assert.AreEqual(EntityKind.Glass, player.Kind);
            GlassEntity indexed;
            Assert.IsTrue(_registry.TryGetGlass(BattleSide.Player, player.Coordinate, out indexed));
            Assert.AreSame(player, indexed);
            Assert.IsTrue(_registry.TryGetGlass(BattleSide.Opponent, player.Coordinate, out indexed));
            Assert.AreSame(opponent, indexed);
        }

        [Test]
        public void DuplicateLiveCellRejectsCreationWithoutEntityOrIdLeak()
        {
            var first = CreateGlass();
            var before = PoolInfo();
            Assert.Throws<InvalidOperationException>(() => _registry.CreateGlass(2, first.Side,
                first.Coordinate, GlassKind.Colored, 5, 0f));
            Assert.AreEqual(before.AcquireMemoryCount, PoolInfo().AcquireMemoryCount);
            GlassEntity indexed;
            Assert.IsTrue(_registry.TryGetGlass(first.Side, first.Coordinate, out indexed));
            Assert.AreSame(first, indexed);
            Entity entity;
            Assert.IsFalse(_registry.TryGet(2, out entity));
            Assert.AreEqual(2, _registry.CreateGlass(2, first.Side, new BoardCoordinate(2, 1),
                GlassKind.Colored, 5, 0f).InstanceId);
        }

        [Test]
        public void BreakSnapshotsIdentityBeforePoolClearAndCannotRepeat()
        {
            var glass = _registry.CreateGlass(7, BattleSide.Opponent, new BoardCoordinate(2, 3),
                GlassKind.Colored, 1, 0f);
            var id = glass.InstanceId;
            var before = PoolInfo();
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Broken, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(before.ReleaseMemoryCount + 1, PoolInfo().ReleaseMemoryCount);
            Assert.AreEqual(id, snapshot.InstanceId);
            Assert.AreEqual(7, snapshot.ConfigId);
            Assert.AreEqual(BattleSide.Opponent, snapshot.Side);
            Assert.AreEqual(new BoardCoordinate(2, 3), snapshot.Coordinate);
            Assert.AreEqual(GlassKind.Colored, snapshot.GlassKind);
            GlassBreakSnapshot ignored;
            Assert.AreEqual(GlassCollisionResult.Ignored, _registry.ApplyGlassCollision(id, false, out ignored));
            Assert.AreEqual(0, ignored.InstanceId);
            EntityRemovalSnapshot removal;
            Assert.IsFalse(_registry.Remove(id, EntityRemovalReason.Broken, out removal));
            Assert.AreEqual(0, removal.InstanceId);
            Assert.AreEqual(before.ReleaseMemoryCount + 1, PoolInfo().ReleaseMemoryCount);
            var replacement = _registry.CreateGlass(9, BattleSide.Opponent, new BoardCoordinate(2, 3),
                GlassKind.Normal, 3, 0.25f);
            Assert.AreNotEqual(id, replacement.InstanceId);
            Assert.AreEqual(3, replacement.CurrentDurability);
            Assert.AreEqual(0f, replacement.DamageCooldownRemaining);
            Assert.AreEqual(7, snapshot.ConfigId);
            Assert.AreEqual(GlassKind.Colored, snapshot.GlassKind);
            Assert.AreEqual(GlassCollisionResult.Ignored, _registry.ApplyGlassCollision(id, false, out ignored));
            Assert.AreEqual(3, replacement.CurrentDurability);
        }

        [TestCase(EntityRemovalReason.Manual)]
        [TestCase(EntityRemovalReason.BattleEnded)]
        public void CleanupRemovesIndexWithoutBrokenResultAndAllowsReplacement(EntityRemovalReason reason)
        {
            var glass = CreateGlass();
            var id = glass.InstanceId;
            var coordinate = glass.Coordinate;
            EntityRemovalSnapshot removal;
            Assert.IsTrue(_registry.Remove(id, reason, out removal));
            Assert.AreEqual(reason, removal.RemovalReason);
            Assert.AreEqual(EntityKind.Glass, removal.Kind);
            GlassEntity indexed;
            Assert.IsFalse(_registry.TryGetGlass(BattleSide.Player, coordinate, out indexed));
            Assert.IsNull(indexed);
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Ignored, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(0, snapshot.InstanceId);
            Assert.AreEqual(2, CreateGlass().InstanceId);
        }

        [Test]
        public void DisposingClearsBothSidesAndIsIdempotent()
        {
            var player = CreateGlass();
            var id = player.InstanceId;
            var coordinate = player.Coordinate;
            _registry.CreateGlass(1, BattleSide.Opponent, coordinate, GlassKind.Normal, 3, 0.25f);
            _registry.Dispose();
            _registry.Dispose();
            GlassEntity indexed;
            Assert.IsFalse(_registry.TryGetGlass(BattleSide.Player, coordinate, out indexed));
            Assert.IsFalse(_registry.TryGetGlass(BattleSide.Opponent, coordinate, out indexed));
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Ignored, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(0, snapshot.InstanceId);
            Assert.Throws<ObjectDisposedException>(() => CreateGlass());
        }

        [TestCase(EntityRemovalReason.Broken)]
        [TestCase(EntityRemovalReason.Killed)]
        [TestCase(EntityRemovalReason.Consumed)]
        [TestCase(EntityRemovalReason.Leaked)]
        [TestCase(EntityRemovalReason.None)]
        public void LiveGlassRejectsIncompatibleRemovalReasons(EntityRemovalReason reason)
        {
            var glass = CreateGlass();
            EntityRemovalSnapshot removal;
            Assert.IsFalse(_registry.Remove(glass.InstanceId, reason, out removal));
            Assert.AreEqual(0, removal.InstanceId);
            GlassEntity indexed;
            Assert.IsTrue(_registry.TryGetGlass(glass.Side, glass.Coordinate, out indexed));
            Assert.AreSame(glass, indexed);
            Assert.AreEqual(3, glass.CurrentDurability);
        }

        [Test]
        public void MissingAndOtherEntityIdsCannotReceiveGlassCollisionsOrBrokenRemoval()
        {
            var robot = _registry.CreateRobot(1, BattleSide.Player, 1, 1);
            var enemyId = _registry.CreateEnemy(1, BattleSide.Player, 5, 1f);
            foreach (var id in new[] { 0, -1, 999, robot.InstanceId, enemyId })
            {
                GlassBreakSnapshot snapshot;
                Assert.AreEqual(GlassCollisionResult.Ignored, _registry.ApplyGlassCollision(id, false, out snapshot));
                Assert.AreEqual(0, snapshot.InstanceId);
                EntityRemovalSnapshot removal;
                Assert.IsFalse(_registry.Remove(id, EntityRemovalReason.Broken, out removal));
            }
            Entity enemy;
            Assert.IsTrue(_registry.TryGet(enemyId, out enemy));
            Assert.AreEqual(5, ((EnemyEntity)enemy).CurrentHp);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidIntervalRejectsCreationWithoutConsumingId(float interval)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateGlass(1, BattleSide.Player,
                new BoardCoordinate(1, 1), GlassKind.Normal, 3, interval));
            Assert.AreEqual(1, CreateGlass().InstanceId);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidTimeCannotChangeGlassCooldown(float time)
        {
            var glass = CreateGlass();
            GlassBreakSnapshot snapshot;
            _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot);
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.AdvanceTime(time));
            Assert.AreEqual(0.25f, glass.DamageCooldownRemaining);
            Assert.AreEqual(2, glass.CurrentDurability);
        }

        [TestCase(0, 0, 0, 3)]
        [TestCase(-1, 0, 0, 3)]
        [TestCase(1, -1, 0, 3)]
        [TestCase(1, 2, 0, 3)]
        [TestCase(1, 0, -1, 3)]
        [TestCase(1, 0, 2, 3)]
        [TestCase(1, 0, 0, 0)]
        [TestCase(1, 0, 0, -1)]
        public void InvalidIdentityOrDurabilityRejectsCreationWithoutConsumingId(
            int configId, int side, int kind, int durability)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateGlass(configId, (BattleSide)side,
                new BoardCoordinate(1, 1), (GlassKind)kind, durability, 0.25f));
            Assert.AreEqual(1, CreateGlass().InstanceId);
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        public void NegativeCellRejectsCreationWithoutConsumingId(int column, int row)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateGlass(1, BattleSide.Player,
                new BoardCoordinate(column, row), GlassKind.Normal, 3, 0.25f));
            Assert.AreEqual(1, CreateGlass().InstanceId);
        }

        [Test]
        public void NullConfigRejectsCreationWithoutConsumingId()
        {
            Assert.Throws<ArgumentNullException>(() => _registry.CreateGlassFromConfig(null,
                BattleSide.Player, new BoardCoordinate(1, 1), 0.25f));
            Assert.AreEqual(1, CreateGlass().InstanceId);
        }

        [TestCase(0, 3)]
        [TestCase(1, 5)]
        public void ConfigFactoryUsesConfiguredKindDurabilityAndHitLoss(int kind, int durability)
        {
            var config = MakeConfig(17, kind, durability, 2);
            var glass = _registry.CreateGlassFromConfig(config, BattleSide.Opponent,
                new BoardCoordinate(2, 3), 0.25f);
            Assert.AreEqual(17, glass.ConfigId);
            Assert.AreEqual(BattleSide.Opponent, glass.Side);
            Assert.AreEqual(new BoardCoordinate(2, 3), glass.Coordinate);
            Assert.AreEqual((GlassKind)kind, glass.GlassKind);
            Assert.AreEqual(durability, glass.MaxDurability);
            Assert.AreEqual(2, glass.HitDurabilityLoss);
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Damaged,
                _registry.ApplyGlassCollision(glass.InstanceId, false, out snapshot));
            Assert.AreEqual(durability - 2, glass.CurrentDurability);
            Assert.AreEqual(0.25f, glass.DamageCooldownRemaining);
        }

        [TestCase(3)]
        [TestCase(int.MaxValue)]
        public void ConfiguredDamageClampsAtZeroAndBreaksOnce(int loss)
        {
            var glass = _registry.CreateGlassFromConfig(MakeConfig(17, 1, 3, loss),
                BattleSide.Player, new BoardCoordinate(1, 1), 0f);
            var id = glass.InstanceId;
            GlassBreakSnapshot snapshot;
            Assert.AreEqual(GlassCollisionResult.Broken, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(id, snapshot.InstanceId);
            Assert.AreEqual(17, snapshot.ConfigId);
            Assert.AreEqual(GlassKind.Colored, snapshot.GlassKind);
            Assert.AreEqual(0, glass.CurrentDurability, "归还池后不保留耐久状态。");
            Assert.AreEqual(GlassCollisionResult.Ignored, _registry.ApplyGlassCollision(id, false, out snapshot));
            Assert.AreEqual(0, snapshot.InstanceId);
        }

        [TestCase(0, 0, 3, 1)]
        [TestCase(1, -1, 3, 1)]
        [TestCase(1, 2, 3, 1)]
        [TestCase(1, 0, 0, 1)]
        [TestCase(1, 0, -1, 1)]
        [TestCase(1, 0, 3, 0)]
        [TestCase(1, 0, 3, -1)]
        public void InvalidConfigRejectsCreationWithoutAcquiringOrConsumingId(
            int configId, int kind, int durability, int loss)
        {
            var config = MakeConfig(configId, kind, durability, loss);
            var before = PoolInfo();
            Assert.Throws<ArgumentOutOfRangeException>(() => _registry.CreateGlassFromConfig(config,
                BattleSide.Player, new BoardCoordinate(1, 1), 0.25f));
            Assert.AreEqual(before.AcquireMemoryCount, PoolInfo().AcquireMemoryCount);
            Assert.AreEqual(1, CreateGlass().InstanceId);
        }

        [Test]
        public void ConfigCreationSharesCellUniquenessWithSixParameterFactory()
        {
            var first = CreateGlass();
            Assert.Throws<InvalidOperationException>(() => _registry.CreateGlassFromConfig(MakeConfig(17, 1, 5, 2),
                first.Side, first.Coordinate, 0.25f));
            Assert.AreEqual(2, _registry.CreateGlassFromConfig(MakeConfig(17, 1, 5, 2),
                BattleSide.Opponent, first.Coordinate, 0.25f).InstanceId);
        }

        private static Glass MakeConfig(int id, int kind, int durability, int loss)
        {
            // 使用生成类的真实序列化协议；表现字段不属于实体状态。
            var buffer = new ByteBuf();
            buffer.WriteInt(id);
            buffer.WriteInt(kind);
            buffer.WriteInt(durability);
            buffer.WriteInt(loss);
            buffer.WriteString("GlassPrefab");
            buffer.WriteSize(1);
            buffer.WriteFloat(1f);
            buffer.WriteSize(1);
            buffer.WriteString("GlassSprite");
            return new Glass(new ByteBuf(buffer.CopyData()));
        }

        private static MemoryPoolInfo PoolInfo()
        {
            foreach (var info in MemoryPool.GetAllMemoryPoolInfos())
                if (info.Type == typeof(GlassEntity)) return info;
            return default(MemoryPoolInfo);
        }

        private GlassEntity CreateGlass()
        {
            return _registry.CreateGlass(1, BattleSide.Player, new BoardCoordinate(1, 1),
                GlassKind.Normal, 3, 0.25f);
        }
    }
}
