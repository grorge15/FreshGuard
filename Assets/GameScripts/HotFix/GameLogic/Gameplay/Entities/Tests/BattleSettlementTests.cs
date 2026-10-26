using System;
using System.Reflection;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class BattleSettlementTests
    {
        [TestCase(5, 0, "Defeat", 0, 5)]
        [TestCase(0, 5, "Victory", 5, 0)]
        [TestCase(5, 5, "Victory", 0, 0)]
        public void BatchCommitsBothHealthValuesBeforePublishingOneResult(int playerDamage, int opponentDamage,
            string expectedResult, int playerHp, int opponentHp)
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(42, registry, 5))
            {
                var method = typeof(TowerBattleSession).GetMethod("ApplyDamageBatch");
                Assert.IsNotNull(method, "同一逻辑时刻的双方伤害需要批次入口");
                var notifications = 0;
                session.SettlementRequested += snapshot =>
                {
                    notifications++;
                    Assert.IsTrue(session.IsEnded);
                    Assert.AreEqual(playerHp, session.PlayerTower.CurrentHp);
                    Assert.AreEqual(opponentHp, session.OpponentTower.CurrentHp);
                    Assert.AreEqual(expectedResult, Read(snapshot, "Result").ToString());
                    Assert.AreEqual(BattleSettlementReason.TowerDestroyed, snapshot.Reason);
                    Assert.AreEqual(playerHp, Read(snapshot, "PlayerTowerHp"));
                    Assert.AreEqual(opponentHp, Read(snapshot, "OpponentTowerHp"));
                    Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(42, BattleSide.Player, 5));
                    Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(session.OpponentTower.InstanceId, 5));
                };
                Assert.IsTrue((bool)method.Invoke(session, new object[] { 42L, playerDamage, opponentDamage }));
                Assert.AreEqual(1, notifications);
                Assert.IsFalse((bool)method.Invoke(session, new object[] { 42L, 5, 5 }));
                Assert.AreEqual(1, notifications);
            }
        }

        [Test]
        public void NonfatalBatchRejectsObserverReentryAndStaleBattle()
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(42, registry, 5))
            {
                var method = typeof(TowerBattleSession).GetMethod("ApplyDamageBatch");
                Assert.IsNotNull(method);
                var observations = 0;
                session.PlayerTower.HealthChanged += _ =>
                {
                    observations++;
                    Assert.AreEqual(4, session.OpponentTower.CurrentHp);
                    Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(42, BattleSide.Opponent, 5));
                    Assert.IsFalse((bool)method.Invoke(session, new object[] { 42L, 5, 5 }));
                };
                Assert.IsFalse((bool)method.Invoke(session, new object[] { 41L, 5, 5 }));
                Assert.IsTrue((bool)method.Invoke(session, new object[] { 42L, 1, 1 }));
                Assert.IsFalse(session.IsEnded);
                Assert.AreEqual(4, session.OpponentTower.CurrentHp);
                Assert.AreEqual(1, observations);
            }
        }

        [Test]
        public void WaveClearPreservesRealHealthAndLocksOneVictoryBeforeNotifying()
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(42, registry, 5))
            {
                session.ApplyDamageBatch(42, 1, 2);
                var notifications = 0;
                session.SettlementRequested += snapshot =>
                {
                    notifications++;
                    Assert.AreEqual(BattleSettlementResult.Victory, snapshot.Result);
                    Assert.AreEqual(BattleSettlementReason.FinalWaveCleared, snapshot.Reason);
                    Assert.AreEqual(4, snapshot.PlayerTowerHp);
                    Assert.AreEqual(3, snapshot.OpponentTowerHp);
                    Assert.IsNull(snapshot.TowerInstanceId);
                    Assert.IsNull(snapshot.DefeatedSide);
                    Assert.IsNull(snapshot.TowerHp);
                    Assert.IsTrue(session.IsEnded);
                    Assert.IsFalse(session.TryCompleteWaveClear(42));
                    Assert.AreEqual(DamageResult.Ignored, registry.ApplyTowerDamage(session.PlayerTower.InstanceId, 5));
                };
                Assert.IsFalse(session.TryCompleteWaveClear(41));
                Assert.IsFalse(session.IsEnded);
                Assert.IsTrue(session.TryCompleteWaveClear(42));
                Assert.IsFalse(session.TryCompleteWaveClear(42));
                Assert.AreEqual(1, notifications);
            }
        }

        [TestCase(5, 0, BattleSettlementResult.Defeat)]
        [TestCase(0, 5, BattleSettlementResult.Victory)]
        [TestCase(5, 5, BattleSettlementResult.Victory)]
        public void TowerDestructionAlwaysTakesPriorityOverLaterWaveClear(int playerDamage, int opponentDamage,
            BattleSettlementResult expected)
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(42, registry, 5))
            {
                var count = 0;
                session.SettlementRequested += snapshot =>
                {
                    count++;
                    Assert.AreEqual(expected, snapshot.Result);
                    Assert.AreEqual(BattleSettlementReason.TowerDestroyed, snapshot.Reason);
                };
                session.ApplyDamageBatch(42, playerDamage, opponentDamage);
                Assert.IsFalse(session.TryCompleteWaveClear(42));
                Assert.AreEqual(1, count);
            }
        }

        [Test]
        public void WaveClearCannotRunInsideDamageNotificationsOrAfterDisposal()
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(42, registry, 5))
            {
                var healthNotifications = 0;
                session.PlayerTower.HealthChanged += _ =>
                {
                    healthNotifications++;
                    Assert.IsFalse(session.TryCompleteWaveClear(42));
                };
                session.ApplyDamage(42, BattleSide.Player, 1);
                session.ApplyDamageBatch(42, 1, 1);
                Assert.AreEqual(2, healthNotifications);
                Assert.IsFalse(session.IsEnded);
                session.Dispose();
                Assert.IsFalse(session.TryCompleteWaveClear(42));
            }
        }

        private static object Read(BattleSettlementSnapshot snapshot, string name)
        {
            var property = typeof(BattleSettlementSnapshot).GetProperty(name);
            Assert.IsNotNull(property, name);
            return property.GetValue(snapshot);
        }
    }
}
