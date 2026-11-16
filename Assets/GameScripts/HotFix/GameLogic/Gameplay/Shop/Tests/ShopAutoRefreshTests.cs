using System.Reflection;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class ShopAutoRefreshTests
    {
        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property).SetValue(target, value);

        private static BattleShopUI Shop(BattleShopContext context)
        {
            var shop = new BattleShopUI();
            typeof(BattleShopUI).GetProperty("Context").SetValue(shop, context);
            return shop;
        }

        private static bool Advance(BattleShopUI shop, float seconds, bool canInteract = true)
        {
            var method = typeof(BattleShopUI).GetMethod("AdvanceAutoRefresh", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "商店需要在帧更新中推进空店自动刷新计时。");
            return (bool)method.Invoke(shop, new object[] { seconds, canInteract });
        }

        private static void Empty(BattleShopState state)
        {
            foreach (var slot in state.Slots) { Set(slot, "Offer", null); Set(slot, "InstanceId", null); }
        }

        [Test]
        public void EmptyFixedSlotsWaitFiveSecondsThenRefreshChargesOnce()
        {
            using (var context = ShopTestData.Context())
            {
                var shop = Shop(context);
                var state = context.GetState(BattleSide.Player);
                Empty(state);
                Assert.AreEqual(3, state.Slots.Count);
                Assert.IsFalse(Advance(shop, 4.9f));
                Assert.IsFalse(Advance(shop, 0f), "暂停帧不能推进倒计时。");
                Assert.IsTrue(Advance(shop, 0.11f));
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                Assert.AreEqual(20 - context.RefreshPrice, state.EnergyCoins);
                foreach (var slot in state.Slots) Assert.IsNotNull(slot.Offer);
                Assert.IsFalse(Advance(shop, 6f), "刷新补货后不能重复触发。");
            }
        }

        [Test]
        public void UnpurchasedAndPurchasedObjectsBothPreventCountdown()
        {
            using (var context = ShopTestData.Context())
            {
                var shop = Shop(context);
                Assert.IsFalse(Advance(shop, 6f));
                var robot = ShopTestData.Purchase(context);
                var state = context.GetState(BattleSide.Player);
                foreach (var slot in state.Slots) Set(slot, "Offer", null);
                Assert.IsFalse(Advance(shop, 6f), "已购机器人仍占据商店，不能自动清除。");
                Assert.AreEqual(robot.InstanceId, state.FindSlot(0).InstanceId);
            }
        }

        [Test]
        public void LosingFundsOrInteractionResetsTheFullDelay()
        {
            using (var context = ShopTestData.Context())
            {
                var shop = Shop(context);
                var state = context.GetState(BattleSide.Player);
                Empty(state);
                Assert.IsFalse(Advance(shop, 4f));
                Set(state, "EnergyCoins", context.RefreshPrice - 1);
                Assert.IsFalse(Advance(shop, 10f));
                Set(state, "EnergyCoins", context.RefreshPrice);
                Assert.IsFalse(Advance(shop, 4f));
                Assert.IsFalse(Advance(shop, 10f, false), "拖拽或隐藏等不可操作期间重新计时。");
                Assert.IsFalse(Advance(shop, 4f));
                Assert.IsTrue(Advance(shop, 1f));
            }
        }

        [Test]
        public void ActiveGestureBlocksAutoRefreshEvenIfTheCallerAllowsInteraction()
        {
            using (var context = ShopTestData.Context())
            {
                var shop = Shop(context);
                var state = context.GetState(BattleSide.Player);
                var offer = state.FindSlot(0).Offer;
                RobotDragSession session;
                Assert.AreEqual(ShopOperationResult.Success, context.TryReserveDrag(BattleSide.Player,
                    RobotDragSource.Offer(0, offer.OfferId), out session));
                Empty(state);
                Assert.IsFalse(Advance(shop, 6f));
                context.CancelInteraction();
                Assert.IsFalse(Advance(shop, 4f));
                Assert.IsTrue(Advance(shop, 1f));
            }
        }

        [Test]
        public void ReturningAnObjectAndEndingBattleCancelCountdown()
        {
            using (var context = ShopTestData.Context())
            {
                var shop = Shop(context);
                var state = context.GetState(BattleSide.Player);
                var offer = state.FindSlot(0).Offer;
                Empty(state);
                Assert.IsFalse(Advance(shop, 4f));
                Set(state.FindSlot(0), "Offer", offer);
                Assert.IsFalse(Advance(shop, 0f));
                Empty(state);
                Assert.IsFalse(Advance(shop, 4f));
                context.EndBattle();
                Assert.IsFalse(Advance(shop, 10f));
            }
        }
    }
}
