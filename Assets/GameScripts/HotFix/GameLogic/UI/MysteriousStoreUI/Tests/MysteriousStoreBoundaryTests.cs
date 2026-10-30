using System.Linq;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class MysteriousStoreBoundaryTests
    {
        [Test]
        public void FullSlotsRejectNewBuffWithoutSpendingAndStillAllowUpgrade()
        {
            var offers = MysteriousStorePreviewSession.CreateDefault(MysteriousStorePreviewTests.Player(120)).Offers;
            var held = Enumerable.Range(0, 6).Select(i => new MysteriousStoreHeldBuffData(
                new MysteriousStoreOfferData(1800 + i, "助力" + i, "", MysteriousStoreQuality.Blue, 40, 60, 80), 1, i));
            var newBuff = new MysteriousStoreOfferData(1900, "新助力", "", MysteriousStoreQuality.Green, 40);
            var session = new MysteriousStorePreviewSession(MysteriousStorePreviewTests.Player(500), new[] { newBuff, offers[0] }, held);
            Assert.That(session.TryBuy(0), Is.EqualTo(MysteriousStorePurchaseResult.SlotsFull));
            Assert.That(session.PlayerState.Coins, Is.EqualTo(500));
            Assert.That(session.HasPurchased, Is.False);
            Assert.That(session.TryBuy(1), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(session.HeldBuffs.Count, Is.EqualTo(6));
            Assert.That(session.GetHeldLevel(1800), Is.EqualTo(2));
        }

        [Test]
        public void RemovingBuffMakesItsSlotAvailableWithoutMovingOtherBuffs()
        {
            var offers = MysteriousStorePreviewSession.CreateDefault(MysteriousStorePreviewTests.Player(120)).Offers;
            var held = Enumerable.Range(0, 6).Select(i => new MysteriousStoreHeldBuffData(
                new MysteriousStoreOfferData(1900 + i, "助力", "", MysteriousStoreQuality.Green, 40), 1, i));
            var session = new MysteriousStorePreviewSession(MysteriousStorePreviewTests.Player(200), new[] { offers[2] }, held);
            session.Remove(1902);
            Assert.That(session.FindHeld(1903).Slot, Is.EqualTo(3));
            Assert.That(session.TryBuy(0), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(session.FindHeld(1802).Slot, Is.EqualTo(2));
            Assert.That(session.FindHeld(1802).Level, Is.EqualTo(1));
            Assert.That(session.PlayerState.Coins, Is.EqualTo(40));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void PreviewAcceptsActualOfferCountAndEmptyHeldSlots(int count)
        {
            var offers = MysteriousStorePreviewSession.CreateDefault(MysteriousStorePreviewTests.Player(120)).Offers.Take(count);
            var session = new MysteriousStorePreviewSession(MysteriousStorePreviewTests.Player(120), offers);
            Assert.That(session.Offers.Count, Is.EqualTo(count));
            Assert.That(session.HeldBuffs.Count, Is.Zero);
            if (count == 0) Assert.That(session.TryBuy(0), Is.EqualTo(MysteriousStorePurchaseResult.Unavailable));
        }

        [Test]
        public void MaxLevelCannotBeBoughtButRemovalAllowsLevelOneAgain()
        {
            var offer = MysteriousStorePreviewSession.CreateDefault(MysteriousStorePreviewTests.Player(120)).Offers[0];
            var original = new MysteriousStoreHeldBuffData(offer, 3, 0);
            var session = new MysteriousStorePreviewSession(MysteriousStorePreviewTests.Player(120), new[] { offer }, new[] { original });
            Assert.That(session.TryBuy(0), Is.EqualTo(MysteriousStorePurchaseResult.Unavailable));
            Assert.That(session.HasPurchased, Is.False);
            session.Remove(offer.BuffId);
            Assert.That(session.TryBuy(0), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(session.GetHeldLevel(offer.BuffId), Is.EqualTo(1));
            Assert.That(original.Level, Is.EqualTo(3), "演示会话不能修改调用方提供的持有对象。");
        }
    }
}
