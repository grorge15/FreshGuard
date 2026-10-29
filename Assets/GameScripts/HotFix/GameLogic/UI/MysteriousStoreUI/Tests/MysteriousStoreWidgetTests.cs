using System;
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
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class MysteriousStoreWidgetTests
    {
        private const string PrefabPath = "Assets/AssetRaw/UI/MysteriousStoreWidget.prefab";
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
            _widget.Bind(MysteriousStorePreviewSession.CreateDefault(), null);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(MysteriousStoreWidget).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_widget, null);
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void ButtonsReflectInsufficientFundsThenOnePurchaseSellsOutAllRows()
        {
            Click("m_rect_Content/m_item_Offer2/m_btn_Buy");
            Assert.That(TextAt("m_rect_Content/m_text_Feedback"), Is.EqualTo("助力币不足"));
            Assert.That(_widget.Session.Balance, Is.EqualTo(120));
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

        [Test]
        public void RemovalRequiresConfirmationAndOutsideOrCloseOnlyCancelDialog()
        {
            var closed = false;
            _widget.Bind(MysteriousStorePreviewSession.CreateDefault(), () => closed = true);
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
            Assert.That(_widget.Session.Balance, Is.EqualTo(120));
            Assert.That(_root.transform.Find("m_go_RemoveConfirmation").gameObject.activeSelf, Is.False);
            Click("m_rect_Content/m_btn_Close");
            Assert.That(closed, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ActualOfferCountAndEmptyHeldSlotsRenderCorrectly(int count)
        {
            _widget.Bind(new MysteriousStorePreviewSession(120,
                MysteriousStorePreviewSession.CreateDefault().Offers.Take(count)), null);
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
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/AssetRaw/Fonts/NotoSansSC-Regular.otf");
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
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/" + location + ".prefab"), parent, false);
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
        private bool _destroyed;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/MainMenuUI.prefab"));
            SceneManager.MoveGameObjectToScene(_root, _scene);
            _menu = new MainMenuUI();
            typeof(UIWindow).GetField("_panel", Private).SetValue(_menu, _root);
            _previousPlayer = typeof(GameApp).GetField("_playerState", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
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
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void MainMenuRefreshDoesNotOpenStoreAndSameBattleCannotOpenTwice()
        {
            Invoke("OnRefresh");
            Assert.That(_loader.Calls, Is.Zero);
            _menu.ShowMysteriousStorePreviewAsync(101).GetAwaiter().GetResult();
            var store = _root.transform.GetChild(_root.transform.childCount - 1);
            Assert.That(store.name, Does.StartWith("MysteriousStoreWidget"));
            _menu.ShowMysteriousStorePreviewAsync(101).GetAwaiter().GetResult();
            Invoke("OnRefresh");
            Assert.That(_loader.Calls, Is.EqualTo(1));
            var button = _root.GetComponentsInChildren<Button>().First(b => b.name == "StartBattleButton");
            Assert.That(button.interactable, Is.False);
            store.Find("m_rect_Content/m_item_Offer0/m_btn_Buy").GetComponent<Button>().onClick.Invoke();
            Assert.That(GameApp.PlayerState.Coins, Is.EqualTo(777));
        }

        [Test]
        public void EmptyOffersDoNotLoadAnything()
        {
            _menu.ShowMysteriousStorePreviewAsync(102, new MysteriousStorePreviewSession(0,
                Array.Empty<MysteriousStoreOfferData>())).GetAwaiter().GetResult();
            Assert.That(_loader.Calls, Is.Zero);
        }

        [Test]
        public void LoadFailureRestoresMenuAndDoesNotRetrySameBattle()
        {
            _loader.Fail = true;
            _menu.ShowMysteriousStorePreviewAsync(103).GetAwaiter().GetResult();
            _menu.ShowMysteriousStorePreviewAsync(103).GetAwaiter().GetResult();
            Assert.That(_loader.Calls, Is.EqualTo(1));
            Assert.That((bool)typeof(MainMenuUI).GetField("_openingMysteriousStore", Private).GetValue(_menu), Is.False);
            Assert.That((string)typeof(MainMenuUI).GetField("_statusMessage", Private).GetValue(_menu), Is.EqualTo("神秘商店加载失败"));
            Assert.That(GameApp.PlayerState.Coins, Is.EqualTo(777));
            Assert.That(_root.GetComponentsInChildren<Button>().First(b => b.name == "StartBattleButton").interactable, Is.True);
        }

        [Test]
        public void DestroyingMenuCancelsPendingStoreLoad()
        {
            _loader.Pending = true;
            var task = _menu.ShowMysteriousStorePreviewAsync(104);
            Assert.That(task.Status, Is.EqualTo(UniTaskStatus.Pending));
            Invoke("OnDestroy");
            _destroyed = true;
            Assert.That(_loader.ReceivedToken.IsCancellationRequested, Is.True);
            task.GetAwaiter().GetResult();
            Assert.That(_root.transform.Find("MysteriousStoreWidget(Clone)"), Is.Null);
        }

        private void Invoke(string method) => typeof(MainMenuUI).GetMethod(method, Private).Invoke(_menu, null);
    }
}
