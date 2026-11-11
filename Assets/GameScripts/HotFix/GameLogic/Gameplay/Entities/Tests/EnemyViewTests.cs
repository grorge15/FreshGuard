using System;
using System.IO;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class EnemyViewTests
    {
        private BattleEntityRegistry _registry;
        private GameObject _root;
        private Component _view;
        private Tables _tables;

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            _registry.Dispose();
        }

        [Test]
        public void DuplicateBindingNeverCreatesAnotherEntityAndWrongConfigIsRejected()
        {
            CreateView();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            Call("Bind", _registry, id);
            Assert.AreEqual(id, Property<int>("InstanceId"));
            var wrong = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2002), BattleSide.Player);
            Assert.Throws<ArgumentException>(() => Call("Bind", _registry, wrong));
            Assert.AreEqual(id, Property<int>("InstanceId"));
            Assert.AreEqual(3, _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player));
        }

        [Test]
        public void AttackReturnsToLastRequestedLocomotionAndCannotInterruptDeath()
        {
            CreateView();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            Call("SetMoving", true);
            Assert.AreEqual("Move", Property<string>("AnimationState"));
            Call("PlayAttack");
            Assert.AreEqual("Attack", Property<string>("AnimationState"));
            Call("SetMoving", false);
            Assert.AreEqual("Attack", Property<string>("AnimationState"));
            Call("OnAttackAnimationComplete");
            Assert.AreEqual("Idle", Property<string>("AnimationState"));
            EntityRemovalSnapshot removal;
            _registry.ApplyDamage(id, 100, out removal);
            Assert.AreEqual(true, Call("PlayRemoval", removal));
            Assert.AreEqual("Death", Property<string>("AnimationState"));
            Assert.AreEqual(false, Call("PlayAttack"));
            Call("OnAttackAnimationComplete");
            Assert.AreEqual("Death", Property<string>("AnimationState"));
        }

        [Test]
        public void RemovalSnapshotWorksAfterPoolReuseAndCompletionIsSentOnce()
        {
            CreateView();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            EntityRemovalSnapshot removal;
            _registry.ApplyDamage(id, 100, out removal);
            var newId = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            var completions = 0;
            Action<int> callback = value => { Assert.AreEqual(id, value); completions++; };
            _view.GetType().GetEvent("RemovalAnimationCompleted").AddEventHandler(_view, callback);
            Assert.AreEqual(true, Call("PlayRemoval", removal));
            Assert.AreEqual(false, Call("PlayRemoval", removal));
            Call("OnDeathAnimationComplete");
            Call("OnDeathAnimationComplete");
            Assert.AreEqual(1, completions);
            Assert.AreEqual(0, Property<int>("InstanceId"));
            Entity newEntity;
            Assert.IsTrue(_registry.TryGet(newId, out newEntity));
            Assert.AreEqual(100, ((EnemyEntity)newEntity).CurrentHp);
        }

        [Test]
        public void DisableAndReenableClearBindingAndRestoreIdle()
        {
            CreateView();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            Call("SetMoving", true);
            _root.SetActive(false);
            // 普通 MonoBehaviour 在 EditMode 不由激活切换派发生命周期消息。
            Lifecycle("OnDisable");
            _root.SetActive(true);
            Lifecycle("OnEnable");
            Assert.AreEqual(0, Property<int>("InstanceId"));
            Assert.AreEqual("Idle", Property<string>("AnimationState"));
        }

        private void Lifecycle(string name, params object[] args) => _view.GetType().GetMethod(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_view, args);

        [Test]
        public void WrongSnapshotIsRejectedAndManualRemovalCompletesImmediately()
        {
            CreateView();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            var otherId = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            var completions = 0;
            Action<int> callback = value => { Assert.AreEqual(id, value); completions++; };
            _view.GetType().GetEvent("RemovalAnimationCompleted").AddEventHandler(_view, callback);
            EntityRemovalSnapshot removal;
            _registry.Remove(otherId, EntityRemovalReason.Manual, out removal);
            Assert.AreEqual(false, Call("PlayRemoval", removal));
            Assert.AreEqual(id, Property<int>("InstanceId"));
            _registry.Remove(id, EntityRemovalReason.Manual, out removal);
            Assert.AreEqual(true, Call("PlayRemoval", removal));
            Assert.AreEqual(1, completions);
            Assert.AreEqual(0, Property<int>("InstanceId"));
            Call("OnDeathAnimationComplete");
            Assert.AreEqual(1, completions);
        }

        [Test]
        public void MissingRemovalSnapshotHidesViewWithoutReadingReusedEntity()
        {
            CreateView();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            EntityRemovalSnapshot removal;
            _registry.Remove(id, EntityRemovalReason.Manual, out removal);
            var nextId = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Lifecycle("Update");
            Assert.AreEqual(0, Property<int>("InstanceId"));
            Assert.IsFalse(_root.activeSelf);
            Entity entity;
            Assert.IsTrue(_registry.TryGet(nextId, out entity));
            Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
        }

        [Test]
        public void DamageFeedbackTurnsEnemySpritesRed()
        {
            CreateView();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.color = Color.white;
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);

            Assert.IsTrue((bool)Call("PlayHitFeedback", 0.12f));
            Assert.IsTrue(renderer.color.r > 0.8f && renderer.color.g < 0.4f && renderer.color.b < 0.4f);
        }

        [Test]
        public void HitShakeMovesOnlyVisualAndRepeatedHitsRestoreWithoutDrift()
        {
            var visual = CreateHitVisual();
            var origin = visual.localPosition;
            var collider = _root.AddComponent<BoxCollider2D>();
            _root.transform.position = new Vector3(2f, 3f, 0f);
            var rootPosition = _root.transform.position;
            var collisionCenter = collider.bounds.center;
            for (var hit = 0; hit < 6; hit++)
            {
                Assert.IsTrue((bool)Call("PlayHitFeedback", 0.12f));
                Lifecycle("LateUpdate");
                var displacement = Vector3.Distance(origin, visual.localPosition);
                Assert.Greater(displacement, 0f);
                Assert.LessOrEqual(displacement, 0.041f, "连续受击不应叠加抖动幅度");
                Lifecycle("AdvanceHitFeedback", 0.025f);
                Lifecycle("LateUpdate");
                Assert.AreEqual(rootPosition, _root.transform.position);
                Assert.AreEqual(collisionCenter, collider.bounds.center);
            }
            Lifecycle("AdvanceHitFeedback", 0.2f);
            Assert.That(Vector3.Distance(origin, visual.localPosition), Is.LessThan(0.00001f));
            Assert.IsFalse(Property<bool>("IsHitFlashing"));
        }

        [Test]
        public void HitShakePreservesAnimatedPoseAndFreezesWithAnimator()
        {
            var visual = CreateHitVisual();
            Call("PlayHitFeedback", 0.12f);
            Lifecycle("LateUpdate");
            var frozenPosition = visual.localPosition;
            _root.GetComponent<Animator>().speed = 0f;
            Lifecycle("AdvanceHitFeedback", 1f);
            Lifecycle("LateUpdate");
            Assert.AreEqual(frozenPosition, visual.localPosition);
            Assert.IsTrue(Property<bool>("IsHitFlashing"));
            Lifecycle("Update");
            var authoredPose = new Vector3(0.08f, 0.4f, 0f);
            visual.localPosition = authoredPose;
            Lifecycle("LateUpdate");
            Assert.Greater(Vector3.Distance(authoredPose, visual.localPosition), 0f);
            Lifecycle("Update");
            Assert.That(Vector3.Distance(authoredPose, visual.localPosition), Is.LessThan(0.00001f),
                "抖动应撤销自身偏移，保留动画本来写入的位置");
            _root.GetComponent<Animator>().speed = 1f;
            Lifecycle("AdvanceHitFeedback", 0.2f);
            Lifecycle("LateUpdate");
            Assert.That(Vector3.Distance(authoredPose, visual.localPosition), Is.LessThan(0.00001f));
        }

        [TestCase("Unbind")]
        [TestCase("Disable")]
        [TestCase("Death")]
        public void HitShakeClearsBeforeUnbindDisableOrDeath(string transition)
        {
            var visual = CreateHitVisual();
            var origin = visual.localPosition;
            Call("PlayHitFeedback", 0.12f);
            Lifecycle("LateUpdate");
            if (transition == "Unbind") Call("Unbind");
            else if (transition == "Disable") Lifecycle("OnDisable");
            else
            {
                _registry.ApplyDamage(Property<int>("InstanceId"), int.MaxValue, out var removal);
                Assert.IsTrue((bool)Call("PlayRemoval", removal));
            }
            Assert.That(Vector3.Distance(origin, visual.localPosition), Is.LessThan(0.00001f));
            Assert.IsFalse(Property<bool>("IsHitFlashing"));
            Assert.AreEqual(Color.white, visual.GetComponent<SpriteRenderer>().color);
        }

        private Transform CreateHitVisual()
        {
            CreateView();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(_root.transform, false);
            visual.localPosition = new Vector3(0f, 0.355f, 0f);
            visual.gameObject.AddComponent<SpriteRenderer>().color = Color.white;
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            return visual;
        }

        [Test]
        public void SortingOrderUsesWorldHeightAndStableInstanceTieBreaker()
        {
            CreateView();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            var firstId = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, firstId);
            _root.transform.position = new Vector3(0f, 2f, 0f);
            Lifecycle("LateUpdate");
            var highOrder = renderer.sortingOrder;
            Assert.AreEqual(SpriteSortPoint.Pivot, renderer.spriteSortPoint,
                "动画帧包围盒变化不应参与相同 Order 的距离排序");
            _root.transform.position = new Vector3(0f, -1f, 0f);
            Lifecycle("LateUpdate");
            Assert.Greater(renderer.sortingOrder, highOrder);
        }

        [Test]
        public void MovementFacingMirrorsArtworkWithoutMovingOrRotatingTheEnemy()
        {
            var renderer = CreateFacingView();
            _root.transform.position = new Vector3(2f, 3f, 0f);
            var position = _root.transform.position;
            var footPosition = renderer.transform.localPosition;

            Assert.AreEqual(true, Call("FaceMovement", Vector2.right));
            Assert.IsTrue(renderer.flipX, "原素材朝左，向右行进时应镜像。");
            Assert.AreEqual(position, _root.transform.position);
            Assert.AreEqual(Quaternion.identity, _root.transform.rotation);
            Assert.AreEqual(footPosition, renderer.transform.localPosition);
            Assert.AreEqual(true, Call("FaceMovement", Vector2.left));
            Assert.IsFalse(renderer.flipX);
        }

        [Test]
        public void VerticalTravelAndDeathKeepFacingWhileReuseRestoresTheAuthoredPose()
        {
            var renderer = CreateFacingView();
            Call("FaceMovement", Vector2.right);
            Assert.AreEqual(false, Call("FaceMovement", Vector2.up));
            Assert.AreEqual(false, Call("FaceMovement", new Vector2(0.03f, 1f)));
            Assert.IsTrue(renderer.flipX, "纵向路段不应因节点随机偏移而反复翻转。");
            Call("PlayHitFeedback", 0.12f);
            Assert.IsTrue(renderer.flipX);

            EntityRemovalSnapshot removal;
            _registry.ApplyDamage(Property<int>("InstanceId"), int.MaxValue, out removal);
            Assert.AreEqual(true, Call("PlayRemoval", removal));
            Assert.AreEqual(false, Call("FaceMovement", Vector2.left));
            Assert.IsTrue(renderer.flipX, "死亡应保持倒下前的朝向。");
            Call("OnDeathAnimationComplete");
            Assert.IsFalse(renderer.flipX, "回收时应恢复预制体朝向。");
            var nextId = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, nextId);
            Assert.IsFalse(renderer.flipX);
        }

        [Test]
        public void MovementFacingDoesNotChangeEnemiesThatHaveNotEnabledIt()
        {
            var renderer = CreateFacingView();
            var serialized = new SerializedObject(_view);
            serialized.FindProperty("_faceMovement").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(false, Call("FaceMovement", Vector2.right));
            Assert.IsFalse(renderer.flipX);
        }

        private SpriteRenderer CreateFacingView()
        {
            CreateView();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            var serialized = new SerializedObject(_view);
            serialized.FindProperty("_faceMovement").boolValue = true;
            serialized.FindProperty("_spriteFacesRight").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var id = _registry.CreateEnemyFromConfig(_tables.TbEnemy.Get(2001), BattleSide.Player);
            Call("Bind", _registry, id);
            return renderer;
        }

        private void CreateView()
        {
            var type = typeof(EnemyEntity).Assembly.GetType("GameLogic.EnemyView");
            Assert.IsNotNull(type, "预制体需要用于绑定纯逻辑实体的 EnemyView");
            _root = new GameObject("EnemyViewTest");
            _view = _root.AddComponent(type);
            var serialized = new SerializedObject(_view);
            serialized.FindProperty("_configId").intValue = 2001;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private object Call(string name, params object[] args)
        {
            try { return _view.GetType().GetMethod(name).Invoke(_view, args); }
            catch (System.Reflection.TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private T Property<T>(string name) => (T)_view.GetType().GetProperty(name).GetValue(_view);
    }
}
