using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class MysteriousStoreDomainTests
    {
        private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(8));
        private sealed class FirstRandom : IShopRandom
        {
            public int NextInt(int maxExclusive) => 0;
            public double NextDouble() => 0;
        }
        private static MainMenuPlayerState Player(int coins = 1000, int energy = 10) =>
            new MainMenuPlayerState(energy, TimeSpan.FromMinutes(5), 30, coins, 2500, DateTimeOffset.UtcNow);
        private static MysteriousStoreOfferData Offer(int goods, int buff, int max = 1, bool instant = false) =>
            new MysteriousStoreOfferData(goods, buff, "商品", "icon", new[] { "一级", "二级", "三级" },
                MysteriousStoreQuality.Green, 100, max > 1, max, instant);
        private static HeldAssistRecord Held(int buff, int level = 1, int slot = 0) =>
            new HeldAssistRecord(buff, level, slot, HeldAssistRecord.NextBeijingMidnight(Noon).UtcTicks);

        [Test]
        public void ExportedGoodsProduceSixOffersAndFiveBattleBuffs()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,
                "AssetRaw/Configs/bytes", name + ".bytes"))));
            var offers = AssistConfigAdapter.FromTables(tables);
            CollectionAssert.AreEqual(new[] { 1801, 1802, 1803, 1804, 1805, 1806 }, offers.Select(offer => offer.GoodsId));
            Assert.That(offers.Single(offer => offer.GoodsId == 1806).BuffId, Is.Zero);
            Assert.That(offers.Single(offer => offer.GoodsId == 1806).IsInstantReward, Is.True);
            Assert.That(offers.Single(offer => offer.GoodsId == 1802).MaxLevel, Is.EqualTo(3));
            Assert.That(offers.Single(offer => offer.GoodsId == 1802).HasLevel, Is.True);
            CollectionAssert.AreEquivalent(new[] { 1801, 1802, 1803, 1804, 1805 },
                AssistConfigAdapter.UnlockedMatchBuffs(tables).Select(buff => buff.BuffId));
        }

        [Test]
        public void ExpiryUsesBeijingMidnightAndPreservesCoins()
        {
            var player = Player();
            player.ReplaceHeldAssists(new[] { Held(1802, 2) });
            var midnight = HeldAssistRecord.NextBeijingMidnight(Noon);
            Assert.That(midnight.UtcDateTime, Is.EqualTo(new DateTime(2026, 11, 13, 16, 0, 0, DateTimeKind.Utc)));
            Assert.That(player.PruneExpiredAssists(midnight.AddTicks(-1)), Is.False);
            Assert.That(player.PruneExpiredAssists(midnight), Is.True);
            Assert.That(player.HeldAssists, Is.Empty);
            Assert.That(player.Coins, Is.EqualTo(1000));
        }

        [Test]
        public void DrawExcludesMaxLevelAndDoesNotConfuseGoodsIdWithBuffId()
        {
            var player = Player();
            player.ReplaceHeldAssists(new[] { Held(1801) });
            var catalog = new[] { Offer(1801, 1801), Offer(1802, 1803, 3), Offer(1806, 0, instant: true) };
            var session = new MysteriousStoreSession(player, catalog, new FirstRandom(), _ => UniTask.CompletedTask, () => Noon);
            Assert.That(session.Offers.Select(x => x.GoodsId), Is.EqualTo(new[] { 1802, 1806 }));
            Assert.That(session.Offers[0].BuffId, Is.EqualTo(1803));
        }

        [UnityTest]
        public IEnumerator UpgradeKeepsSlotAndExpiryAndCommitsOnlyAfterSave() => UniTask.ToCoroutine(async () =>
        {
            var player = Player();
            player.ReplaceHeldAssists(new[] { Held(1802, 1, 4) });
            MainMenuPlayerState saved = null;
            var session = new MysteriousStoreSession(player, new[] { Offer(1802, 1802, 3) }, new FirstRandom(),
                candidate => { Assert.That(player.Coins, Is.EqualTo(1000)); saved = candidate.Clone(); return UniTask.CompletedTask; }, () => Noon);
            Assert.That(session.GetTargetLevel(0), Is.EqualTo(2));
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(player.HeldAssists[0].Level, Is.EqualTo(2));
            Assert.That(player.HeldAssists[0].Slot, Is.EqualTo(4));
            Assert.That(player.HeldAssists[0].ExpiresUtcTicks, Is.EqualTo(Held(1802).ExpiresUtcTicks));
            Assert.That(saved.HeldAssists[0].Level, Is.EqualTo(2));
            Assert.That(player.Coins, Is.EqualTo(900));
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.SoldOut));
        });

        [UnityTest]
        public IEnumerator SaveFailureDoesNotSpendGrantOrSellOut() => UniTask.ToCoroutine(async () =>
        {
            var player = Player();
            var session = new MysteriousStoreSession(player, new[] { Offer(1802, 1802, 3) }, new FirstRandom(),
                _ => UniTask.FromException(new IOException("测试保存失败")), () => Noon);
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.SaveFailed));
            Assert.That(player.Coins, Is.EqualTo(1000));
            Assert.That(player.HeldAssists, Is.Empty);
            Assert.That(session.HasPurchased, Is.False);
            Assert.That(session.IsBusy, Is.False);
        });

        [UnityTest]
        public IEnumerator PendingSaveBlocksDoubleClickAndDoesNotPublishCandidate() => UniTask.ToCoroutine(async () =>
        {
            var player = Player();
            var pending = new UniTaskCompletionSource();
            var session = new MysteriousStoreSession(player, new[] { Offer(1802, 1802, 3) }, new FirstRandom(),
                _ => pending.Task, () => Noon);
            var first = session.TryBuyAsync(0);
            Assert.That(session.IsBusy, Is.True);
            Assert.That(player.Coins, Is.EqualTo(1000));
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.Busy));
            pending.TrySetResult();
            Assert.That(await first, Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(player.Coins, Is.EqualTo(900));
        });

        [UnityTest]
        public IEnumerator StaminaIsImmediateAndDoesNotOccupySlot() => UniTask.ToCoroutine(async () =>
        {
            var player = Player(1000, 25);
            var session = new MysteriousStoreSession(player, new[] { Offer(1806, 0, instant: true) }, new FirstRandom(),
                _ => UniTask.CompletedTask, () => DateTimeOffset.UtcNow);
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(player.Energy, Is.EqualTo(30));
            Assert.That(player.HeldAssists, Is.Empty);
            Assert.That(player.Coins, Is.EqualTo(900));
        });

        [UnityTest]
        public IEnumerator FullSlotsRejectNewHeldButPermitStaminaAndInsufficientFundsPreserveVisit() => UniTask.ToCoroutine(async () =>
        {
            var player = Player(50);
            var session = new MysteriousStoreSession(player, new[] { Offer(1806, 0, instant: true) }, new FirstRandom(),
                _ => UniTask.CompletedTask, () => Noon);
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.InsufficientBalance));
            Assert.That(session.HasPurchased, Is.False);
            player.AddCoins(950);
            player.ReplaceHeldAssists(Enumerable.Range(0, 6).Select(slot => Held(1900 + slot, 1, slot)));
            var catalog = new[] { Offer(1802, 1802, 3), Offer(1806, 0, instant: true) };
            session = new MysteriousStoreSession(player, catalog, new FirstRandom(), _ => UniTask.CompletedTask, () => Noon);
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.SlotsFull));
            Assert.That(player.Coins, Is.EqualTo(1000));
            Assert.That(await session.TryBuyAsync(1), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(player.HeldAssists.Count, Is.EqualTo(6));
        });

        [UnityTest]
        public IEnumerator ExpiryAndRemovalArePersistedWithoutResettingSoldOut() => UniTask.ToCoroutine(async () =>
        {
            var player = Player();
            var now = Noon;
            player.ReplaceHeldAssists(new[] { Held(1802, 1, 3) });
            var saves = 0;
            var session = new MysteriousStoreSession(player, new[] { Offer(1802, 1802, 3) }, new FirstRandom(),
                _ => { saves++; return UniTask.CompletedTask; }, () => now);
            Assert.That(await session.TryBuyAsync(0), Is.EqualTo(MysteriousStorePurchaseResult.Success));
            Assert.That(await session.RemoveAsync(1802), Is.True);
            Assert.That(player.HeldAssists, Is.Empty);
            Assert.That(session.HasPurchased, Is.True);
            player.ReplaceHeldAssists(new[] { Held(1802) });
            now = HeldAssistRecord.NextBeijingMidnight(Noon);
            Assert.That(await session.RefreshExpiredAsync(), Is.True);
            Assert.That(saves, Is.EqualTo(3));
            Assert.That(player.HeldAssists, Is.Empty);
        });

        [Test]
        public void RecordRoundTripKeepsHeldAndOldRecordHasEmptyHeld()
        {
            var player = Player();
            player.ReplaceHeldAssists(new[] { Held(1802, 2, 4) });
            var record = new MatchPlayerRecord { EntryId = "matching-entry", EntryPhase = "Matching" };
            record.Capture(player);
            var loaded = JsonUtility.FromJson<MatchPlayerRecord>(JsonUtility.ToJson(record));
            var restored = loaded.Restore(30, TimeSpan.FromMinutes(5), Noon);
            Assert.That(restored.HeldAssists.Single().Slot, Is.EqualTo(4));
            Assert.That(restored.HeldAssists.Single().Level, Is.EqualTo(2));
            Assert.That(loaded.EntryId, Is.EqualTo("matching-entry"));
            loaded.HeldAssists = null;
            Assert.That(loaded.Restore(30, TimeSpan.FromMinutes(5), Noon).HeldAssists, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PendingCurrentSavePreservesMatchingSuccess() => UniTask.ToCoroutine(async () =>
            await VerifyPendingSavePhase(false));

        [UnityTest]
        public IEnumerator PendingCandidateSavePreservesMatchingRefund() => UniTask.ToCoroutine(async () =>
            await VerifyPendingSavePhase(true));

        private static async UniTask VerifyPendingSavePhase(bool candidateSave)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var stateField = typeof(GameApp).GetField("_playerState", flags);
            var recordField = typeof(GameApp).GetField("_playerRecord", flags);
            var storeField = typeof(GameApp).GetField("_playerStore", flags);
            var oldState = stateField.GetValue(null);
            var oldRecord = recordField.GetValue(null);
            var oldStore = storeField.GetValue(null);
            var directory = Path.Combine(Path.GetTempPath(), "FreshGuardAssistSave_" + Guid.NewGuid().ToString("N"));
            var store = new MatchPlayerStore(Path.Combine(directory, "player.json"));
            var serial = (SemaphoreSlim)typeof(MatchPlayerStore).GetField("_serial",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(store);
            var player = Player();
            var record = new MatchPlayerRecord();
            record.BeginEntry(player, "pending-entry", DateTimeOffset.UtcNow);
            stateField.SetValue(null, player);
            recordField.SetValue(null, record);
            storeField.SetValue(null, store);
            try
            {
                await serial.WaitAsync();
                var first = (candidateSave ? GameApp.SavePlayerCandidateAsync(player.Clone()) :
                    GameApp.SavePlayerStateAsync()).Preserve();
                if (candidateSave) record.RefundFailure(player, "pending-entry", DateTimeOffset.UtcNow);
                else record.CommitSuccess("pending-entry");
                var second = GameApp.SavePlayerStateAsync().Preserve();
                serial.Release();
                await first;
                await second;
                var saved = await store.LoadAsync();
                Assert.That(saved.EntryPhase, Is.EqualTo(candidateSave ? "Refunded" : "Success"));
                Assert.That(saved.EntryId, Is.EqualTo("pending-entry"));
                Assert.That(saved.RefundMatching(player, DateTimeOffset.UtcNow), Is.False,
                    "已成功或退款的记录重启后不能重复返还体力。");
            }
            finally
            {
                stateField.SetValue(null, oldState);
                recordField.SetValue(null, oldRecord);
                storeField.SetValue(null, oldStore);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
