using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class MysteriousStoreWidgetTests
    {
        private const string PrefabPath = "Assets/AssetRaw/Actor/Prefabs/UIPrefabs/MysteriousStoreWidget.prefab";
        private GameObject _root;
        private MysteriousStoreWidget _widget;
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            _root = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _widget = new MysteriousStoreWidget();
            Assert.That(_widget.Create(new UIBase(), _root), Is.True);
            _widget.Bind(MysteriousStorePreviewTests.WithHeld(MysteriousStorePreviewTests.Player(120)), null);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(MysteriousStoreWidget).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_widget, null);
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void PlayerCoinChangesRefreshWhileStoreRemainsOpen()
        {
            _widget.Session.PlayerState.AddCoins(55);
            typeof(MysteriousStoreWidget).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_widget, null);
            Assert.That(TextAt("m_rect_Content/m_text_Balance"), Is.EqualTo("助力币  175"));
        }

        [Test]
        public void PrefabHasNoSeparateWalletContainer()
        {
            Assert.That(_root.transform.Find("m_rect_Content/Wallet"), Is.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            for (var i = 0; i < 6; i++)
            {
                var content = prefab.transform.Find("m_rect_Content/m_item_Held" + i + "/m_go_Content");
                Assert.That(content.gameObject.activeSelf, Is.False);
                Assert.That(content.Find("m_text_Level").GetComponent<Text>().text, Is.Empty);
                foreach (Transform icon in content.Find("m_rect_Icon"))
                    Assert.That(icon.gameObject.activeSelf, Is.False);
            }
        }

        [Test]
        public void ButtonsReflectInsufficientFundsThenOnePurchaseSellsOutAllRows()
        {
            Click("m_rect_Content/m_item_Offer2/m_btn_Buy");
            Assert.That(TextAt("m_rect_Content/m_text_Feedback"), Is.EqualTo("助力币不足"));
            Assert.That(_widget.Session.PlayerState.Coins, Is.EqualTo(120));
            Click("m_rect_Content/m_item_Offer0/m_btn_Buy");
            for (var i = 0; i < 3; i++)
            {
                var button = _root.transform.Find("m_rect_Content/m_item_Offer" + i + "/m_btn_Buy");
                Assert.That(button.GetComponent<Button>().interactable, Is.False);
                Assert.That(button.Find("m_text_SoldOut").gameObject.activeSelf, Is.True);
                Assert.That(button.Find("m_img_Coin").gameObject.activeSelf, Is.False);
            }
            Assert.That(TextAt("m_rect_Content/m_text_Balance"), Is.EqualTo("助力币  60"));
            Assert.That(TextAt("m_rect_Content/m_item_Held0/m_go_Content/m_text_Level"), Is.EqualTo("2级"));
        }

        private sealed class FirstRandom : IShopRandom
        {
            public int NextInt(int maxExclusive) => 0;
            public double NextDouble() => 0;
        }

        private MysteriousStoreSession ProductionSession(Func<MainMenuPlayerState, UniTask> save,
            bool includeInstant = false)
        {
            var player = MysteriousStorePreviewTests.Player(500);
            player.ReplaceHeldAssists(new[] { new HeldAssistRecord(1802, 1, 4,
                HeldAssistRecord.NextBeijingMidnight(DateTimeOffset.UtcNow).UtcTicks) });
            var offers = new[]
            {
                new MysteriousStoreOfferData(1802, 1802, "Combo加速器", "", new[] { "一级", "二级", "三级" },
                    MysteriousStoreQuality.Green, 150, true, 3),
                includeInstant
                    ? new MysteriousStoreOfferData(1806, 0, "体力补给", "", new[] { "增加10点体力" },
                        MysteriousStoreQuality.Green, 200, false, 1, true)
                    : new MysteriousStoreOfferData(1804, 1804, "玻璃击破器", "", new[] { "扣除2点耐久" },
                        MysteriousStoreQuality.Green, 100, false, 1),
                new MysteriousStoreOfferData(1803, 1803, "Combo增伤器", "", new[] { "一级增伤", "二级增伤", "三级增伤" },
                    MysteriousStoreQuality.Green, 150, true, 3)
            };
            return new MysteriousStoreSession(player, offers, new FirstRandom(), save);
        }

        [Test]
        public void HeldLevelOneShowsNextLevelAndExistingArrowThenUpgradeSellsOut()
        {
            var session = ProductionSession(_ => UniTask.CompletedTask);
            _widget.Bind(session, null);
            Assert.That(TextAt("m_rect_Content/m_item_Offer0/m_text_Level"), Is.EqualTo("2级"));
            Assert.That(TextAt("m_rect_Content/m_item_Offer0/m_text_Description"), Is.EqualTo("二级"));
            var arrow = _root.transform.Find("m_rect_Content/m_item_Offer0/m_img_Upgrade");
            Assert.That(arrow.gameObject.activeSelf, Is.True);
            Assert.That(arrow.GetComponent<Image>().sprite, Is.SameAs(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/AssetRaw/UI/UISprites/MysteriousStore/Shop_upgradeArrow.png")));
            Assert.That(_root.transform.Find("m_rect_Content/m_item_Offer0/m_rect_Icon/m_img_Upgrade"), Is.Null,
                "复用原有同级箭头，不能另建或迁移布局。");
            Assert.That(TextAt("m_rect_Content/m_item_Held4/m_go_Content/m_text_Level"), Is.EqualTo("1级"));
            Click("m_rect_Content/m_item_Offer0/m_btn_Buy");
            Assert.That(session.PlayerState.HeldAssists.Single().Slot, Is.EqualTo(4));
            Assert.That(TextAt("m_rect_Content/m_item_Held4/m_go_Content/m_text_Level"), Is.EqualTo("2级"));
            Assert.That(TextAt("m_rect_Content/m_item_Offer0/m_text_Level"), Is.EqualTo("2级"));
            Assert.That(TextAt("m_rect_Content/m_item_Offer0/m_text_Description"), Is.EqualTo("二级"));
            Assert.That(TextAt("m_rect_Content/m_item_Offer2/m_text_Level"), Is.EqualTo("1级"));
            for (var row = 0; row < session.Offers.Count; row++)
            {
                var offer = _root.transform.Find("m_rect_Content/m_item_Offer" + row);
                Assert.That(offer.Find("m_img_Upgrade").gameObject.activeSelf, Is.False);
                Assert.That(offer.Find("m_btn_Buy").GetComponent<Button>().interactable, Is.False);
                Assert.That(offer.Find("m_btn_Buy/m_text_SoldOut").gameObject.activeSelf, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NonLevelGoodsAndStaminaHideLevelAndArrow(bool instant)
        {
            _widget.Bind(ProductionSession(_ => UniTask.CompletedTask, instant), null);
            var row = _root.transform.Find("m_rect_Content/m_item_Offer1");
            Assert.That(row.Find("m_text_Level").gameObject.activeSelf, Is.False);
            Assert.That(row.Find("m_img_Upgrade").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator PendingPurchaseLocksActionsAndSaveFailurePreservesHeldAndBalance() => UniTask.ToCoroutine(async () =>
        {
            var pending = new UniTaskCompletionSource();
            var session = ProductionSession(_ => pending.Task);
            var closed = false;
            _widget.Bind(session, () => closed = true);
            Click("m_rect_Content/m_item_Offer0/m_btn_Buy");
            Assert.That(session.IsBusy, Is.True);
            Assert.That(session.PlayerState.Coins, Is.EqualTo(500));
            Assert.That(session.GetHeldLevel(1802), Is.EqualTo(1));
            Assert.That(_root.transform.Find("m_rect_Content/m_item_Offer0/m_btn_Buy").GetComponent<Button>().interactable, Is.False);
            Assert.That(_root.transform.Find("m_rect_Content/m_item_Held4/m_go_Content/m_btn_Remove").GetComponent<Button>().interactable, Is.False);
            Click("m_rect_Content/m_btn_Close");
            Click("m_rect_Content/m_item_Held4/m_go_Content/m_btn_Remove");
            Assert.That(closed, Is.False);
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf, Is.False);
            pending.TrySetException(new IOException("测试磁盘写入失败"));
            await UniTask.Yield();
            Assert.That(session.IsBusy, Is.False);
            Assert.That(session.HasPurchased, Is.False);
            Assert.That(session.PlayerState.Coins, Is.EqualTo(500));
            Assert.That(session.GetHeldLevel(1802), Is.EqualTo(1));
            Assert.That(TextAt("m_rect_Content/m_text_Feedback"), Is.EqualTo("保存失败，请重试"));
            Assert.That(_root.transform.Find("m_rect_Content/m_item_Offer0/m_btn_Buy").GetComponent<Button>().interactable, Is.True);
            Click("m_rect_Content/m_btn_Close");
            Assert.That(closed, Is.True);
        });

        [UnityTest]
        public IEnumerator PendingRemovalKeepsConfirmationAndFailureDoesNotRemoveHeld() => UniTask.ToCoroutine(async () =>
        {
            var pending = new UniTaskCompletionSource();
            var session = ProductionSession(_ => pending.Task);
            _widget.Bind(session, null);
            Click("m_rect_Content/m_item_Held4/m_go_Content/m_btn_Remove");
            Click("m_go_RemoveConfirmation/m_rect_Content/m_btn_Remove");
            Click("m_go_RemoveConfirmation/m_btn_Outside");
            Assert.That(session.IsBusy, Is.True);
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf, Is.True);
            pending.TrySetException(new IOException("测试移除保存失败"));
            await UniTask.Yield();
            Assert.That(session.GetHeldLevel(1802), Is.EqualTo(1));
            Assert.That(session.PlayerState.Coins, Is.EqualTo(500));
            Assert.That(TextAt("m_rect_Content/m_text_Feedback"), Is.EqualTo("保存失败，请重试"));
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf, Is.False);
        });

        [Test]
        public void RemovalRequiresConfirmationAndOutsideOrCloseOnlyCancelDialog()
        {
            var closed = false;
            _widget.Bind(MysteriousStorePreviewTests.WithHeld(MysteriousStorePreviewTests.Player(120)), () => closed = true);
            const string minus = "m_rect_Content/m_item_Held1/m_go_Content/m_btn_Remove";
            Click(minus);
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf, Is.True);
            Click("m_go_RemoveConfirmation/m_btn_Outside");
            Assert.That(_widget.Session.GetHeldLevel(1801), Is.EqualTo(2));
            Assert.That(closed, Is.False);
            Click(minus);
            Click("m_go_RemoveConfirmation/m_rect_Content/m_btn_Close");
            Assert.That(_widget.Session.GetHeldLevel(1801), Is.EqualTo(2));
            Click(minus);
            Click("m_go_RemoveConfirmation/m_rect_Content/m_btn_Remove");
            Assert.That(_widget.Session.GetHeldLevel(1801), Is.Zero);
            Assert.That(_root.transform.Find("m_rect_Content/m_item_Held1/m_go_Content").gameObject.activeSelf, Is.False);
            Assert.That(TextAt("m_rect_Content/m_item_Offer1/m_btn_Buy/m_text_Price"), Is.EqualTo("40"));
            Assert.That(_widget.Session.PlayerState.Coins, Is.EqualTo(120));
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf, Is.False);
            Click("m_rect_Content/m_btn_Close");
            Assert.That(closed, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ActualOfferCountAndEmptyHeldSlotsRenderCorrectly(int count)
        {
            _widget.Bind(new MysteriousStorePreviewSession(MysteriousStorePreviewTests.Player(120),
                MysteriousStorePreviewSession.CreateDefault(MysteriousStorePreviewTests.Player(120)).Offers.Take(count)), null);
            for (var i = 0; i < 3; i++)
                Assert.That(_root.transform.Find("m_rect_Content/m_item_Offer" + i).gameObject.activeSelf, Is.EqualTo(i < count));
            for (var i = 0; i < 6; i++)
                Assert.That(_root.transform.Find("m_rect_Content/m_item_Held" + i + "/m_go_Content").gameObject.activeSelf, Is.False);
        }

        [Test]
        public void FullScreenBlockerFitsCanvasEvenWhenOwnerRectIsZeroAndAfterResize()
        {
            var canvasObject = new GameObject("RootCanvas", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject, _scene);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1080, 1920);
            var owner = new GameObject("ZeroSizedMainMenu", typeof(RectTransform)).GetComponent<RectTransform>();
            owner.SetParent(canvasRect, false);
            owner.sizeDelta = Vector2.zero;
            typeof(MysteriousStoreWidget).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_widget, null);
            _root.transform.SetParent(owner, false);
            _widget = new MysteriousStoreWidget();
            _widget.Create(new UIBase(), _root);
            var blocker = _root.transform.Find("m_img_InputBlocker").GetComponent<RectTransform>();
            Assert.That(blocker.rect.size, Is.EqualTo(new Vector2(1080, 1920)));
            canvasRect.sizeDelta = new Vector2(1080, 2400);
            typeof(MysteriousStoreWidget).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_widget, null);
            Assert.That(blocker.rect.size, Is.EqualTo(new Vector2(1080, 2400)));
            Assert.That(owner.rect.size, Is.EqualTo(Vector2.zero), "不修改宿主主菜单的布局。");
        }

        [Test]
        public void PopupBackgroundBlocksRaycastsAndAllLabelsUseProjectFont()
        {
            Assert.That(_root.transform.Find("m_img_InputBlocker").GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation/m_rect_Content/Panel").GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(_root.GetComponentsInChildren<CanvasScaler>(true).Length, Is.Zero);
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/AssetRaw/Fonts/AlibabaPuHuiTi-3-95-ExtraBold.ttf");
            foreach (var text in _root.GetComponentsInChildren<Text>(true))
            {
                Assert.That(text.font, Is.SameAs(font));
                Assert.That(text.raycastTarget, Is.False);
            }
        }

        private void Click(string path) => _root.transform.Find(path).GetComponent<Button>().onClick.Invoke();
        private string TextAt(string path) => _root.transform.Find(path).GetComponent<Text>().text;
    }

    public sealed class MysteriousStoreMainMenuTests
    {
        // 实例化真实 Prefab，只替换框架现有 IUIResourceLoader 边界，避免启动全局游戏。
        private sealed class PrefabLoader : IUIResourceLoader
        {
            public int Calls;
            public bool Fail;
            public bool Pending;
            public CancellationToken ReceivedToken;
            public readonly UniTaskCompletionSource<GameObject> Completion = new UniTaskCompletionSource<GameObject>();
            public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "") =>
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/UIPrefabs/" + location + ".prefab"), parent, false);
            public UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
                CancellationToken cancellationToken = default, string packageName = "")
            {
                Calls++;
                ReceivedToken = cancellationToken;
                if (Pending) return Completion.Task.AttachExternalCancellation(cancellationToken);
                return UniTask.FromResult(Fail ? null : LoadGameObject(location, parent, packageName));
            }
        }

        private Scene _scene;
        private GameObject _root;
        private MainMenuUI _menu;
        private PrefabLoader _loader;
        private IUIResourceLoader _previousLoader;
        private object _previousPlayer;
        private object _previousStore, _previousRecord;
        private bool _destroyed;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/UIPrefabs/MainMenuUI.prefab"));
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _menu = new MainMenuUI();
            typeof(UIWindow).GetField("_panel", Private).SetValue(_menu, _root);
            _previousPlayer = typeof(GameApp).GetField("_playerState", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _previousStore = AppField("_playerStore").GetValue(null);
            _previousRecord = AppField("_playerRecord").GetValue(null);
            AppField("_playerStore").SetValue(null, null);
            AppField("_playerRecord").SetValue(null, null);
            typeof(GameApp).GetField("_playerState", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,
                new MainMenuPlayerState(30, TimeSpan.FromMinutes(5), 30, 777, 2500, DateTimeOffset.UtcNow));
            Invoke("ScriptGenerator");
            Invoke("OnCreate");
            _previousLoader = UIModule.Resource;
            _loader = new PrefabLoader();
            UIModule.Resource = _loader;
            _destroyed = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (!_destroyed) Invoke("OnDestroy");
            foreach (var store in _root.GetComponentsInChildren<RectTransform>(true).Where(t => t.name == "MysteriousStoreWidget(Clone)"))
                store.gameObject.SetActive(false);
            UIModule.Resource = _previousLoader;
            typeof(GameApp).GetField("_playerState", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousPlayer);
            AppField("_playerStore").SetValue(null, _previousStore);
            AppField("_playerRecord").SetValue(null, _previousRecord);
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void MainMenuRefreshDoesNotOpenStoreAndSameBattleCannotOpenTwice()
        {
            Invoke("OnRefresh");
            Assert.That(_loader.Calls, Is.Zero);
            OpenPreview(101).GetAwaiter().GetResult();
            var store = _root.transform.GetChild(_root.transform.childCount - 1);
            Assert.That(store.name, Does.StartWith("MysteriousStoreWidget"));
            var balance = store.Find("m_rect_Content/m_text_Balance").GetComponent<Text>();
            Assert.That(balance.text, Is.EqualTo("助力币  777"));
            OpenPreview(101).GetAwaiter().GetResult();
            Invoke("OnRefresh");
            Assert.That(_loader.Calls, Is.EqualTo(1));
            var button = _root.GetComponentsInChildren<Button>().First(b => b.name == "StartBattleButton");
            Assert.That(button.interactable, Is.False);
            store.Find("m_rect_Content/m_item_Offer0/m_btn_Buy").GetComponent<Button>().onClick.Invoke();
            Assert.That(GameApp.PlayerState.Coins, Is.EqualTo(737));
            Assert.That(balance.text, Is.EqualTo("助力币  737"));
        }

        [Test]
        public void MainMenuAndStoreRefreshShowSameChangedCoins()
        {
            OpenPreview(106).GetAwaiter().GetResult();
            MainMenuUI.State.AddCoins(23);
            Invoke("OnUpdate");
            var widget = (MysteriousStoreWidget)typeof(MainMenuUI).GetField("_mysteriousStore", Private).GetValue(_menu);
            typeof(MysteriousStoreWidget).GetMethod("OnUpdate", Private).Invoke(widget, null);
            Assert.That(_root.GetComponentsInChildren<Text>().First(t => t.name == "Coins").text, Is.EqualTo("800"));
            Assert.That(widget.transform.Find("m_rect_Content/m_text_Balance").GetComponent<Text>().text, Is.EqualTo("助力币  800"));
        }

        [UnityTest]
        public IEnumerator PurchaseSavesActualCoinsToIsolatedPlayerRecord() => SavePurchaseAsync().ToCoroutine();

        private async UniTask SavePurchaseAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FreshGuard-store-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "player.json");
            var store = new MatchPlayerStore(path);
            AppField("_playerStore").SetValue(null, store);
            AppField("_playerRecord").SetValue(null, new MatchPlayerRecord());
            try
            {
                OpenPreview(107).GetAwaiter().GetResult();
                var widget = (MysteriousStoreWidget)typeof(MainMenuUI).GetField("_mysteriousStore", Private).GetValue(_menu);
                widget.transform.Find("m_rect_Content/m_item_Offer0/m_btn_Buy").GetComponent<Button>().onClick.Invoke();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!File.Exists(path) && DateTime.UtcNow < deadline)
                    await UniTask.RunOnThreadPool(() => System.Threading.Thread.Sleep(10));
                Assert.That(File.Exists(path), Is.True, "购买必须调用存档流程。");
                var saved = await store.LoadAsync();
                Assert.That(saved.Coins, Is.EqualTo(737));
                Assert.That(saved.Restore(30, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow).Coins, Is.EqualTo(737));
                widget.transform.Find("m_rect_Content/m_item_Offer1/m_btn_Buy").GetComponent<Button>().onClick.Invoke();
                Assert.That(MainMenuUI.State.Coins, Is.EqualTo(737));
            }
            finally
            {
                AppField("_playerStore").SetValue(null, null);
                AppField("_playerRecord").SetValue(null, null);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static FieldInfo AppField(string name) => typeof(GameApp).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void EmptyOffersDoNotLoadAnything()
        {
            _menu.ShowMysteriousStorePreviewAsync(102, new MysteriousStorePreviewSession(MainMenuUI.State,
                Array.Empty<MysteriousStoreOfferData>())).GetAwaiter().GetResult();
            Assert.That(_loader.Calls, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SettlementReturnButtonOpensStoreForBothResults(bool victory)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/UIPrefabs/CheckOutUI.prefab"));
            SceneManager.MoveGameObjectToScene(root, _scene);
            var checkout = new CheckOutUI();
            typeof(UIWindow).GetField("_panel", Private).SetValue(checkout, root);
            try
            {
                foreach (var method in new[] { "ScriptGenerator", "OnCreate", "OnRefresh" })
                    typeof(CheckOutUI).GetMethod(method, Private).Invoke(checkout, null);
                checkout.Render(victory);
                var tables = new GameConfig.Tables(name => new Luban.ByteBuf(File.ReadAllBytes(
                    Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
                var saved = 0;
                var reward = new BattleSettlementReward(tables, MainMenuUI.State,
                    victory ? BattleSettlementResult.Victory : BattleSettlementResult.Defeat,
                    () => { saved++; return UniTask.CompletedTask; });
                checkout.BindReward(reward, () => OpenPreview(victory ? 109 : 108));
                var returnButton = root.transform.Find("m_rect_Content/m_btn_ReturnMain").GetComponent<Button>();
                Assert.That(returnButton.interactable, Is.True);
                returnButton.onClick.Invoke();
                Assert.That(reward.IsSaved, Is.True);
                Assert.That(saved, Is.EqualTo(1));
                var widget = (MysteriousStoreWidget)typeof(MainMenuUI).GetField("_mysteriousStore", Private).GetValue(_menu);
                Assert.That(widget, Is.Not.Null, "正常胜负结算返回都应创建商店。");
                Assert.That(widget.Visible, Is.True);
                Assert.That(widget.Session.PlayerState, Is.SameAs(MainMenuUI.State));
                Assert.That(MainMenuUI.State.Coins, Is.EqualTo(777 + reward.BaseAmount));
            }
            finally { typeof(CheckOutUI).GetMethod("OnDestroy", Private).Invoke(checkout, null); }
        }

        [Test]
        public void LoadFailureRestoresMenuAndDoesNotRetrySameBattle()
        {
            MainMenuUI.State.TrySpendEnergy(5);
            Invoke("OnRefresh");
            var countdown = _root.GetComponentsInChildren<Text>(true).First(t => t.name == "EnergyCountdown");
            var previousCountdown = countdown.text;
            var countdownObservedAt = DateTimeOffset.UtcNow;
            _loader.Fail = true;
            OpenPreview(103).GetAwaiter().GetResult();
            OpenPreview(103).GetAwaiter().GetResult();
            Assert.That(_loader.Calls, Is.EqualTo(1));
            Assert.That((bool)typeof(MainMenuUI).GetField("_openingMysteriousStore", Private).GetValue(_menu), Is.False);
            Assert.That((string)typeof(MainMenuUI).GetField("_statusMessage", Private).GetValue(_menu), Is.EqualTo("神秘商店加载失败"));
            StringAssert.IsMatch(@"^\d{2}:[0-5]\d$", countdown.text, "商店错误不能覆盖体力倒计时。");
            var before = TimeSpan.ParseExact(previousCountdown, @"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
            var after = TimeSpan.ParseExact(countdown.text, @"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
            var elapsedSeconds = (DateTimeOffset.UtcNow - countdownObservedAt).TotalSeconds;
            Assert.That((before - after).TotalSeconds, Is.InRange(0d, Math.Ceiling(elapsedSeconds) + 1d),
                "加载失败后倒计时应只随真实时间自然递减。");
            var status = _root.transform.Find("m_text_Status").GetComponent<Text>();
            Assert.That(status.gameObject.activeSelf, Is.True);
            Assert.That(status.text, Is.EqualTo("神秘商店加载失败"));
            typeof(MainMenuUI).GetField("_statusUntil", Private).SetValue(_menu, -1f);
            Invoke("OnUpdate");
            Assert.That(status.gameObject.activeSelf, Is.False);
            Assert.That(GameApp.PlayerState.Coins, Is.EqualTo(777));
            Assert.That(_root.GetComponentsInChildren<Button>().First(b => b.name == "StartBattleButton").interactable, Is.True);
        }

        [Test]
        public void DestroyingMenuCancelsPendingStoreLoad()
        {
            _loader.Pending = true;
            var task = OpenPreview(104);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));
            Invoke("OnDestroy");
            _destroyed = true;
            Assert.That(_loader.ReceivedToken.IsCancellationRequested, Is.True);
            task.GetAwaiter().GetResult();
            Assert.That(_root.transform.Find("MysteriousStoreWidget(Clone)"), Is.Null);
        }

        private void Invoke(string method) => typeof(MainMenuUI).GetMethod(method, Private).Invoke(_menu, null);
        private UniTask OpenPreview(long battleId) => _menu.ShowMysteriousStorePreviewAsync(battleId,
            MysteriousStorePreviewSession.CreateDefault(MainMenuUI.State));
    }
}
