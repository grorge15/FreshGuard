using System;
using NUnit.Framework;
using TEngine;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class GlassCurrencyRewardTests
    {
        [Test]
        public void SuccessfulGlassCreditsImmediatelyAndPresentsOnceWithItsOriginalPosition()
        {
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var count = 0;
                var origin = new Vector3(3f, 4f, 0f);
                Action<BattleCurrencyFlightEvent> listener = reward =>
                {
                    count++;
                    Assert.AreEqual(origin, reward.WorldOrigin);
                    Assert.AreEqual(4, reward.IconCount);
                    Assert.AreEqual(30, context.GetState(BattleSide.Player).EnergyCoins);
                };
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, listener);
                try
                {
                    var fact = new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.NormalGlass, 81001, origin);
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, fact);
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, fact);
                    adapter.Advance();
                    Assert.AreEqual(1, count);
                    Assert.AreEqual(30, context.GetState(BattleSide.Player).EnergyCoins);
                }
                finally { GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, listener); }
            }
        }

        [Test]
        public void ReentrantGlassWaitsForSuccessfulRetryBeforePresentingAndStillDeduplicates()
        {
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var count = 0;
                var sent = false;
                var fact = new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.ColoredGlass, 81002, Vector3.one);
                Action<BattleCurrencyFlightEvent> listener = reward => count++;
                Action<int> sendInTransaction = side =>
                {
                    if (sent) return;
                    sent = true;
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, fact);
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, fact);
                };
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, listener);
                GameEvent.AddEventListener(BattleShopEvents.Changed, sendInTransaction);
                try
                {
                    ShopTestData.Purchase(context);
                    Assert.AreEqual(0, count);
                    adapter.Advance();
                    adapter.Advance();
                    Assert.AreEqual(1, count);
                    Assert.AreEqual(44, context.GetState(BattleSide.Player).EnergyCoins);
                }
                finally
                {
                    GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, listener);
                    GameEvent.RemoveEventListener(BattleShopEvents.Changed, sendInTransaction);
                }
            }
        }

        [Test]
        public void LegacyEnemyRewardsMissingOriginsWrongBattlesAndEndedBattlesNeverPresent()
        {
            using (var context = ShopTestData.Context())
            using (var adapter = new BattleRewardAdapter(context))
            {
                var count = 0;
                Action<BattleCurrencyFlightEvent> listener = reward => count++;
                GameEvent.AddEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, listener);
                try
                {
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, new BattleRewardEvent(context.BattleId,
                        BattleSide.Player, ShopRewardSource.NormalEnemy, 81003, Vector3.one));
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, new BattleRewardEvent(context.BattleId,
                        BattleSide.Player, ShopRewardSource.NormalGlass, 81004));
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, new BattleRewardEvent(context.BattleId + 1,
                        BattleSide.Player, ShopRewardSource.NormalGlass, 81005, Vector3.one));
                    Assert.AreEqual(34, context.GetState(BattleSide.Player).EnergyCoins);
                    context.EndBattle();
                    GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, new BattleRewardEvent(context.BattleId,
                        BattleSide.Player, ShopRewardSource.NormalGlass, 81006, Vector3.one));
                    Assert.AreEqual(0, count);
                }
                finally { GameEvent.RemoveEventListener(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, listener); }
            }
        }
    }
}
