using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class GlassCurrencyFlightTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private Camera _camera;
        private RectTransform _layer;
        private RectTransform _target;
        private GlassCurrencyFlightWidget _widget;

        [SetUp]
        public void SetUp()
        {
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab"));
            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _layer = (RectTransform)_root.transform.Find("GlassCurrencyFlight");
            _target = (RectTransform)_root.transform.Find("BattleShopUI/m_rect_ShopPanel/EnergyBalance/m_img_EnergyCoin");
            _camera = new GameObject("TestProjectionCamera", typeof(Camera)).GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.transform.position = new Vector3(0f, 0f, -10f);
            _widget = new GlassCurrencyFlightWidget();
            typeof(UIWidget).GetProperty("gameObject").SetValue(_widget, _layer.gameObject);
            typeof(UIWidget).GetProperty("transform").SetValue(_widget, _layer);
            typeof(UIWidget).GetProperty("rectTransform").SetValue(_widget, _layer);
            typeof(GlassCurrencyFlightWidget).GetMethod("ScriptGenerator", Private).Invoke(_widget, null);
            typeof(GlassCurrencyFlightWidget).GetMethod("OnCreate", Private).Invoke(_widget, null);
            _widget.Bind(null, _camera, _target);
            Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void TearDown()
        {
            _widget.Clear();
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_camera.gameObject);
        }

        private RectTransform FirstCoin => (RectTransform)_layer.Find("FlyingEnergyCoin");
        private Vector2 TargetPoint => _layer.InverseTransformPoint(_target.TransformPoint(_target.rect.center));

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(20)]
        public void EnemyDropCountCreatesThatManyIconsAndRecyclesAllOfThem(int count)
        {
            _widget.Show(Vector3.zero, count);
            Assert.AreEqual(count, _widget.ActiveCount);
            Assert.AreEqual(count + 1, _layer.childCount, "Includes the hidden template.");
            _widget.Advance(4f);
            Assert.AreEqual(0, _widget.ActiveCount);
        }

        [Test]
        public void ProjectsFromWorldThroughCameraAndUsesNonBlockingEnergyCoinSprites()
        {
            var origin = new Vector3(1f, 2f, 0f);
            _widget.Show(origin);
            Assert.AreEqual(4, _widget.ActiveCount);
            var expected = _camera.WorldToScreenPoint(origin);
            var actual = RectTransformUtility.WorldToScreenPoint(null, FirstCoin.position);
            Assert.Less(Vector2.Distance(expected, actual), 0.1f);
            Assert.IsFalse(_layer.GetComponent<CanvasGroup>().blocksRaycasts);
            foreach (var image in _layer.GetComponentsInChildren<Image>(true))
            {
                Assert.IsFalse(image.raycastTarget);
                Assert.AreEqual("Assets/AssetRaw/Actor/UISprites/Battle/energyCoin.png", AssetDatabase.GetAssetPath(image.sprite));
            }
        }

        [Test]
        public void HoldsAfterScatterThenAcceleratesOnAStraightLineToTheActualTarget()
        {
            _widget.Show(Vector3.zero);
            _widget.Advance(GlassCurrencyFlightWidget.ScatterDuration);
            var burst = FirstCoin.anchoredPosition;
            _widget.Advance(GlassCurrencyFlightWidget.FlightDelay - GlassCurrencyFlightWidget.ScatterDuration);
            Assert.Less(Vector2.Distance(burst, FirstCoin.anchoredPosition), 0.01f);
            var destination = TargetPoint;
            var previous = burst;
            var previousDistance = 0f;
            for (var step = 1; step <= 3; step++)
            {
                _widget.Advance(GlassCurrencyFlightWidget.FlightDuration * 0.25f);
                var current = FirstCoin.anchoredPosition;
                var expected = Vector2.LerpUnclamped(burst, destination, Mathf.Pow(step * 0.25f, 3f));
                Assert.Less(Vector2.Distance(current, expected), 0.05f);
                var distance = Vector2.Distance(current, previous);
                Assert.Greater(distance, previousDistance);
                previousDistance = distance;
                previous = current;
            }
            _target.anchoredPosition += new Vector2(50f, 20f);
            _widget.Advance(0f);
            Assert.Less(Vector2.Distance(FirstCoin.anchoredPosition,
                Vector2.LerpUnclamped(burst, TargetPoint, Mathf.Pow(0.75f, 3f))), 0.05f);
        }

        [Test]
        public void BoundsConcurrentIconsReusesThemAndHideRestoresTargetScale()
        {
            var scale = _target.localScale;
            for (var i = 0; i < 40; i++) _widget.Show(Vector3.zero);
            Assert.AreEqual(GlassCurrencyFlightWidget.MaxActiveIcons, _widget.ActiveCount);
            var allocated = _layer.childCount;
            _widget.Advance(1.2f);
            Assert.AreEqual(0, _widget.ActiveCount);
            _widget.Advance(0.1f);
            Assert.Greater(_target.localScale.x, scale.x);
            _widget.Show(Vector3.zero);
            Assert.AreEqual(allocated, _layer.childCount);
            _widget.Visible = false;
            Assert.AreEqual(0, _widget.ActiveCount);
            Assert.AreEqual(scale, _target.localScale);
            foreach (Transform child in _layer) Assert.IsFalse(child.gameObject.activeSelf);
        }

        [Test]
        public void BehindCameraOriginsAndMissingTargetDoNotSpawnAndRebindingClearsOldFlights()
        {
            _widget.Show(new Vector3(0f, 0f, -20f));
            Assert.AreEqual(0, _widget.ActiveCount);
            _widget.Show(Vector3.zero);
            _widget.Bind(null, _camera, null);
            Assert.AreEqual(0, _widget.ActiveCount);
            _widget.Show(Vector3.zero);
            Assert.AreEqual(0, _widget.ActiveCount);
        }
    }
}
