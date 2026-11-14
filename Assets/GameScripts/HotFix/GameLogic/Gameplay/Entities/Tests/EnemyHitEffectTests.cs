using System.IO;
using System.Linq;
using System.Reflection;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyHitEffectTests
    {
        private Tables _tables;
        private BattleEntityRegistry _registry;
        private GameObject _root;
        private EnemyView _view;
        private EnemyHitEffect _effect;
        private int _id;

        [SetUp]
        public void SetUp()
        {
            _tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            _registry = new BattleEntityRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _registry.Dispose();
        }

        [Test]
        public void EveryConfiguredEnemyIncludesTheSharedHitFramesAsPrefabDependencies()
        {
            string[] sharedFrames = null;
            foreach (var row in _tables.TbEnemy.DataList)
            {
                var path = "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + row.EnemyPrefab + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, row.EnemyPrefab);
                var effect = prefab.GetComponentInChildren<EnemyHitEffect>(true);
                Assert.IsNotNull(effect, row.EnemyPrefab + " 缺少受击表现。");
                Assert.AreSame(effect, new SerializedObject(prefab.GetComponent<EnemyView>())
                    .FindProperty("_hitEffect").objectReferenceValue);
                var frames = new SerializedObject(effect).FindProperty("_frames");
                Assert.AreEqual(16, frames.arraySize);
                var paths = Enumerable.Range(0, frames.arraySize).Select(i =>
                    AssetDatabase.GetAssetPath(frames.GetArrayElementAtIndex(i).objectReferenceValue)).ToArray();
                Assert.IsTrue(paths.All(p => p.StartsWith("Assets/AssetRaw/Effects/EnemyHit/")));
                var dependencies = AssetDatabase.GetDependencies(path, true);
                Assert.IsTrue(paths.All(dependencies.Contains), "资源包应随怪物预制体携带受击帧。");
                if (sharedFrames != null) CollectionAssert.AreEqual(sharedFrames, paths);
                sharedFrames = paths;
                Assert.IsFalse(effect.GetComponent<SpriteRenderer>().enabled, "未命中时不显示爆点。");
            }
        }

        [TestCase(2001)]
        [TestCase(2002)]
        [TestCase(2003)]
        [TestCase(2004)]
        public void HitWorksOnEveryEnemyWithoutChangingBoundsCollisionOrAnimation(int configId)
        {
            CreateView(configId);
            _view.SetMoving(true);
            var bounds = _view.GetVisualBounds();
            var rootPosition = _root.transform.position;
            var collider = _root.AddComponent<BoxCollider2D>();
            var collisionCenter = collider.bounds.center;
            Assert.IsTrue(_view.PlayHitFeedback());
            Assert.IsTrue(_effect.IsPlaying);
            Assert.That(_effect.Duration, Is.EqualTo(.5f).Within(.001f));
            var renderer = _effect.GetComponent<SpriteRenderer>();
            Assert.IsTrue(renderer.enabled);
            Assert.AreEqual(Color.white, renderer.color, "火花不能被怪物闪红染色。");
            Assert.AreEqual(bounds, _view.GetVisualBounds(), "火花不能扩大怪物包围盒或移动伤害数字。");
            _view.GetVisualSorting(out var layer, out var order);
            Assert.AreEqual(layer, renderer.sortingLayerID);
            Assert.Greater(renderer.sortingOrder, order);
            Advance(.30f);
            Assert.IsFalse(_view.IsHitFlashing);
            Assert.IsTrue(_effect.IsPlaying, "闪红结束后仍应有扩散余光。");
            Advance(.21f);
            Assert.IsFalse(_effect.IsPlaying);
            Assert.IsFalse(renderer.enabled);
            Assert.AreEqual("Move", _view.AnimationState);
            Assert.AreEqual(rootPosition, _root.transform.position);
            Assert.AreEqual(collisionCenter, collider.bounds.center);
        }

        [Test]
        public void RepeatedHitsRestartTheSameEffectWithoutAccumulatingShakeOrSize()
        {
            CreateView(2004);
            _view.PlayHitFeedback();
            var firstSprite = _effect.GetComponent<SpriteRenderer>().sprite;
            var scale = _effect.transform.localScale;
            var center = _effect.transform.position;
            var childCount = _root.GetComponentsInChildren<Transform>(true).Length;
            for (var hit = 0; hit < 6; hit++)
            {
                Advance(.10f);
                Invoke("LateUpdate");
                Assert.IsTrue(_view.PlayHitFeedback());
                Assert.AreSame(firstSprite, _effect.GetComponent<SpriteRenderer>().sprite);
                Assert.That(Vector3.Distance(center, _effect.transform.position), Is.LessThan(.00001f));
                Assert.That(Vector3.Distance(scale, _effect.transform.localScale), Is.LessThan(.00001f));
            }
            Assert.AreEqual(childCount, _root.GetComponentsInChildren<Transform>(true).Length);
        }

        [Test]
        public void EffectFreezesAndResumesWithEnemyAnimator()
        {
            CreateView(2001);
            _view.PlayHitFeedback();
            Advance(.125f);
            var sprite = _effect.GetComponent<SpriteRenderer>().sprite;
            _root.GetComponent<Animator>().speed = 0f;
            Advance(2f);
            Assert.IsTrue(_effect.IsPlaying);
            Assert.AreSame(sprite, _effect.GetComponent<SpriteRenderer>().sprite);
            _root.GetComponent<Animator>().speed = 1f;
            Advance(.4f);
            Assert.IsFalse(_effect.IsPlaying);
        }

        [TestCase("Unbind")]
        [TestCase("Disable")]
        [TestCase("ManualRemoval")]
        public void RecycleAndNonFatalRemovalClearTheEffect(string transition)
        {
            CreateView(2002);
            _view.PlayHitFeedback();
            if (transition == "Unbind") _view.Unbind();
            else if (transition == "Disable") Invoke("OnDisable");
            else
            {
                Assert.IsTrue(_registry.Remove(_id, EntityRemovalReason.Manual, out var removal));
                Assert.IsTrue(_view.PlayRemoval(removal));
            }
            Assert.IsFalse(_effect.IsPlaying);
            Assert.IsNull(_effect.GetComponent<SpriteRenderer>().sprite);
            Assert.IsFalse(_effect.GetComponent<SpriteRenderer>().enabled);
            var nextId = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2002), BattleSide.Player);
            _view.Bind(_registry, nextId);
            Assert.IsFalse(_effect.IsPlaying, "复用后的怪物不能残留上一只的火花。");
        }

        [TestCase(2001)]
        [TestCase(2002)]
        [TestCase(2003)]
        [TestCase(2004)]
        public void FatalHitUsesBakedDeathFlashAndClearsTheIndependentHitOverlay(int configId)
        {
            CreateView(configId);
            _view.PlayHitFeedback();
            Assert.AreEqual(DamageResult.Killed, _registry.ApplyDamage(_id, int.MaxValue, out var removal));
            Assert.IsTrue(_view.PlayRemoval(removal));
            Assert.AreEqual("Death", _view.AnimationState);
            Assert.IsFalse(_view.IsHitFlashing);
            Assert.IsFalse(_effect.IsPlaying, "死亡序列已包含击杀闪光，不能重复叠加独立受击爆点。");
            Advance(.10f);
            Assert.IsFalse(_effect.IsPlaying);
            _view.OnDeathAnimationComplete();
            Assert.IsFalse(_effect.IsPlaying);
        }

        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void HitRendererBelongsToTheSameBattleCameraAsTheEnemy(BattleSide side)
        {
            CreateView(2001, side);
            var cameras = new GameObject("HitEffectCameraTest");
            try
            {
                var registry = cameras.AddComponent<BattleSideRenderRegistry>();
                var player = new GameObject("PlayerCamera");
                player.transform.SetParent(cameras.transform);
                var opponent = new GameObject("OpponentCamera");
                opponent.transform.SetParent(cameras.transform);
                var playerCamera = player.AddComponent<Camera>();
                var opponentCamera = opponent.AddComponent<Camera>();
                registry.BindCameras(playerCamera, opponentCamera);
                registry.RegisterRoot(_root, side);
                _view.PlayHitFeedback();
                var renderer = _effect.GetComponent<SpriteRenderer>();
                var before = typeof(BattleSideRenderRegistry).GetMethod("OnCameraPreCull", BindingFlags.Instance | BindingFlags.NonPublic);
                var after = typeof(BattleSideRenderRegistry).GetMethod("OnCameraPostRender", BindingFlags.Instance | BindingFlags.NonPublic);
                before.Invoke(registry, new object[] { playerCamera });
                Assert.AreEqual(side == BattleSide.Opponent, renderer.forceRenderingOff);
                after.Invoke(registry, new object[] { playerCamera });
                before.Invoke(registry, new object[] { opponentCamera });
                Assert.AreEqual(side == BattleSide.Player, renderer.forceRenderingOff);
                after.Invoke(registry, new object[] { opponentCamera });
                Assert.IsFalse(renderer.forceRenderingOff);
            }
            finally { Object.DestroyImmediate(cameras); }
        }

        private void CreateView(int configId, BattleSide side = BattleSide.Player)
        {
            var config = _tables.TbEnemy.Get(configId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Prefabs/" + config.EnemyPrefab + ".prefab");
            _root = Object.Instantiate(prefab);
            _view = _root.GetComponent<EnemyView>();
            _effect = _root.GetComponentInChildren<EnemyHitEffect>(true);
            Assert.IsNotNull(_effect);
            Invoke("Awake");
            _id = _registry.CreateEnemyFromConfig(config, side);
            _view.Bind(_registry, _id);
        }

        private void Advance(float seconds) => Invoke("AdvanceHitFeedback", seconds);
        private object Invoke(string name, params object[] args)
            => typeof(EnemyView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_view, args);
    }
}
