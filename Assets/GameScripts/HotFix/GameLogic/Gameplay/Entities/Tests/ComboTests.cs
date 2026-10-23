using System;
using System.Collections.Generic;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class ComboTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleEntityRegistry _registry;
        private PhysicalCollisionBall _ball;
        private Transform _popups;
        private bool _running;

        [SetUp]
        public void SetUp()
        {
            _registry = new BattleEntityRegistry();
            _running = true;
            _popups = NewObject("ComboTestPopups").transform;
            var go = NewObject("ComboTestBall");
            go.SetActive(false);
            _ball = go.AddComponent<PhysicalCollisionBall>();
            Set(_ball, "launchOnEnable", false);
            go.SetActive(true);
            if (Get(_ball, "_body") == null) Invoke(_ball, "Awake");
            _ball.GlobalValueProvider = key => key == 34 ? 1.5f : key == 17 ? 0.7f : 0f;
            _ball.BindBattle(1, BattleSide.Player, () => _running);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _objects.Clear();
            _registry.Dispose();
        }

        [Test]
        public void GeneratedGlobalContainsOnePointFiveSecondComboTimeout()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/AssetRaw/Configs/bytes/globalcfg_tbglobal.bytes");
            var table = new GameConfig.globalcfg.TbGlobal(new ByteBuf(asset.bytes));
            Assert.IsNotNull(table.GetOrDefault(34), "Combo断档配置34尚未生成");
            Assert.AreEqual(1.5f, table.Get(34).Value);
        }

        [Test]
        public void RobotCooldownDoesNotPreventCountingAndRepeatedContactDoesNotExtendWindow()
        {
            Bind();
            var view = Robot();
            Entity entity; _registry.TryGet(view.InstanceId, out entity);
            Assert.IsTrue(((RobotEntity)entity).TryConsumeAttackTrigger(3f));
            Hit(view);
            Assert.AreEqual(1, Count);
            Advance(1f);
            Hit(view);
            Assert.AreEqual(1, Count);
            Advance(0.51f);
            Assert.AreEqual(0, Count, "重复接触不能刷新断档时间");
            Hit(view);
            Assert.AreEqual(0, Count, "超时不能使尚未脱离的接触再记一次");
        }

        [Test]
        public void MultipleCellsAndDifferentRobotsUseSeparateContactEpisodes()
        {
            Bind(); var first = Robot(); var second = Robot();
            foreach (var collider in first.GetPhysicalColliders()) Hit(first, collider);
            Hit(second);
            Assert.AreEqual(2, Count);
            // Fixtures are physically separated: reconciliation closes these completed episodes.
            Invoke(_ball, "ReconcileRobotContacts");
            Invoke(_ball, "FixedUpdate");
            Hit(first);
            Assert.AreEqual(3, Count);
        }

        [Test]
        public void OnlyARealBounceAndCurrentSameSideParticipantCanCount()
        {
            Bind(); var robot = Robot();
            Hit(robot, bounced: false); Assert.AreEqual(0, Count);
            Hit(robot); Assert.AreEqual(1, Count);
            Hit(Robot(BattleSide.Opponent)); Assert.AreEqual(1, Count);
            var dragged = Robot(); Entity entity; _registry.TryGet(dragged.InstanceId, out entity);
            ((RobotEntity)entity).TrySetDragging(true);
            Hit(dragged); Assert.AreEqual(1, Count);
            var stored = Robot(); _registry.TryGet(stored.InstanceId, out entity);
            ((RobotEntity)entity).TrySetLocation(RobotLocation.Storage, null);
            Hit(stored); Assert.AreEqual(1, Count);
            var removed = Robot(); _registry.Remove(removed.InstanceId, EntityRemovalReason.Consumed, out _);
            Hit(removed); Assert.AreEqual(1, Count);
            using (var oldRegistry = new BattleEntityRegistry())
            {
                Hit(Robot(registry: oldRegistry));
                Assert.AreEqual(1, Count, "其他注册表里相同实例ID不能混入本局");
            }
        }

        [Test]
        public void ForeignRegistryWithUnconsumedSameIdCannotCount()
        {
            Bind();
            var current = Robot();
            using (var foreignRegistry = new BattleEntityRegistry())
            {
                var foreign = Robot(registry: foreignRegistry);
                Assert.AreEqual(current.InstanceId, foreign.InstanceId);
                Hit(foreign); Assert.AreEqual(0, Count);
                Hit(current); Assert.AreEqual(1, Count);
            }
        }

        [Test]
        public void NewBattleRejectsOldViewAndRebindingRestoresExactEntityIdentity()
        {
            Bind(); var oldView = Robot(); Hit(oldView);
            using (var nextRegistry = new BattleEntityRegistry())
            {
                var current = Robot(registry: nextRegistry);
                Assert.AreEqual(oldView.InstanceId, current.InstanceId);
                _ball.BindBattle(2, BattleSide.Player, () => _running);
                _ball.BindCombo(nextRegistry, _popups); _ball.LaunchAt(Vector2.zero, 45f);
                Hit(oldView); Assert.AreEqual(0, Count, "新局不能接收旧局同ID视图");
                Entity oldEntity; _registry.TryGet(oldView.InstanceId, out oldEntity);
                current.Render((RobotEntity)oldEntity);
                Hit(current); Assert.AreEqual(0, Count, "视图中的实体引用必须与注册表一致");
                current.Bind(nextRegistry, current.InstanceId);
                Hit(current); Assert.AreEqual(1, Count, "显式重新绑定后才能接受当前实体");
            }
        }

        [Test]
        public void TimeoutBoundaryIsInclusiveAndNextEpisodeRestartsAtOne()
        {
            Bind(); Hit(Robot()); Advance(1.5f);
            Assert.AreEqual(1, Count);
            Hit(Robot()); Assert.AreEqual(2, Count);
            Advance(1.5001f); Assert.AreEqual(0, Count);
            Hit(Robot()); Assert.AreEqual(1, Count);
        }

        [Test]
        public void BerserkConsumesContactsWithoutBackfillingAndDoesNotAutomaticallyStartAtFifty()
        {
            Bind(); Hit(Robot());
            _ball.SetBerserk(true); Assert.AreEqual(0, Count);
            var touching = Robot(); Hit(touching);
            _ball.SetBerserk(false); Hit(touching); Assert.AreEqual(0, Count);
            for (var i = 0; i < 60; i++) Hit(Robot());
            Assert.AreEqual(60, Count);
            Assert.IsFalse(_ball.IsBerserk);
            Assert.AreEqual(5, _popups.childCount, "只显示10至50五档");
        }

        [Test]
        public void FrozenAndClosedGateNeitherCountNorConsumeTime()
        {
            Bind(); var touching = Robot(); Hit(touching); Advance(1f);
            _ball.SetFrozen(true); Advance(5f); Hit(Robot());
            Assert.AreEqual(1, Count);
            _ball.SetFrozen(false); Hit(touching); Assert.AreEqual(1, Count);
            _running = false; Advance(5f); Hit(Robot()); Assert.AreEqual(1, Count);
            _running = true; Advance(0.51f); Assert.AreEqual(0, Count);
        }

        [Test]
        public void MilestonesUseFiveColorsAndCanRepeatAfterTimeout()
        {
            Bind();
            var colors = new[] { Color.white, new Color(0.37f, 0.78f, 0.42f), new Color(0.28f, 0.57f, 0.95f),
                new Color(0.70f, 0.39f, 0.88f), new Color(1f, 0.76f, 0.20f) };
            for (var i = 1; i <= 50; i++)
            {
                Hit(Robot());
                if (i % 10 != 0) continue;
                var label = _popups.GetChild(i / 10 - 1).GetComponent<TextMesh>();
                Assert.AreEqual(i + " Combo", label.text);
                var expected = colors[i / 10 - 1];
                Assert.AreEqual(expected.r, label.color.r, 1f / 255f);
                Assert.AreEqual(expected.g, label.color.g, 1f / 255f);
                Assert.AreEqual(expected.b, label.color.b, 1f / 255f);
                Assert.AreEqual(expected.a, label.color.a, 1f / 255f);
            }
            Advance(1.51f);
            for (var i = 0; i < 10; i++) Hit(Robot());
            Assert.AreEqual(6, _popups.childCount);
            Assert.AreEqual("10 Combo", _popups.GetChild(5).GetComponent<TextMesh>().text);
        }

        [Test]
        public void PopupUsesFixedWorldOriginAndLinearlyRisesAndFades()
        {
            Bind(); for (var i = 0; i < 10; i++) Hit(Robot());
            var popup = _popups.GetChild(0); var label = popup.GetComponent<TextMesh>();
            Assert.That(Vector3.Distance(_ball.transform.position + Vector3.up * 0.35f, popup.position), Is.LessThan(0.0001f));
            var start = popup.position; _ball.transform.position += Vector3.right * 5f;
            var component = popup.GetComponent(PopupType());
            Assert.IsFalse((bool)Invoke(component, "Advance", 0.25f));
            Assert.That(Vector3.Distance(start + Vector3.up * 0.175f, popup.position), Is.LessThan(0.0001f));
            Assert.AreEqual(0.5f, label.color.a, 1f / 255f);
            Assert.IsTrue((bool)Invoke(component, "Advance", 0.25f));
            Assert.AreEqual(0f, label.color.a);
        }

        [TestCase("Stop")]
        [TestCase("LaunchAt")]
        [TestCase("BindBattle")]
        public void LifecycleResetClearsCountAndExistingPopups(string transition)
        {
            Bind(); for (var i = 0; i < 10; i++) Hit(Robot());
            Assert.AreEqual(1, _popups.childCount);
            if (transition == "Stop") _ball.Stop();
            else if (transition == "LaunchAt") _ball.LaunchAt(Vector2.zero, 45f);
            else _ball.BindBattle(2, BattleSide.Player, () => _running);
            Assert.AreEqual(0, Count);
            Assert.AreEqual(0, _popups.childCount);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTimeoutFailsDuringBinding(float timeout)
        {
            AssignPopup(); _ball.GlobalValueProvider = key => timeout;
            var error = Assert.Throws<TargetInvocationException>(() => Invoke(_ball, "BindCombo", _registry, _popups));
            Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
            Assert.IsFalse(_ball.IsLaunched);
        }

        [Test]
        public void MissingPopupFailsDuringBindingAndLegacyBallNeedsNoComboConfig()
        {
            var error = Assert.Throws<TargetInvocationException>(() => Invoke(_ball, "BindCombo", _registry, _popups));
            Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
            _ball.GlobalValueProvider = key => key == 34 ? throw new Exception("unexpected Combo config read") : 0f;
            _ball.LaunchAt(Vector2.zero, 45f);
            Assert.IsTrue(_ball.IsLaunched);
        }

        [Test]
        public void FiftyComboRequestsBerserkOnceAndKeepsMainDirection()
        {
            Bind();
            var direction = _ball.CurrentDirection;
            var requests = 0;
            var threshold = typeof(PhysicalCollisionBall).GetEvent("ComboThresholdReached");
            Assert.IsNotNull(threshold, "50 Combo需要通知暴走装配入口。");
            Action<PhysicalCollisionBall> enter = ball => { requests++; ball.SetBerserk(true); };
            threshold.AddEventHandler(_ball, enter);
            for (var i = 0; i < 50; i++) Hit(Robot());
            Assert.AreEqual(1, requests);
            Assert.AreEqual(0, Count);
            Assert.AreEqual(direction, _ball.CurrentDirection);
            for (var i = 0; i < 5; i++) Hit(Robot());
            Assert.AreEqual(1, requests);
            Assert.AreEqual(0, Count);
        }

        [Test]
        public void TemporaryBallReportsOneRobotContactWithoutComboBinding()
        {
            var bind = typeof(PhysicalCollisionBall).GetMethod("BindRobotContacts");
            Assert.IsNotNull(bind, "临时球不能依赖Combo装配才能触发机器人攻击。");
            bind.Invoke(_ball, new object[] { _registry });
            var contactEvent = typeof(PhysicalCollisionBall).GetEvent("RobotContactDetected");
            Assert.IsNotNull(contactEvent);
            var requests = 0;
            Action<PhysicalCollisionBall, int> attack = (ball, id) => requests++;
            contactEvent.AddEventHandler(_ball, attack);
            _ball.SetBerserk(true);
            _ball.LaunchAt(Vector2.zero, 90f);
            var view = Robot();
            Hit(view); Hit(view);
            Assert.AreEqual(1, requests);
            Assert.AreEqual(0, Count);
        }

        private int Count
        {
            get { var property = typeof(PhysicalCollisionBall).GetProperty("ComboCount"); Assert.IsNotNull(property, "ComboCount尚未实现"); return (int)property.GetValue(_ball); }
        }

        private void Bind() { AssignPopup(); Invoke(_ball, "BindCombo", _registry, _popups); _ball.LaunchAt(Vector2.zero, 45f); }
        private void AssignPopup()
        {
            var template = NewObject("ComboPopupTemplate");
            var popup = template.AddComponent(PopupType());
            Set(_ball, "_comboPopupPrefab", popup);
        }
        private static Type PopupType() { var type = typeof(PhysicalCollisionBall).Assembly.GetType("GameLogic.ComboPopupView"); Assert.IsNotNull(type, "Combo提示组件尚未实现"); return type; }
        private void Hit(RobotView view, Collider2D collider = null, bool bounced = true) => Invoke(_ball, "ProcessRobotCombo", view, collider != null ? collider : view.GetComponentInChildren<Collider2D>(), bounced);
        private void Advance(float seconds) => Invoke(_ball, "AdvanceComboTime", seconds);
        private RobotView Robot(BattleSide side = BattleSide.Player, BattleEntityRegistry registry = null)
        {
            registry = registry ?? _registry;
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/RobotCannon.prefab"));
            _objects.Add(go); go.transform.position = Vector3.right * (20f + _objects.Count * 2f);
            var view = go.GetComponent<RobotView>();
            var entity = registry.CreateRobot(501, side, 1, 1);
            entity.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)); view.Bind(registry, entity.InstanceId);
            return view;
        }
        private GameObject NewObject(string name) { var go = new GameObject(name); _objects.Add(go); return go; }
        private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); Assert.IsNotNull(field, name); field.SetValue(target, value);
        }
        private static object Invoke(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, name + "尚未实现"); return method.Invoke(target, args);
        }
    }
}
