using System;
using System.Collections;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class MysteriousStorePreviewTests
    {
        private Type _type;
        private object _session;
        private MainMenuPlayerState _player;

        public static MainMenuPlayerState Player(int coins) => new MainMenuPlayerState(30,
            TimeSpan.FromMinutes(5), 30, coins, 2500, DateTimeOffset.UtcNow);

        public static MysteriousStorePreviewSession WithHeld(MainMenuPlayerState player)
        {
            var offers = MysteriousStorePreviewSession.CreateDefault(player).Offers;
            return new MysteriousStorePreviewSession(player, offers, new[]
            {
                new MysteriousStoreHeldBuffData(offers[0], 1, 0),
                new MysteriousStoreHeldBuffData(offers[1], 2, 1)
            });
        }

        [SetUp]
        public void SetUp()
        {
            // 保留反射调用以校验会话公开接口。
            _type = Type.GetType("GameLogic.MysteriousStorePreviewSession, GameLogic");
            Assert.That(_type, Is.Not.Null, "缺少独立的神秘商店演示会话。");
            _player = Player(120);
            _session = WithHeld(_player);
        }

        [Test]
        public void DefaultStoreStartsWithoutHeldBuffs()
        {
            var session = MysteriousStorePreviewSession.CreateDefault(_player);
            Assert.That(session.HeldBuffs.Count, Is.Zero);
            Assert.That(session.GetTargetLevel(0), Is.EqualTo(1));
            Assert.That(session.GetPrice(0), Is.EqualTo(40));
        }

        [Test]
        public void UsesCurrentPlayerCoinsForPurchaseWithoutCachedBalance()
        {
            Assert.That(_type.GetProperty("Balance"), Is.Null);
            Assert.That(_type.GetProperty("PlayerState").GetValue(_session), Is.SameAs(_player));
            Assert.That(_player.TrySpendCoins(100), Is.True);
            Assert.That(Buy(0), Is.EqualTo("InsufficientBalance"));
            Assert.That(_player.Coins, Is.EqualTo(20));
            _player.AddCoins(40);
            Assert.That(Buy(0), Is.EqualTo("Success"));
            Assert.That(_player.Coins, Is.Zero);
            Assert.That(Buy(1), Is.EqualTo("SoldOut"));
            Assert.That(_player.Coins, Is.Zero);
        }

        [Test]
        public void PlayerCoinsRejectNegativeAndUnaffordableSpending()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _player.TrySpendCoins(-1));
            Assert.That(_player.TrySpendCoins(121), Is.False);
            Assert.That(_player.Coins, Is.EqualTo(120));
        }

        [Test]
        public void PurchaseUpgradesExistingBuffAndSellsOutWholeStore()
        {
            Assert.That(Buy(0), Is.EqualTo("Success"));
            Assert.That(_player.Coins, Is.EqualTo(60));
            Assert.That(Get<bool>("HasPurchased"), Is.True);
            Assert.That(Level(1800), Is.EqualTo(2));
            Assert.That(((ICollection)Get<object>("HeldBuffs")).Count, Is.EqualTo(2));
            Assert.That(Buy(1), Is.EqualTo("SoldOut"));
            Assert.That(_player.Coins, Is.EqualTo(60));
            Assert.That(Level(1801), Is.EqualTo(2));
        }

        [Test]
        public void InsufficientBalancePreservesOpportunityAndHeldBuffs()
        {
            Assert.That(Buy(2), Is.EqualTo("InsufficientBalance"));
            Assert.That(_player.Coins, Is.EqualTo(120));
            Assert.That(Get<bool>("HasPurchased"), Is.False);
            Assert.That(Level(1802), Is.Zero);
            Assert.That(Buy(1), Is.EqualTo("Success"));
        }

        [Test]
        public void RemovalResetsTargetLevelAndPriceWithoutRefund()
        {
            var before = Held(1801);
            var slot = (int)before.GetType().GetProperty("Slot").GetValue(before);
            Assert.That((bool)_type.GetMethod("Remove").Invoke(_session, new object[] { 1800 }), Is.True);
            Assert.That(Level(1800), Is.Zero);
            Assert.That((int)_type.GetMethod("GetTargetLevel").Invoke(_session, new object[] { 0 }), Is.EqualTo(1));
            Assert.That((int)_type.GetMethod("GetPrice").Invoke(_session, new object[] { 0 }), Is.EqualTo(40));
            Assert.That(_player.Coins, Is.EqualTo(120));
            Assert.That((int)Held(1801).GetType().GetProperty("Slot").GetValue(Held(1801)), Is.EqualTo(slot));
            Assert.That(Buy(0), Is.EqualTo("Success"));
            Assert.That(Level(1800), Is.EqualTo(1));
            Assert.That(_player.Coins, Is.EqualTo(80));
        }

        [Test]
        public void RemovalAfterPurchaseDoesNotRestoreBuyingOpportunity()
        {
            Buy(0);
            _type.GetMethod("Remove").Invoke(_session, new object[] { 1800 });
            Assert.That(Buy(1), Is.EqualTo("SoldOut"));
            Assert.That(_player.Coins, Is.EqualTo(60));
        }

        [Test]
        public void SeparateVisitsSharePlayerCoinsButResetPreviewBuffs()
        {
            Buy(0);
            var second = _type.GetMethod("CreateDefault").Invoke(null, new object[] { _player });
            Assert.That(((MainMenuPlayerState)_type.GetProperty("PlayerState").GetValue(second)).Coins, Is.EqualTo(60));
            Assert.That((bool)_type.GetProperty("HasPurchased").GetValue(second), Is.False);
            Assert.That((int)_type.GetMethod("GetHeldLevel").Invoke(second, new object[] { 1800 }), Is.Zero);
        }

        private string Buy(int index) => _type.GetMethod("TryBuy").Invoke(_session, new object[] { index }).ToString();
        private int Level(int id) => (int)_type.GetMethod("GetHeldLevel").Invoke(_session, new object[] { id });
        private object Held(int id) => _type.GetMethod("FindHeld").Invoke(_session, new object[] { id });
        private T Get<T>(string name) => (T)_type.GetProperty(name).GetValue(_session);
    }
}
