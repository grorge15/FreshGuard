using System;
using System.Reflection;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class BattleSettlementTests
    {
        [Test]
        public void SceneWaitsForBothFieldsToClearThenRequestsVictoryAndCleansUpOnce()
        {
            using (var fixture = new EnemyBattleControllerTests.Fixture())
            {
                fixture.SetRoutes(new[] { new UnityEngine.Vector3(-1000f, 0f, 0f) });
                fixture.Prepare(); fixture.Start(); fixture.Advance(5f);
                var notifications = 0;
                var towers = fixture.Host.Towers;
                towers.SettlementRequested += snapshot =>
                {
                    notifications++;
                    Assert.AreEqual(BattleSettlementResult.Victory, snapshot.Result);
                    Assert.AreEqual(BattleEndReason.WavesCleared, snapshot.Reason);
                };
                foreach (var view in fixture.Views)
                {
                    if (fixture.Side(view) != BattleSide.Player) continue;
                    fixture.Context.Registry.ApplyDamage(view.InstanceId, 10000, out var removal);
                    ((EnemyBattleController)fixture.Controller).NotifyKilled(removal);
                }
                fixture.Host.AdvanceBattle(0f);
                Assert.AreEqual(0, notifications);
                Assert.IsFalse(towers.IsEnded);
                foreach (var view in fixture.Views)
                {
                    fixture.Context.Registry.ApplyDamage(view.InstanceId, 10000, out var removal);
                    ((EnemyBattleController)fixture.Controller).NotifyKilled(removal);
                }
                fixture.Host.AdvanceBattle(0f);
                Assert.AreEqual(1, notifications);
                Assert.IsTrue(towers.IsEnded);
                fixture.Host.AdvanceBattle(0f);
                Assert.IsTrue(fixture.Context.IsEnded);
                Assert.IsNull(fixture.Host.Context);
                Assert.AreEqual(1, notifications);
            }
        }

        [Test]
        public void ClearingAllWavesWinsOnceWithoutPretendingATowerDied()
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(42, registry, 5))
            {
                var complete = typeof(TowerBattleSession).GetMethod("CompleteWaves");
                Assert.IsNotNull(complete, "清场需要独立终局入口，不能通过伪造扣血触发");
                var notifications = 0;
                session.SettlementRequested += snapshot =>
                {
                    notifications++;
                    Assert.AreEqual(BattleSettlementResult.Victory, snapshot.Result);
                    Assert.AreEqual(5, snapshot.PlayerTowerHp);
                    Assert.AreEqual(5, snapshot.OpponentTowerHp);
                    Assert.AreEqual(0, snapshot.TowerInstanceId);
                };
                Assert.IsFalse((bool)complete.Invoke(session, new object[] { 41L }));
                Assert.IsTrue((bool)complete.Invoke(session, new object[] { 42L }));
                Assert.IsFalse((bool)complete.Invoke(session, new object[] { 42L }));
                Assert.AreEqual(1, notifications);
                Assert.AreEqual(DamageResult.Ignored, session.ApplyDamage(42, BattleSide.Player, 5));
            }
        }

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

        private static object Read(BattleSettlementSnapshot snapshot, string name)
        {
            var property = typeof(BattleSettlementSnapshot).GetProperty(name);
            Assert.IsNotNull(property, name);
            return property.GetValue(snapshot);
        }
    }
}
