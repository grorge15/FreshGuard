using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using UnityEngine.UI;
using System.Reflection;

namespace GameLogic.Tests
{
    public sealed class BattleCupSettlementTests
    {
        private static Tables LoadTables() => new Tables(name => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));

        private static MainMenuPlayerState Player(int trophies) =>
            new MainMenuPlayerState(30, TimeSpan.FromMinutes(5), 30, 100, trophies, DateTimeOffset.UtcNow);

        private static MatchPlayerRecord Started(MainMenuPlayerState state, string id = "battle")
        {
            var record = new MatchPlayerRecord();
            record.BeginEntry(state, id, DateTimeOffset.UtcNow);
            record.CommitSuccess(id);
            record.StartBattle(state, id, BattleRewardRules.FromTables(LoadTables(), state.Trophies));
            return record;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CandidateSavePreservesCupAndAssistRecords(bool settled)
        {
            var player = Player(400);
            player.ReplaceHeldAssists(new[]
            {
                new HeldAssistRecord(1802, 2, 0, DateTimeOffset.UtcNow.AddDays(1).UtcTicks)
            });
            var record = Started(player);
            if (settled) record.SettleBattle(player, "battle", BattleSettlementResult.Victory, false);

            var candidate = record.Copy();
            Assert.AreEqual(JsonUtility.ToJson(record), JsonUtility.ToJson(candidate));
            Assert.AreNotSame(record.EntryRules, candidate.EntryRules);
            Assert.AreNotSame(record.HeldAssists[0], candidate.HeldAssists[0]);
            if (settled) Assert.AreNotSame(record.Settlement, candidate.Settlement);

            candidate.Capture(player.Clone());
            var restored = JsonUtility.FromJson<MatchPlayerRecord>(JsonUtility.ToJson(candidate));
            var restoredPlayer = restored.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
            restored.RecoverPending(restoredPlayer, DateTimeOffset.UtcNow);
            Assert.AreEqual(settled ? 420 : 390, restoredPlayer.Trophies);
            Assert.AreEqual(1802, restoredPlayer.HeldAssists[0].BuffId);
            Assert.AreEqual(2, restoredPlayer.HeldAssists[0].Level);

            candidate.EntryRules.WinCup = 99;
            Assert.AreEqual(20, record.EntryRules.WinCup);
            if (settled)
            {
                candidate.Settlement.ClaimedMultiplier = 2;
                Assert.AreEqual(0, record.Settlement.ClaimedMultiplier);
            }
        }

        [TestCase(0, false, 0, 28)]
        [TestCase(0, true, 28, 0)]
        [TestCase(3, false, 0, 31)]
        [TestCase(399, true, 427, 394)]
        [TestCase(400, false, 390, 420)]
        [TestCase(400, true, 420, 390)]
        [TestCase(2500, true, 2520, 2490)]
        [TestCase(99999, true, 100019, 99989)]
        [TestCase(100000, true, 100020, 99990)]
        [TestCase(int.MaxValue, true, int.MaxValue, int.MaxValue - 10)]
        [TestCase(int.MaxValue, false, int.MaxValue - 10, int.MaxValue)]
        public void ActualDeltaUsesEntryBandAndClampsAtZero(int before, bool win, int after, int opponentAfter)
        {
            var player = Player(before);
            var record = Started(player);
            var data = record.SettleBattle(player, "battle", win ? BattleSettlementResult.Victory : BattleSettlementResult.Defeat, false);
            Assert.AreEqual(after, player.Trophies);
            Assert.AreEqual(after - before, data.TrophyDelta);
            Assert.IsTrue(data.HasOpponentTrophies);
            Assert.AreEqual(before, data.OpponentTrophiesBefore);
            Assert.AreEqual(opponentAfter, data.OpponentTrophiesAfter);
            Assert.AreEqual(opponentAfter - before, data.OpponentTrophyDelta);
            Assert.AreEqual(100, player.Coins, "杯数自动结算不依赖金币领取");
        }

        [Test]
        public void CrossingBandOnlyAffectsNextBattleAndDoubleOnlyAffectsCoins()
        {
            var player = Player(399);
            var record = Started(player);
            var first = record.SettleBattle(player, "battle", BattleSettlementResult.Victory, false);
            Assert.AreSame(first, record.SettleBattle(player, "battle", BattleSettlementResult.Defeat, true));
            record.ClaimSettlement(player, "battle", 2);
            record.ClaimSettlement(player, "battle", 2);
            record.ClaimSettlement(player, "battle", 1);
            Assert.AreEqual(427, player.Trophies);
            Assert.AreEqual(320, player.Coins);
            Assert.AreEqual(394, first.OpponentTrophiesAfter, "重复结算和双倍奖励不改变敌方杯数");
            record.BeginEntry(player, "next", DateTimeOffset.UtcNow);
            record.CommitSuccess("next");
            record.StartBattle(player, "next", BattleRewardRules.FromTables(LoadTables(), player.Trophies));
            Assert.Throws<InvalidOperationException>(() => record.SettleBattle(player, "battle", BattleSettlementResult.Victory, false));
            record.SettleBattle(player, "next", BattleSettlementResult.Defeat, false);
            Assert.AreEqual(417, player.Trophies);
        }

        [Test]
        public void RestartOfUnfinishedBattleDeductsOnceAndNeverGrantsCoins()
        {
            var player = Player(400);
            var saved = JsonUtility.ToJson(Started(player));
            var record = JsonUtility.FromJson<MatchPlayerRecord>(saved);
            player = record.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            Assert.AreEqual(390, player.Trophies);
            Assert.AreEqual(25, player.Energy);
            Assert.AreEqual(100, player.Coins);
            Assert.IsTrue(record.Settlement.Abandoned);
            var again = JsonUtility.FromJson<MatchPlayerRecord>(JsonUtility.ToJson(record));
            player = again.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
            again.RecoverPending(player, DateTimeOffset.UtcNow);
            again.ClaimSettlement(player, "battle", 2);
            Assert.AreEqual(390, player.Trophies);
            Assert.AreEqual(100, player.Coins);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestartKeepsKnownVictoryAndRecoversUnclaimedBaseCoinsOnce(bool alreadyDoubled)
        {
            var player = Player(399);
            var record = Started(player);
            record.SettleBattle(player, "battle", BattleSettlementResult.Victory, false);
            if (alreadyDoubled) record.ClaimSettlement(player, "battle", 2);
            record = JsonUtility.FromJson<MatchPlayerRecord>(JsonUtility.ToJson(record));
            player = record.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            Assert.AreEqual(427, player.Trophies);
            Assert.AreEqual(BattleSettlementResult.Victory, record.Settlement.Result);
            Assert.AreEqual(394, record.Settlement.OpponentTrophiesAfter);
            Assert.AreEqual(-5, record.Settlement.OpponentTrophyDelta);
            Assert.AreEqual(alreadyDoubled ? 320 : 210, player.Coins);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OldSettledSaveFillsOpponentWithoutRepeatingPlayerSettlement(bool win)
        {
            var player = Player(400);
            var record = Started(player);
            var result = win ? BattleSettlementResult.Victory : BattleSettlementResult.Defeat;
            record.SettleBattle(player, "battle", result, false);
            record.ClaimSettlement(player, "battle", 1);
            var saved = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(record),
                "\"(?:HasOpponentTrophies|OpponentTrophiesBefore|OpponentTrophiesAfter)\":(?:true|false|[0-9]+),", string.Empty);
            record = JsonUtility.FromJson<MatchPlayerRecord>(saved);
            Assert.IsFalse(record.Settlement.HasOpponentTrophies);
            player = record.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            var data = record.SettleBattle(player, "battle", result, false);
            Assert.AreEqual(win ? 420 : 390, player.Trophies);
            Assert.AreEqual(win ? 210 : 135, player.Coins);
            Assert.IsTrue(data.HasOpponentTrophies);
            Assert.AreEqual(400, data.OpponentTrophiesBefore);
            Assert.AreEqual(win ? 390 : 420, data.OpponentTrophiesAfter);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancelOrRestartWhileMatchingOrLoadingRefundsEnergyWithoutCupPenalty(bool loading)
        {
            var player = Player(2500);
            var record = new MatchPlayerRecord();
            record.BeginEntry(player, "entry", DateTimeOffset.UtcNow);
            if (loading) record.CommitSuccess("entry");
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            record.RecoverPending(player, DateTimeOffset.UtcNow);
            Assert.AreEqual(2500, player.Trophies);
            Assert.AreEqual(30, player.Energy);
            Assert.AreEqual(100, player.Coins);
        }

        [Test]
        public void FailedReleaseCanRefundAndOldSavesKeepTheirTrophies()
        {
            var player = Player(2500);
            var record = Started(player);
            record.CancelUnreleasedBattle("battle");
            Assert.IsTrue(record.RefundFailure(player, "battle", DateTimeOffset.UtcNow));
            Assert.AreEqual(2500, record.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow).Trophies);
            Assert.AreEqual(0, MainMenuPlayerState.DefaultTrophies);
        }

        [Test]
        public void BattleInformationShowsEntryTrophiesInsteadOfPrefabPreviewValues()
        {
            var player = Player(3);
            player.PlayerName = "杯数测试";
            var profile = AssetDatabase.LoadAssetAtPath<DemoBattleProfile>("Assets/AssetRaw/Configs/DemoBattleProfile.asset");
            var entry = new MatchEntrySnapshot(profile, player, ShopRules.FromTables(LoadTables()));
            player.ChangeTrophies(-3);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/UIPrefabs/BattleInfo/BattleInfoUI.prefab");
            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var widget = new BattleInfoUI();
                typeof(UIWidget).GetProperty("gameObject").SetValue(widget, root);
                typeof(UIWidget).GetProperty("transform").SetValue(widget, root.transform);
                typeof(UIWidget).GetProperty("rectTransform").SetValue(widget, root.GetComponent<RectTransform>());
                typeof(BattleInfoUI).GetMethod("ScriptGenerator", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(widget, null);
                widget.BindMatch(entry);
                Assert.AreEqual("3", root.transform.Find("m_rect_BattleInfo/m_rect_Player/m_img_InfoBackground/m_text_Trophies").GetComponent<Text>().text);
                Assert.AreEqual("3", root.transform.Find("m_rect_BattleInfo/m_rect_Enemy/m_img_InfoBackground/m_text_Trophies").GetComponent<Text>().text);
                Assert.AreEqual("杯数测试", root.transform.Find("m_rect_BattleInfo/m_rect_Player/m_img_InfoBackground/m_text_Name").GetComponent<Text>().text);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator DiskSaveFailureThenRestartRecoversExactlyOnce() => SaveFailureAsync().ToCoroutine();

        private async UniTask SaveFailureAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FreshGuard-cups-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "player.json");
            try
            {
                var store = new MatchPlayerStore(path);
                var player = Player(400);
                var record = Started(player);
                await store.SaveAsync(record);
                record.SettleBattle(player, "battle", BattleSettlementResult.Defeat, true);
                Directory.CreateDirectory(path + ".tmp");
                var failed = false;
                try { await store.SaveAsync(record); }
                catch (IOException) { failed = true; }
                catch (UnauthorizedAccessException) { failed = true; }
                Assert.IsTrue(failed);
                Directory.Delete(path + ".tmp");
                record = await store.LoadAsync();
                player = record.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
                Assert.AreEqual(400, player.Trophies, "失败写入保留上次原子提交");
                record.RecoverPending(player, DateTimeOffset.UtcNow);
                await store.SaveAsync(record);
                record = await store.LoadAsync();
                player = record.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
                record.RecoverPending(player, DateTimeOffset.UtcNow);
                Assert.AreEqual(390, player.Trophies);
                Assert.AreEqual(100, player.Coins);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
