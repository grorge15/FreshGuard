using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class BattleInfoRenderLayerTests
    {
        private GameObject _root;
        private Canvas _source;
        private Camera _uiCamera;
        private RectTransform _info;
        private IDisposable _layer;
        private readonly Vector3[] _before = new Vector3[4];
        private readonly Vector3[] _after = new Vector3[4];

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("InformationLayerTest");
            _source = new GameObject("CombatUI", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            _source.transform.SetParent(_root.transform, false);
            _source.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)_source.transform;
            rect.sizeDelta = new Vector2(1080, 1920);
            rect.localScale = Vector3.one * 0.005f;
            rect.localPosition = Vector3.forward * 100;
            _info = new GameObject("Info", typeof(RectTransform)).GetComponent<RectTransform>();
            _info.SetParent(rect, false);
            _info.sizeDelta = new Vector2(1040, 156);
            _info.anchoredPosition = new Vector2(15, 200);
            _info.localScale = new Vector3(0.8f, 0.8f, 1);
            _info.gameObject.layer = 5;
            var child = new GameObject("Avatar");
            child.transform.SetParent(_info, false);
            child.layer = 2;
            _uiCamera = new GameObject("UICamera", typeof(Camera)).GetComponent<Camera>();
            _uiCamera.transform.SetParent(_root.transform, false);
            _uiCamera.orthographic = true;
            _uiCamera.depth = 2;
        }

        [TearDown]
        public void TearDown()
        {
            _layer?.Dispose();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void SeparateRenderPassPreservesScreenGeometryAndTracksParentResize()
        {
            _info.GetWorldCorners(_before);
            Bind();
            AssertCorners();
            var canvas = _info.GetComponentInParent<Canvas>();
            Assert.AreNotSame(_source, canvas);
            Assert.AreEqual(LayerMask.GetMask("BattleInfo"), canvas.worldCamera.cullingMask);
            Assert.That(canvas.worldCamera.depth, Is.LessThan(-1f));
            Assert.That(canvas.worldCamera.depth, Is.GreaterThan(-2f));
            Assert.AreEqual(CameraClearFlags.Depth, canvas.worldCamera.clearFlags);
            // 已确认的布局坐标应在新旧父节点下得到相同的世界投影。
            var sourceRect = (RectTransform)_source.transform;
            sourceRect.sizeDelta = new Vector2(1080, 2160);
            sourceRect.localScale = Vector3.one * 0.007f;
            var position = _info.localPosition;
            var scale = _info.localScale;
            var expected = new GameObject("Expected", typeof(RectTransform)).GetComponent<RectTransform>();
            expected.SetParent(sourceRect, false);
            expected.sizeDelta = _info.sizeDelta;
            expected.localPosition = position;
            expected.localScale = scale;
            expected.GetWorldCorners(_before);
            Refresh(true);
            AssertCorners();
        }

        [Test]
        public void HideAndShowAreImmediateAndDisposeRestoresLayersAndHierarchy()
        {
            var parent = _info.parent;
            var position = _info.localPosition;
            var scale = _info.localScale;
            var sibling = _info.GetSiblingIndex();
            Bind();
            var canvas = _info.GetComponentInParent<Canvas>();
            var camera = canvas.worldCamera;
            Refresh(true);
            Assert.IsTrue(canvas.enabled);
            Assert.IsTrue(camera.enabled);
            Refresh(false);
            Assert.IsFalse(canvas.enabled);
            Assert.IsFalse(camera.enabled);
            Refresh(true);
            Assert.IsTrue(canvas.enabled);
            _layer.Dispose();
            _layer.Dispose();
            Assert.AreSame(parent, _info.parent);
            Assert.AreEqual(position, _info.localPosition);
            Assert.AreEqual(scale, _info.localScale);
            Assert.AreEqual(sibling, _info.GetSiblingIndex());
            Assert.AreEqual(5, _info.gameObject.layer);
            Assert.AreEqual(2, _info.GetChild(0).gameObject.layer);
            Assert.IsTrue(canvas == null);
            Assert.IsTrue(camera == null);
        }

        private void Bind()
        {
            var type = typeof(BattleScreenLayout).Assembly.GetType("GameLogic.BattleInfoRenderLayer");
            Assert.IsNotNull(type);
            _layer = (IDisposable)Activator.CreateInstance(type, new object[] { _info, _source, _uiCamera, _root.transform, -1.5f });
        }

        private void Refresh(bool visible) => _layer.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.Public).Invoke(_layer, new object[] { visible });

        private void AssertCorners()
        {
            _info.GetWorldCorners(_after);
            for (var i = 0; i < 4; i++) Assert.That(Vector3.Distance(_before[i], _after[i]), Is.LessThan(0.0001f));
        }
    }
}
