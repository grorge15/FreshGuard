using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class BattleScreenLayoutControllerTests
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private GameObject _panel;
        private Sprite _sprite;
        private Camera _camera;
        private PlacementController _placement;
        private PlacementBoardView _player;
        private PlacementBoardView _opponent;
        private BattleScreenLayoutController _layout;
        private MainCombatUI _window;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ScreenLayoutTests");
            _camera = Child("Camera").AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.transform.position = new Vector3(0.3f, 0.4f, -10);
            _camera.orthographicSize = 5;
            _sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1);
            var template = Child("Slot").AddComponent<SpriteRenderer>();
            template.sprite = _sprite;
            template.gameObject.SetActive(false);
            _player = Board("Player", new Vector3(0, -3, 0), template);
            _opponent = Board("Opponent", new Vector3(0, 3, 0), template);
            _placement = _root.AddComponent<PlacementController>();
            Set(_placement, "_playerBoard", _player);
            Set(_placement, "_enemyBoard", _opponent);
            Set(_placement, "_sceneCamera", _camera);
            Assert.IsTrue(_placement.Initialize());
            _layout = _root.AddComponent<BattleScreenLayoutController>();
            Set(_layout, "_placement", _placement);
            Set(_layout, "_playerBoard", _player);
            Set(_layout, "_opponentBoard", _opponent);
            Set(_layout, "_playerFixedRenderers", new[] { Fixed("PlayerTower", new Vector3(2.5f, -1.3f, 0)) });
            Set(_layout, "_opponentFixedRenderers", new[] { Fixed("OpponentTower", new Vector3(-2.5f, 1.3f, 0)) });
            _panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab"));
            _window = new MainCombatUI();
            typeof(UIWindow).GetField("_panel", PRIVATE).SetValue(_window, _panel);
            typeof(UIWindow).GetField("_canvas", PRIVATE).SetValue(_window, _panel.GetComponent<Canvas>());
            typeof(MainCombatUI).GetMethod("OnCreate", PRIVATE).Invoke(_window, null);
            Canvas.ForceUpdateCanvases();
        }

        [UnityTest]
        public IEnumerator BindKeepsWorldLayoutProjectsInsideAndUnbindRestoresCamera()
        {
            var originalPosition = _camera.transform.position;
            var originalSize = _camera.orthographicSize;
            var playerPosition = _player.transform.position;
            var playerScale = _player.transform.localScale;
            yield return _layout.BindAsync(_window, CancellationToken.None).ToCoroutine();
            Assert.IsTrue(_layout.IsBound);
            Assert.That(_layout.PlayerBounds.max.x, Is.GreaterThanOrEqualTo(3), "未激活的塔必须参与边界。");
            AssertBounds(_layout.PlayerBounds);
            AssertBounds(_layout.OpponentBounds);
            var adaptedPosition = _camera.transform.position;
            var adaptedSize = _camera.orthographicSize;
            Assert.IsTrue(_layout.RefreshLayout());
            Assert.That(_camera.transform.position, Is.EqualTo(adaptedPosition));
            Assert.That(_camera.orthographicSize, Is.EqualTo(adaptedSize));
            Assert.That(_player.transform.position, Is.EqualTo(playerPosition));
            Assert.That(_player.transform.localScale, Is.EqualTo(playerScale));
            var cell = new BoardCoordinate(3, 3);
            var world = _player.GetCellWorldPosition(cell);
            PlacementBoardView board;
            BoardCoordinate hit;
            Assert.IsTrue(_placement.TryGetPointerCell(_camera.WorldToScreenPoint(world), out board, out hit));
            Assert.AreSame(_player, board);
            Assert.AreEqual(cell, hit);
            Vector3 ignored;
            Assert.IsFalse(_placement.TryGetPointerWorldPosition(Vector2.zero, out ignored));
            Set(_placement, "_inputScreenSize", Vector2Int.zero);
            Assert.IsFalse(_placement.IsReady, "布局更新前不能接受旧屏幕映射的输入。");
            Assert.IsFalse(_placement.TryGetPointerCell(_camera.WorldToScreenPoint(world), out board, out hit));
            Assert.IsTrue(_layout.RefreshLayout());
            Assert.IsTrue(_placement.IsReady);
            _layout.Unbind();
            _layout.Unbind();
            Assert.That(_camera.transform.position, Is.EqualTo(originalPosition));
            Assert.That(_camera.orthographicSize, Is.EqualTo(originalSize));
            Assert.IsTrue(_placement.TryGetPointerWorldPosition(Vector2.zero, out ignored));
        }

        [UnityTest]
        public IEnumerator SideAndBottomSafeAreaFitShopWithoutCumulativeScale()
        {
            yield return _layout.BindAsync(_window, CancellationToken.None).ToCoroutine();
            var safe = new Rect(Screen.width * 0.12f, Screen.height * 0.08f, Screen.width * 0.76f, Screen.height * 0.85f);
            Rect first, second;
            Assert.IsTrue(_window.TryLayoutShop(safe, out first));
            var scale = _window.Shop.rectTransform.localScale;
            Assert.IsTrue(_window.TryLayoutShop(safe, out second));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(_window.Shop.rectTransform.localScale, Is.EqualTo(scale));
            Assert.That(first.xMin, Is.GreaterThanOrEqualTo(safe.xMin + 12 * _window.ScreenLayoutScale - 0.01f));
            Assert.That(first.xMax, Is.LessThanOrEqualTo(safe.xMax - 12 * _window.ScreenLayoutScale + 0.01f));
            Assert.That(first.yMin, Is.EqualTo(safe.yMin + 12 * _window.ScreenLayoutScale).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator CancelledBindAndDestroyedWindowRestoreCamera()
        {
            var original = _camera.transform.position;
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = _layout.BindAsync(_window, cancellation.Token).SuppressCancellationThrow();
            yield return cancelled.ToCoroutine(result => Assert.IsTrue(result));
            Assert.IsFalse(_layout.IsBound);
            Assert.AreEqual(original, _camera.transform.position);
            cancellation.Dispose();
            yield return _layout.BindAsync(_window, CancellationToken.None).ToCoroutine();
            typeof(MainCombatUI).GetMethod("OnDestroy", PRIVATE).Invoke(_window, null);
            Assert.IsFalse(_layout.IsBound);
            Assert.AreEqual(original, _camera.transform.position);
        }

        private void AssertBounds(Bounds bounds)
        {
            for (var i = 0; i < 4; i++)
            {
                var point = _camera.WorldToScreenPoint(new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, 0));
                var area = _layout.AvailableScreenRect;
                Assert.That(point.x, Is.InRange(area.xMin - 0.01f, area.xMax + 0.01f));
                Assert.That(point.y, Is.InRange(area.yMin - 0.01f, area.yMax + 0.01f));
            }
        }

        private PlacementBoardView Board(string name, Vector3 position, SpriteRenderer template)
        {
            var go = Child(name);
            go.transform.position = position;
            var board = go.AddComponent<PlacementBoardView>();
            Set(board, "_slotPrefab", template);
            Assert.IsTrue(board.Initialize());
            return board;
        }

        private SpriteRenderer Fixed(string name, Vector3 position)
        {
            var renderer = Child(name).AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.transform.position = position;
            renderer.gameObject.SetActive(false);
            return renderer;
        }

        private GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            return go;
        }

        private static void Set(object target, string name, object value) => target.GetType().GetField(name, PRIVATE).SetValue(target, value);

        [TearDown]
        public void TearDown()
        {
            _layout.Unbind();
            DisposeChildren(_window);
            Object.DestroyImmediate(_panel);
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_sprite);
        }

        private static void DisposeChildren(UIBase parent)
        {
            var children = (IList)typeof(UIBase).GetField("ListChild", PRIVATE).GetValue(parent);
            foreach (UIWidget child in children)
            {
                DisposeChildren(child);
                typeof(UIBase).GetMethod("RemoveAllUIEvent", PRIVATE).Invoke(child, null);
                child.GetType().GetMethod("OnDestroy", PRIVATE).Invoke(child, null);
            }
        }
    }
}
