using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace GameLogic.Tests
{
    public sealed class TowerEntityTests
    {
        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void DamageCommitsHealthAndPublishesDeathExactlyOnce(BattleSide side)
        {
            using (var registry = new BattleEntityRegistry())
            {
                var tower = registry.CreateTower(3001, side, 100);
                Assert.AreEqual(EntityKind.Tower, tower.Kind);
                Assert.IsTrue(tower.IsAlive);
                var healthEvents = 0;
                var deaths = 0;
                tower.HealthChanged += snapshot =>
                {
                    healthEvents++;
                    Assert.AreEqual(tower.CurrentHp, snapshot.CurrentHp);
                    Assert.AreEqual(100, snapshot.MaxHp);
                    Assert.AreEqual(side, snapshot.Side);
                };
                tower.Died += snapshot =>
                {
                    deaths++;
                    Assert.AreEqual(0, tower.CurrentHp);
                    Assert.IsFalse(tower.IsAlive);
                    Assert.AreEqual(tower.InstanceId, snapshot.InstanceId);
                    Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(tower.InstanceId, 100));
                };
                Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(tower.InstanceId, 0));
                Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(tower.InstanceId, -1));
                Assert.AreEqual(DamageResult.Damaged, registry.ApplyTowerDamage(tower.InstanceId, 30));
                Assert.AreEqual(70, tower.CurrentHp);
                Assert.AreEqual(DamageResult.Killed, registry.ApplyTowerDamage(tower.InstanceId, int.MaxValue));
                Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(tower.InstanceId, 1));
                Assert.AreEqual(2, healthEvents);
                Assert.AreEqual(1, deaths);
                Assert.AreEqual(0, tower.CurrentHp);
            }
        }

        [Test]
        public void OneTowerPerSideAndInvalidCreationNeverConsumesIdentity()
        {
            using (var registry = new BattleEntityRegistry())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateTower(3001, BattleSide.Player, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateTower(0, BattleSide.Player, 100));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateTower(3001, (BattleSide)99, 100));
                var player = registry.CreateTower(3001, BattleSide.Player, 100);
                Assert.AreEqual(1, player.InstanceId);
                Assert.Throws<InvalidOperationException>(() => registry.CreateTower(3001, BattleSide.Player, 100));
                var opponent = registry.CreateTower(3001, BattleSide.Opponent, 100);
                Assert.AreEqual(2, opponent.InstanceId);
                TowerEntity found;
                Assert.IsTrue(registry.TryGetTower(BattleSide.Player, out found));
                Assert.AreSame(player, found);
            }
        }

        [Test]
        public void RemovalAndBattleDisposalDoNotPublishDeath()
        {
            var registry = new BattleEntityRegistry();
            var player = registry.CreateTower(3001, BattleSide.Player, 100);
            var opponent = registry.CreateTower(3001, BattleSide.Opponent, 100);
            var deaths = 0;
            player.Died += _ => deaths++;
            opponent.Died += _ => deaths++;
            EntityRemovalSnapshot removal;
            Assert.IsFalse(registry.Remove(player.InstanceId, EntityRemovalReason.Killed, out removal));
            Assert.IsTrue(registry.Remove(player.InstanceId, EntityRemovalReason.Manual, out removal));
            Assert.IsTrue(player.IsRemoved);
            Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(player.InstanceId, 100));
            registry.Dispose();
            registry.Dispose();
            Assert.AreEqual(0, deaths);
            Assert.IsTrue(opponent.IsRemoved);
        }

        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void EitherTowerDeathLocksSingleSettlementAndStopsBothTowers(BattleSide side)
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(12, registry, 100))
            {
                var notifications = 0;
                session.SettlementRequested += snapshot =>
                {
                    notifications++;
                    Assert.IsTrue(session.IsEnded);
                    Assert.AreEqual(12, snapshot.BattleId);
                    Assert.AreEqual(side, snapshot.DefeatedSide);
                    Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(12,
                        side == BattleSide.Player ? BattleSide.Opponent : BattleSide.Player, 100));
                };
                Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(11, side, 100));
                Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(12, (BattleSide)99, 100));
                Assert.AreEqual(DamageResult.Killed, session.ApplyDamage(12, side, 100));
                Assert.AreEqual(1, notifications);
                var other = side == BattleSide.Player ? session.OpponentTower : session.PlayerTower;
                Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(other.InstanceId, 100));
                Assert.AreEqual(5, other.CurrentHp);
                session.Dispose();
                Assert.AreEqual(1, notifications);
            }
        }

        [Test]
        public void InvalidSessionCreatesNoHalfPairAndOldBattleCannotDamageNewSession()
        {
            using (var registry = new BattleEntityRegistry())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new TowerBattleSession(0, registry, 100));
                Assert.Throws<ArgumentOutOfRangeException>(() => new TowerBattleSession(1, registry, 0));
                using (var session = new TowerBattleSession(1, registry, 100))
                {
                    Assert.AreEqual(1, session.PlayerTower.InstanceId);
                    Assert.AreEqual(2, session.OpponentTower.InstanceId);
                }
            }
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(2, registry, 100))
            {
                Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(1, BattleSide.Player, 100));
                Assert.AreEqual(5, session.PlayerTower.CurrentHp);
            }
        }

        [Test]
        public void FaultyObserverDoesNotBlockSettlementOrHealthObservers()
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(1, registry, 100))
            {
                var notifications = 0;
                var health = 0;
                session.SettlementRequested += _ => throw new InvalidOperationException("Tower observer test");
                session.SettlementRequested += _ => notifications++;
                session.PlayerTower.HealthChanged += _ => health++;
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Tower observer test"));
                Assert.AreEqual(DamageResult.Killed, session.ApplyDamage(1, BattleSide.Player, 100));
                Assert.AreEqual(1, notifications);
                Assert.AreEqual(1, health);
            }
        }
    }
}
