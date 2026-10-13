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
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class CheckOutPlaybackTests
    {
        private GameObject _uiRoot;
        private EditorUiLoader _loader;
        private object _presenter;
        private Type _presenterType;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsTrue(Application.isPlaying);
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
        }

        [UnityTest]
        public IEnumerator RealTowerDeathsOpenCorrectWindowOnceAfterCleanup()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            for (var scenario = 0; scenario < 3; scenario++)
            {
                var context = new BattleShopContext(ShopRules.FromTables(tables), new[] { 1001, 1002, 1003 }, new[] { 1001, 1002, 1003 }, tables.TbRobot.Get);
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
