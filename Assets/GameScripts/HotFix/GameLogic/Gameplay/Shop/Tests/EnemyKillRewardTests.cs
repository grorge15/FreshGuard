using System;
using System.IO;
using GameConfig;
using Luban;
using NUnit.Framework;
using TEngine;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyKillRewardTests
    {
        private static Tables LoadTables() => new Tables(name => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));

        [TestCase(2001)]
        [TestCase(2002)]
        [TestCase(2003)]
        public void ConfirmedKillPresentsOnceAtCapturedPositionAfterConfiguredRewardIsCredited(int configId)
        {
            var config = LoadTables().TbEnemy.Get(configId);
            var origin = new Vector3(2f, -3f, 0f);
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context, (side, id) => origin))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                var count = 0;
                Action<BattleCurrencyFlightEvent> onFlight = flight =>
                {
                    count++;
                    Assert.AreEqual(context.BattleId, flight.BattleId);
                    Assert.AreEqual(BattleSide.Player, flight.Recipient);
                    Assert.AreEqual(origin, flight.WorldOrigin);
                    Assert.AreEqual(config.EnemyKillSP, flight.IconCount);
                    Assert.AreEqual(20 + config.EnemyKillSP, context.GetState(BattleSide.Player).EnergyCoins);
                };
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                try
                {
                    Resolve(context, id, BattleSide.Player, 1);
                    Assert.AreEqual(0, count, "Nonlethal damage must not emit coins.");
                    Resolve(context, id, BattleSide.Player);
                    Resolve(context, id, BattleSide.Player);
                    adapter.Advance();
                    Assert.AreEqual(1, count);
                }
                finally { GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight); }
            }
        }

        [Test]
        public void DeferredFlightKeepsDeathPositionAfterEnemyPoolReuseAndWaitsForSuccessfulCredit()
        {
            var config = LoadTables().TbEnemy.Get(2001);
            var origin = new Vector3(4f, -2f, 0f);
            var expected = origin;
            var reads = 0;
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context, (side, id) => { reads++; return origin; }))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                var killed = false;
                var count = 0;
                Action<int> onChanged = _ =>
                {
                    if (killed) return;
                    killed = true;
                    Resolve(context, id, BattleSide.Player);
                    context.Registry.CreateEnemyFromConfig(config, BattleSide.Opponent);
                    origin = new Vector3(100f, 200f, 0f);
                };
                Action<BattleCurrencyFlightEvent> onFlight = flight =>
                {
                    count++;
                    Assert.AreEqual(expected, flight.WorldOrigin);
                    Assert.AreEqual(1, flight.IconCount);
                    Assert.AreEqual(16, context.GetState(BattleSide.Player).EnergyCoins);
                };
                GameEvent.AddEventListener(BattleShopEvents.Changed, onChanged);
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                try
                {
                    context.TryRefresh(BattleSide.Player);
                    Assert.AreEqual(0, count);
                    adapter.Advance(); adapter.Advance();
                    Assert.AreEqual(1, count);
                    Assert.AreEqual(1, reads, "Retry must use the captured position.");
                }
                finally
                {
                    GameEvent.RemoveEventListener(BattleShopEvents.Changed, onChanged);
                    GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingKillFlightIsDiscardedOnDisposeOrBattleEnd(bool endBattle)
        {
            var config = LoadTables().TbEnemy.Get(2001);
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context, (side, id) => Vector3.one))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                var count = 0;
                Action<int> onChanged = _ => Resolve(context, id, BattleSide.Player);
                Action<BattleCurrencyFlightEvent> onFlight = _ => count++;
                GameEvent.AddEventListener(BattleShopEvents.Changed, onChanged);
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                try
                {
                    try { context.TryRefresh(BattleSide.Player); }
                    finally { GameEvent.RemoveEventListener(BattleShopEvents.Changed, onChanged); }
                    if (endBattle) context.EndBattle();
                    else adapter.Dispose();
                    adapter.Advance();
                    Assert.AreEqual(0, count);
                }
                finally
                {
                    GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                }
            }
        }

        [TestCase(2001, 1)]
        [TestCase(2002, 5)]
        [TestCase(2003, 20)]
        public void ConfiguredKillAwardsOnlyTheBattlefieldSideOnce(int configId, int coins)
        {
            var tables = LoadTables();
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var id = context.Registry.CreateEnemyFromConfig(tables.TbEnemy.Get(configId), BattleSide.Player);
                var before = context.GetState(BattleSide.Player).EnergyCoins;
                var notified = 0;
                Action<int> onChanged = side => { if (side == (int)BattleSide.Player) notified++; };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, onChanged);
                try
                {
                    Resolve(context, id, BattleSide.Player, 1);
                    Assert.AreEqual(before, context.GetState(BattleSide.Player).EnergyCoins);
                    Resolve(context, id, BattleSide.Player);
                    Assert.AreEqual(before + coins, context.GetState(BattleSide.Player).EnergyCoins);
                    Assert.AreEqual(20, context.GetState(BattleSide.Opponent).EnergyCoins);
                    Resolve(context, id, BattleSide.Player);
                    adapter.Advance();
                    Assert.AreEqual(before + coins, context.GetState(BattleSide.Player).EnergyCoins);
                    Assert.AreEqual(1, notified);
                }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, onChanged); }
            }
        }

        [Test]
        public void OpponentKillAndLeakDoNotCreditPlayer()
        {
            var config = LoadTables().TbEnemy.Get(2001);
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Opponent);
                Resolve(context, id, BattleSide.Opponent);
                var leaked = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                context.Registry.Remove(leaked, EntityRemovalReason.Leaked, out _);
                adapter.Advance();
                Assert.AreEqual(21, context.GetState(BattleSide.Opponent).EnergyCoins);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void KillDuringShopNotificationIsRetriedFromSnapshotAfterPoolReuse()
        {
            var config = LoadTables().TbEnemy.Get(2001);
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                var killed = false;
                Action<int> onChanged = _ =>
                {
                    if (killed) return;
                    killed = true;
                    Resolve(context, id, BattleSide.Player);
                    context.Registry.CreateEnemyFromConfig(config, BattleSide.Opponent);
                };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, onChanged);
                try { context.TryRefresh(BattleSide.Player); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, onChanged); }
                Assert.AreEqual(15, context.GetState(BattleSide.Player).EnergyCoins);
                adapter.Advance(); adapter.Advance();
                Assert.AreEqual(16, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(20, context.GetState(BattleSide.Opponent).EnergyCoins);
            }
        }

        [Test]
        public void DisposedAdapterDoesNotGrantOrRetainPendingKill()
        {
            var config = LoadTables().TbEnemy.Get(2001);
            using (var context = ShopTestData.Context())
            {
                var adapter = new BattleRewardAdapter(context);
                adapter.Dispose(); adapter.Dispose();
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                Resolve(context, id, BattleSide.Player);
                adapter.Advance();
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeferredKillIsDiscardedOnDisposeOrBattleEnd(bool endBattle)
        {
            var config = LoadTables().TbEnemy.Get(2001);
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                Action<int> onChanged = _ => Resolve(context, id, BattleSide.Player);
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, onChanged);
                try { context.TryRefresh(BattleSide.Player); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, onChanged); }
                if (endBattle) context.Dispose();
                else adapter.Dispose();
                adapter.Advance();
                Assert.AreEqual(15, context.GetState(BattleSide.Player).EnergyCoins);
            }
        }

        [Test]
        public void ZeroConfiguredRewardDoesNotAddCoinsOrNotifyShop()
        {
            var config = LoadTables().TbEnemy.Get(2001);
            config.GetType().GetField("EnemyKillSP").SetValue(config, 0);
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context, (side, id) => Vector3.one))
            {
                var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                var notified = false;
                var presented = false;
                Action<int> onChanged = _ => notified = true;
                Action<BattleCurrencyFlightEvent> onFlight = _ => presented = true;
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, onChanged);
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                try { Resolve(context, id, BattleSide.Player); adapter.Advance(); }
                finally
                {
                    GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, onChanged);
                    GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, onFlight);
                }
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.IsFalse(notified);
                Assert.IsFalse(presented);
            }
        }

        private static void Resolve(BattleShopContext context, int id, BattleSide side, int damage = 100000)
        {
            var combat = context.Battle.Combat;
            var action = new CombatActionSnapshot(context.BattleId, 99999, side, 0, new[] { id },
                CombatOutcomeTag.SingleDamage, damage, Vector3.zero, Vector3.zero, 1, CombatSourceKind.Other);
            combat.Resolve(combat.CaptureAttack(action));
        }
    }
}
