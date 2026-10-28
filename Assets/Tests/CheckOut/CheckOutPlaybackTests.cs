#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using Luban;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class CheckOutPlaybackTests
    {
        private GameObject _uiRoot;
        private EditorUiLoader _loader;
        private object _presenter;
        private Type _presenterType;
        private string _saveDirectory;
        private MatchPlayerStore _store;
        private readonly System.Collections.Generic.Dictionary<FieldInfo, object> _savedFields = new System.Collections.Generic.Dictionary<FieldInfo, object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsTrue(Application.isPlaying);
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            ReplaceField(typeof(ConfigSystem), ConfigSystem.Instance, "_tables", tables);
            ReplaceField(typeof(ConfigSystem), ConfigSystem.Instance, "_init", true);
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FreshGuard-checkout-" + Guid.NewGuid().ToString("N"));
            _store = new MatchPlayerStore(Path.Combine(_saveDirectory, "player.json"));
            var state = new MainMenuPlayerState(30, TimeSpan.FromMinutes(5), 30, 100, 2500, DateTimeOffset.UtcNow);
            ReplaceField(typeof(GameApp), null, "_playerState", state);
            ReplaceField(typeof(GameApp), null, "_playerStore", _store);
            ReplaceField(typeof(GameApp), null, "_playerRecord", new MatchPlayerRecord());
            _presenterType = typeof(TowerBattleSession).Assembly.GetType("GameLogic.BattleSettlementPresenter");
            Assert.IsNotNull(_presenterType, "缺少清理后打开结算页的展示入口");
            // GameModule 缓存不会随 Singleton.Release 自动清除；隔离各用例的 UI 模块。
            typeof(GameModule).GetField("_ui", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            _uiRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TEngine/Settings/Prefab/UIRoot.prefab"));
            _uiRoot.name = "UIRoot";
            var ui = GameModule.UI;
            _loader = new EditorUiLoader();
            UIModule.Resource = _loader;
            _presenter = _presenterType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_presenter != null) _presenterType.GetMethod("Release").Invoke(_presenter, null);
            _loader?.CompletePending();
            if (UIModule.IsValid) GameModule.UI.Release();
            typeof(GameModule).GetField("_ui", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            if (_uiRoot != null) Object.Destroy(_uiRoot);
            yield return null;
            foreach (var pair in _savedFields)
                pair.Key.SetValue(pair.Key.IsStatic ? null : ConfigSystem.Instance, pair.Value);
            _savedFields.Clear();
            if (Directory.Exists(_saveDirectory)) Directory.Delete(_saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator VictoryDoubleButtonGrantsTwiceOnceAndReturnPersists() => RewardButtonsAsync(true, true).ToCoroutine();

        [UnityTest]
        public IEnumerator DefeatReturnButtonGrantsBaseRewardAndOpensMainMenu() => RewardButtonsAsync(false, false).ToCoroutine();

        [UnityTest]
        public IEnumerator DefeatDoubleButtonUsesLoseAward() => RewardButtonsAsync(false, true).ToCoroutine();

        [UnityTest]
        public IEnumerator VictoryReturnButtonUsesWinAward() => RewardButtonsAsync(true, false).ToCoroutine();

        [UnityTest]
        public IEnumerator FailedSaveCanRetryWithoutGrantingAgain() => RewardButtonsAsync(true, true, true).ToCoroutine();

        private async UniTask RewardButtonsAsync(bool victory, bool doubled, bool failFirstSave = false)
        {
            var snapshot = Snapshot(201, victory ? BattleSide.Opponent : BattleSide.Player);
            var baseAmount = victory ? 110 : 35;
            var expectedCoins = 100 + baseAmount * (doubled ? 2 : 1);
            Begin(201);
            GameEvent.Send(BattleSettlementEvents.Requested, snapshot);
            for (var frame = 0; frame < 12 && GameModule.UI.GetUI<CheckOutUI>() == null; frame++) await UniTask.Yield();
            var window = GameModule.UI.GetUI<CheckOutUI>();
            Assert.IsNotNull(window);
            var doubleButton = window.transform.Find("m_rect_Content/m_btn_DoubleReward").GetComponent<Button>();
            var returnButton = window.transform.Find("m_rect_Content/m_btn_ReturnMain").GetComponent<Button>();
            var amount = window.transform.Find("m_rect_Content/m_rect_Rewards/m_item_Reward/m_text_Amount").GetComponent<Text>();
            Assert.IsTrue(doubleButton.interactable, "结算后必须启用双倍按钮");
            Assert.IsTrue(returnButton.interactable, "结算后必须启用返回按钮");
            Assert.AreEqual(baseAmount.ToString(), amount.text);
            if (doubled)
            {
                var blockedPath = Path.Combine(_saveDirectory, "player.json.tmp");
                if (failFirstSave)
                {
                    Directory.CreateDirectory(blockedPath);
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("结算奖励保存或返回失败："));
                }
                doubleButton.onClick.Invoke();
                doubleButton.onClick.Invoke();
                for (var frame = 0; frame < 300 && !returnButton.interactable; frame++) await UniTask.Yield();
                Assert.IsTrue(returnButton.interactable, "保存完成后应允许返回");
                if (failFirstSave)
                {
                    Assert.IsTrue(doubleButton.interactable, "保存失败后应允许重试");
                    Assert.AreEqual(expectedCoins, GameApp.PlayerState.Coins);
                    Assert.IsNull(await _store.LoadAsync(), "失败保存不能伪装为成功");
                    Directory.Delete(blockedPath);
                    doubleButton.onClick.Invoke();
                    for (var frame = 0; frame < 300 && !returnButton.interactable; frame++) await UniTask.Yield();
                    Assert.IsTrue(returnButton.interactable);
                }
                Assert.IsFalse(doubleButton.interactable, "本局只能领取一次双倍");
                Assert.AreEqual((baseAmount * 2).ToString(), amount.text);
                Assert.AreEqual(expectedCoins, GameApp.PlayerState.Coins);
            }
            returnButton.onClick.Invoke();
            returnButton.onClick.Invoke();
            for (var frame = 0; frame < 300 && GameModule.UI.GetUI<CheckOutUI>() != null; frame++) await UniTask.Yield();
            Assert.IsNull(GameModule.UI.GetUI<CheckOutUI>(), "返回应关闭结算页");
            Assert.IsNotNull(GameModule.UI.GetUI<MainMenuUI>(), "返回应打开主菜单");
            Assert.AreEqual(expectedCoins, GameApp.PlayerState.Coins);
            Assert.AreEqual(GameApp.PlayerState.Coins, (await _store.LoadAsync()).Coins, "奖励必须保存到玩家存档");
            GameEvent.Send(BattleSettlementEvents.Requested, snapshot);
            await UniTask.Yield();
            Assert.IsNull(GameModule.UI.GetUI<CheckOutUI>(), "返回后旧结算通知不能重新开窗");
        }

        private void ReplaceField(Type type, object target, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            _savedFields.Add(field, field.GetValue(target));
            field.SetValue(target, value);
        }

        [UnityTest]
        public IEnumerator RealTowerDeathsOpenCorrectWindowOnceAfterCleanup()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            for (var scenario = 0; scenario < 3; scenario++)
            {
                var context = new BattleShopContext(ShopRules.FromTables(tables), new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, tables.TbRobot.Get);
                var root = new GameObject("CheckOutBattle");
                var host = root.AddComponent<BattleShopSceneController>();
                var towers = new TowerBattleSession(context.BattleId, context.Registry, 5);
                SetField(host, "<Context>k__BackingField", context);
                SetField(host, "_towers", towers);
                SetField(host, "_ready", true);
                SetField(host, "_running", true);
                towers.SettlementRequested += (Action<BattleSettlementSnapshot>)Delegate.CreateDelegate(typeof(Action<BattleSettlementSnapshot>), host,
                    host.GetType().GetMethod("OnTowerSettlementRequested", BindingFlags.NonPublic | BindingFlags.Instance));
                BattleSettlementSnapshot snapshot = default;
                towers.SettlementRequested += value => snapshot = value;
                Begin(context.BattleId);
                Assert.IsNull(GameModule.UI.GetUI<CheckOutUI>(), "新对局必须先关闭旧窗口，scenario=" + scenario);
                try
                {
                    Assert.IsTrue(host.ApplyTowerDamageBatch(context.BattleId, scenario == 1 ? 0 : 5, scenario == 0 ? 0 : 5));
                    Assert.IsFalse(host.IsCombatRunning);
                    Assert.IsNull(GameModule.UI.GetUI<CheckOutUI>(), "不能在伤害通知栈中开窗");
                    if (scenario == 2) Object.DestroyImmediate(root);
                    for (var frame = 0; frame < 12 && GameModule.UI.GetUI<CheckOutUI>() == null; frame++) yield return null;
                    var window = GameModule.UI.GetUI<CheckOutUI>();
                    Assert.IsNotNull(window);
                    Assert.IsTrue(context.IsEnded);
                    if (host != null) Assert.IsNull(host.Context);
                    Assert.AreEqual(scenario != 0, window.transform.Find("m_go_Victory").gameObject.activeSelf);
                    Assert.AreEqual(scenario == 0, window.transform.Find("m_go_Defeat").gameObject.activeSelf);
                    Assert.AreEqual(scenario + 1, _loader.LoadCount);
                    GameEvent.Send(BattleSettlementEvents.Requested, snapshot);
                    yield return null;
                    Assert.AreEqual(scenario + 1, _loader.LoadCount, "同一对局不能重复开窗");
                }
                finally
                {
                    if (root != null) Object.DestroyImmediate(root);
                    context.Dispose();
                }
            }
        }

        [UnityTest]
        public IEnumerator FinalWaveClearOpensVictoryWithSurvivingTowersAfterCleanupOnlyOnce()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var context = new BattleShopContext(ShopRules.FromTables(tables), new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, tables.TbRobot.Get);
            var root = new GameObject("FinalWaveCheckOutBattle");
            var host = root.AddComponent<BattleShopSceneController>();
            var towers = new TowerBattleSession(context.BattleId, context.Registry, 5);
            SetField(host, "<Context>k__BackingField", context);
            SetField(host, "_towers", towers);
            SetField(host, "_ready", true);
            SetField(host, "_running", true);
            towers.SettlementRequested += (Action<BattleSettlementSnapshot>)Delegate.CreateDelegate(typeof(Action<BattleSettlementSnapshot>), host,
                host.GetType().GetMethod("OnTowerSettlementRequested", BindingFlags.NonPublic | BindingFlags.Instance));
            BattleSettlementSnapshot snapshot = default;
            towers.SettlementRequested += value => snapshot = value;
            Begin(context.BattleId);
            try
            {
                towers.ApplyDamageBatch(context.BattleId, 1, 2);
                Assert.IsTrue(towers.TryCompleteWaveClear(context.BattleId));
                Assert.IsNull(GameModule.UI.GetUI<CheckOutUI>(), "清理完成之前不能打开结算页");
                for (var frame = 0; frame < 12 && GameModule.UI.GetUI<CheckOutUI>() == null; frame++) yield return null;
                var window = GameModule.UI.GetUI<CheckOutUI>();
                Assert.IsNotNull(window);
                Assert.IsTrue(context.IsEnded);
                Assert.IsNull(host.Context);
                Assert.IsTrue(window.transform.Find("m_go_Victory").gameObject.activeSelf);
                var displayed = (BattleSettlementSnapshot)window.UserData;
                Assert.AreEqual(BattleSettlementReason.FinalWaveCleared, displayed.Reason);
                Assert.AreEqual(4, displayed.PlayerTowerHp);
                Assert.AreEqual(3, displayed.OpponentTowerHp);
                Assert.IsNull(displayed.DefeatedSide);
                GameEvent.Send(BattleSettlementEvents.Requested, snapshot);
                yield return null;
                Assert.AreEqual(1, _loader.LoadCount);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                context.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator LateOldWindowCannotCloseNewResultOrAcceptOldNotifications()
        {
            var oldSnapshot = Snapshot(100, BattleSide.Player);
            Begin(100);
            _loader.HoldNextLoad = true;
            GameEvent.Send(BattleSettlementEvents.Requested, oldSnapshot);
            yield return null;
            Begin(101);
            var current = Snapshot(101, BattleSide.Opponent);
            GameEvent.Send(BattleSettlementEvents.Requested, current);
            yield return null;
            GameEvent.Send(BattleSettlementEvents.Requested, oldSnapshot);
            _loader.CompletePending();
            yield return null;
            yield return null;
            var window = GameModule.UI.GetUI<CheckOutUI>();
            Assert.IsNotNull(window);
            Assert.AreEqual(101, ((BattleSettlementSnapshot)window.UserData).BattleId);
            Assert.IsTrue(window.transform.Find("m_go_Victory").gameObject.activeSelf);
            Assert.AreEqual(2, _loader.LoadCount);
        }

        private void Begin(long id) => _presenterType.GetMethod("BeginBattle").Invoke(_presenter, new object[] { id });

        private static BattleSettlementSnapshot Snapshot(long id, BattleSide side)
        {
            using (var registry = new BattleEntityRegistry())
            using (var session = new TowerBattleSession(id, registry, 5))
            {
                BattleSettlementSnapshot value = default;
                session.SettlementRequested += snapshot => value = snapshot;
                session.ApplyDamage(id, side, 5);
                return value;
            }
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private sealed class EditorUiLoader : IUIResourceLoader
        {
            internal int LoadCount;
            internal bool HoldNextLoad;
            private UniTaskCompletionSource _pending;
            public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "") =>
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/" + location + ".prefab"), parent);

            public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
                CancellationToken cancellationToken = default, string packageName = "")
            {
                LoadCount++;
                if (HoldNextLoad)
                {
                    HoldNextLoad = false;
                    _pending = new UniTaskCompletionSource();
                    await _pending.Task;
                }
                return LoadGameObject(location, parent, packageName);
            }

            internal void CompletePending() { _pending?.TrySetResult(); _pending = null; }
        }
    }
}
#endif
