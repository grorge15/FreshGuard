using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class BattleSideRenderRegistryTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Component _registry;
        private Camera _player;
        private Camera _opponent;

        [SetUp]
        public void SetUp()
        {
            var type = typeof(BattleScreenLayout).Assembly.GetType("GameLogic.BattleSideRenderRegistry");
            Assert.IsNotNull(type, "V2 必须提供每局独立的渲染登记器。");
            _registry = Create("Host").AddComponent(type);
            _player = Create("PlayerCamera").AddComponent<Camera>();
            _opponent = Create("OpponentCamera").AddComponent<Camera>();
            Call("BindCameras", _player, _opponent);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var value in _objects) if (value != null) Object.DestroyImmediate(value);
            _objects.Clear();
        }

        [Test]
        public void OnlyRegisteredCameraHidesOtherSideAndRestoresOriginalFlags()
        {
            var player = Renderer("Player", BattleSide.Player);
            var opponent = Renderer("Opponent", BattleSide.Opponent);
            var hidden = Renderer("AlreadyHidden", BattleSide.Opponent);
            hidden.forceRenderingOff = true;
            var layer = opponent.gameObject.layer;
            Call("OnCameraPreCull", Create("UnrelatedCamera").AddComponent<Camera>());
            Assert.IsFalse(opponent.forceRenderingOff);
            Call("OnCameraPreCull", _player);
            Assert.IsFalse(player.forceRenderingOff);
            Assert.IsTrue(opponent.forceRenderingOff);
            Assert.AreEqual(layer, opponent.gameObject.layer);
            Call("OnCameraPostRender", _player);
            Assert.IsFalse(opponent.forceRenderingOff);
            Assert.IsTrue(hidden.forceRenderingOff);
        }

        [Test]
        public void NestedSideCamerasRestoreOuterMaskThenOriginalState()
        {
            var player = Renderer("Player", BattleSide.Player);
            var opponent = Renderer("Opponent", BattleSide.Opponent);
            Call("OnCameraPreCull", _player);
            Call("OnCameraPreCull", _opponent);
            Assert.IsTrue(player.forceRenderingOff);
            Assert.IsFalse(opponent.forceRenderingOff, "嵌套相机不能继承外层相机的隐藏状态。");
            Call("OnCameraPostRender", _opponent);
            Assert.IsFalse(player.forceRenderingOff);
            Assert.IsTrue(opponent.forceRenderingOff);
            Call("OnCameraPostRender", _player);
            Assert.IsFalse(opponent.forceRenderingOff);
        }

        [Test]
        public void NestedUnrelatedCameraSuspendsMaskAndThenRestoresOuterSide()
        {
            var opponent = Renderer("Opponent", BattleSide.Opponent);
            var unrelated = Create("NestedUnrelatedCamera").AddComponent<Camera>();
            Call("OnCameraPreCull", _player);
            Call("OnCameraPreCull", unrelated);
            Assert.IsFalse(opponent.forceRenderingOff, "无关相机不能继承战场相机的临时隐藏。");
            Call("OnCameraPostRender", unrelated);
            Assert.IsTrue(opponent.forceRenderingOff);
            Call("OnCameraPostRender", _player);
            Assert.IsFalse(opponent.forceRenderingOff);
        }

        [Test]
        public void PoolDisableRebindAndDestroyReleasePendingMask()
        {
            var opponent = Renderer("Pooled", BattleSide.Opponent);
            Call("OnCameraPreCull", _player);
            opponent.gameObject.SetActive(false);
            Assert.IsFalse(opponent.forceRenderingOff, "回收对象不能携带临时隐藏状态。");
            Call("RegisterRoot", opponent.gameObject, BattleSide.Player);
            opponent.gameObject.SetActive(true);
            Assert.IsFalse(opponent.forceRenderingOff);
            Call("OnCameraPostRender", _player);
            Call("OnCameraPreCull", _opponent);
            Assert.IsTrue(opponent.forceRenderingOff);
            Object.DestroyImmediate(_registry);
            Assert.IsFalse(opponent.forceRenderingOff);
        }

        [Test]
        public void DisableAndClearRecoverInterruptedRenderAndRemoveCameraBinding()
        {
            var opponent = Renderer("Opponent", BattleSide.Opponent);
            Call("OnCameraPreCull", _player);
            ((Behaviour)_registry).enabled = false;
            Assert.IsFalse(opponent.forceRenderingOff);
            ((Behaviour)_registry).enabled = true;
            Call("BindCameras", _player, _opponent);
            Call("OnCameraPreCull", _player);
            Call("Clear");
            Assert.IsFalse(opponent.forceRenderingOff);
            Call("OnCameraPreCull", _player);
            Assert.IsFalse(opponent.forceRenderingOff);
        }

        [Test]
        public void SecondBattleCameraDoesNotAffectFirstBattleRenderers()
        {
            var opponent = Renderer("FirstBattleOpponent", BattleSide.Opponent);
            var second = Create("SecondHost").AddComponent(_registry.GetType());
            var camera = Create("SecondCamera").AddComponent<Camera>();
            second.GetType().GetMethod("BindCameras").Invoke(second,
                new object[] { camera, Create("SecondOpponentCamera").AddComponent<Camera>() });
            Call("OnCameraPreCull", camera);
            Assert.IsFalse(opponent.forceRenderingOff);
        }

        [Test]
        public void PreviewCreatedAfterStaticBoardRegistrationKeepsBoardSide()
        {
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1);
            try
            {
                var template = Create("Template").AddComponent<SpriteRenderer>();
                template.sprite = sprite;
                template.gameObject.SetActive(false);
                var board = Create("Board").AddComponent<PlacementBoardView>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(PlacementBoardView).GetField("_slotPrefab", flags).SetValue(board, template);
                typeof(PlacementBoardView).GetField("_columns", flags).SetValue(board, 1);
                typeof(PlacementBoardView).GetField("_rows", flags).SetValue(board, 1);
                Assert.IsTrue(board.Initialize());
                Call("RegisterRoot", board.gameObject, BattleSide.Opponent);
                board.ShowPreview(new BoardPlacementResult(true, new[] { new BoardCoordinate(0, 0) }));
                var preview = board.transform.Find("PlacementPreview/PreviewCell_0").GetComponent<SpriteRenderer>();
                Call("OnCameraPreCull", _player);
                Assert.IsTrue(preview.forceRenderingOff, "静态登记之后创建的预览也必须隔离。");
                Call("OnCameraPostRender", _player);
                Assert.IsFalse(preview.forceRenderingOff);
            }
            finally { Object.DestroyImmediate(sprite); }
        }

        [Test]
        public void RemovingOverlappingRootKeepsDescendantRegistration()
        {
            var parent = Create("Parent");
            var child = Create("Child");
            child.transform.SetParent(parent.transform);
            var renderer = child.AddComponent<SpriteRenderer>();
            Call("RegisterRoot", parent, BattleSide.Player);
            Call("RegisterRoot", child, BattleSide.Opponent);
            Call("UnregisterRoot", parent);
            Call("OnCameraPreCull", _player);
            Assert.IsTrue(renderer.forceRenderingOff);
            Call("OnCameraPostRender", _player);
            Assert.IsFalse(renderer.forceRenderingOff);
        }

        private SpriteRenderer Renderer(string name, BattleSide side)
        {
            var renderer = Create(name).AddComponent<SpriteRenderer>();
            Call("RegisterRoot", renderer.gameObject, side);
            return renderer;
        }

        private GameObject Create(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }

        private void Call(string name, params object[] arguments)
        {
            var method = _registry.GetType().GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, name);
            method.Invoke(_registry, arguments);
        }
    }
}
