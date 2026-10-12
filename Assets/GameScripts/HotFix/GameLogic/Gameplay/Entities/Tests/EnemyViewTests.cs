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

        private void Lifecycle(string name) => _view.GetType().GetMethod(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_view, null);

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
