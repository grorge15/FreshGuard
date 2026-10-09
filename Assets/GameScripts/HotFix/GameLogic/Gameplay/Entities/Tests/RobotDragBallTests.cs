using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class RobotDragBallTests
    {
        private const float Tolerance = 0.0001f;
        private GameObject _root;
        private PhysicalCollisionBall _ball;
        private Rigidbody2D _body;
        private bool _running;
        private float _multiplier;

        [SetUp]
        public void SetUp()
        {
            // 先关闭自动发射，再激活对象，让真实 Rigidbody2D 进入物理世界。
            _root = new GameObject("RobotDragBallTest");
            _root.SetActive(false);
            _ball = _root.AddComponent<PhysicalCollisionBall>();
            SetField("launchOnEnable", false);
            SetField("speed", 7f);
            _root.SetActive(true);
            // EditMode 可能不派发普通 MonoBehaviour 的 Awake；仅在未初始化时补一次。
            var bodyField = typeof(PhysicalCollisionBall).GetField("_body", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(bodyField);
            if (bodyField.GetValue(_ball) == null) InvokePrivate("Awake");
            _body = _root.GetComponent<Rigidbody2D>();
            Assert.IsTrue(_root.activeInHierarchy, "真实速度测试必须使用已激活的 Rigidbody2D。");
            Assert.IsNotNull(_body);
            _multiplier = 0.7f;
            _running = true;
            _ball.GlobalValueProvider = key => key == 17 ? _multiplier : 0f;
            _ball.BindBattle(1, BattleSide.Player, () => _running);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void SetDraggingHasPublicBoolContractAndEffectiveSpeedIsReadOnly()
        {
            var method = DraggingMethod();
            Assert.AreEqual(typeof(void), method.ReturnType);
            var property = typeof(PhysicalCollisionBall).GetProperty("EffectiveSpeed", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, "能源球必须提供只读 EffectiveSpeed。");
            Assert.AreEqual(typeof(float), property.PropertyType);
            Assert.IsFalse(property.CanWrite);
        }

        [Test]
        public void SetDraggingUpdatesBodyVelocityImmediatelyAndPreservesBaseSpeed()
        {
            Launch();
            AssertSpeed(7f);
            SetDragging(true);
            AssertSpeed(4.9f);
            Assert.AreEqual(7f, _ball.Speed, "拖拽倍率不能改写基础 Speed。");
            Assert.AreEqual(4.9f, EffectiveSpeed(), Tolerance);
            Assert.That(Vector2.Distance(_body.velocity.normalized, _ball.CurrentDirection), Is.LessThan(Tolerance));
            SetDragging(false);
            AssertSpeed(7f);
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
        }

        [Test]
        public void RepeatedDraggingCallsNeverCompoundMultiplier()
        {
            Launch();
            SetDragging(true);
            SetDragging(true);
            SetDragging(true);
            AssertSpeed(4.9f);
            InvokePrivate("FixedUpdate");
            AssertSpeed(4.9f);
            SetDragging(false);
            SetDragging(false);
            AssertSpeed(7f);
        }

        [Test]
        public void BerserkIgnoresDraggingAndSwitchesVelocityImmediately()
        {
            Launch();
            SetDragging(true);
            AssertSpeed(4.9f);
            _ball.SetBerserk(true);
            AssertSpeed(7f);
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
            SetDragging(false);
            SetDragging(true);
            InvokePrivate("FixedUpdate");
            AssertSpeed(7f);
            _ball.SetBerserk(false);
            AssertSpeed(4.9f);
        }

        [Test]
        public void DraggingBeforeLaunchUsesReducedSpeedWithoutStartingBall()
        {
            SetDragging(true);
            _ball.SetBerserk(true);
            _ball.SetBerserk(false);
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);
            Launch();
            AssertSpeed(4.9f);
        }

        [Test]
        public void FrozenBallStaysStoppedUntilUnfrozenThenUsesCurrentEffectiveSpeed()
        {
            Launch();
            _ball.SetFrozen(true);
            SetDragging(true);
            _ball.SetBerserk(true);
            _ball.SetBerserk(false);
            InvokePrivate("FixedUpdate");
            AssertStopped();
            _ball.SetFrozen(false);
            AssertSpeed(4.9f);
            _ball.SetFrozen(true);
            SetDragging(false);
            AssertStopped();
            _ball.SetFrozen(false);
            AssertSpeed(7f);
        }

        [Test]
        public void StopClearsDraggingAndStateSettersCannotReviveBall()
        {
            Launch();
            SetDragging(true);
            _ball.Stop();
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
            SetDragging(false);
            _ball.SetBerserk(true);
            _ball.SetBerserk(false);
            _ball.SetFrozen(false);
            InvokePrivate("FixedUpdate");
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);
            Launch();
            AssertSpeed(7f);
            _ball.Stop();
            SetDragging(true);
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);
        }

        [Test]
        public void BindBattleClearsDraggingBeforeNextLaunch()
        {
            Launch();
            SetDragging(true);
            _ball.BindBattle(2, BattleSide.Player, () => _running);
            AssertStopped();
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
            Launch();
            AssertSpeed(7f);
        }

        [Test]
        public void ClosedRunningGatePreventsSettersAndUnfreezeFromStartingBall()
        {
            _running = false;
            SetDragging(true);
            _ball.SetBerserk(true);
            _ball.SetBerserk(false);
            _ball.SetFrozen(false);
            Launch();
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);

            _running = true;
            Launch();
            _running = false;
            SetDragging(false);
            AssertStopped();
            _ball.SetBerserk(true);
            AssertStopped();

            _running = true;
            Launch();
            _ball.SetFrozen(true);
            _running = false;
            SetDragging(false);
            _ball.SetBerserk(true);
            _ball.SetFrozen(false);
            AssertStopped();
        }

        [TestCase(0f)]
        [TestCase(-0.7f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidDraggingMultiplierFallsBackToBaseVelocity(float multiplier)
        {
            Launch();
            SetDragging(true);
            AssertSpeed(4.9f);
            _multiplier = multiplier;
            SetDragging(true);
            AssertSpeed(7f);
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
            InvokePrivate("FixedUpdate");
            AssertSpeed(7f);
        }

        [TestCase(1f, 7f)]
        [TestCase(0.5f, 3.5f)]
        public void ValidDraggingMultiplierUsesGlobal17(float multiplier, float expectedSpeed)
        {
            _multiplier = multiplier;
            SetDragging(true);
            Launch();
            AssertSpeed(expectedSpeed);
            Assert.AreEqual(expectedSpeed, EffectiveSpeed(), Tolerance);
        }

        [TestCase("moving")]
        [TestCase("unlaunched")]
        [TestCase("frozen")]
        [TestCase("closedGate")]
        public void DraggingValidatesProviderBeforeCommittingEvenWhenBallCannotMove(string state)
        {
            if (state != "unlaunched") Launch();
            if (state == "frozen") _ball.SetFrozen(true);
            if (state == "closedGate") _running = false;
            FailDragProvider();

            ExpectProviderError();
            Assert.DoesNotThrow(() => SetDragging(true));
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance, "失败后不能留下拖拽标记并再次读取损坏配置。");

            RestoreDragProvider();
            _running = true;
            _ball.SetFrozen(false);
            AssertStopped();
            Launch();
            AssertSpeed(7f, "修复配置后必须显式发射，且不保留失败的拖拽状态。");
        }

        [Test]
        public void ProviderFailureDuringFixedUpdateStopsOnceAndClearsDragging()
        {
            Launch();
            SetDragging(true);
            AssertSpeed(4.9f);
            FailDragProvider();
            ExpectProviderError();
            Assert.DoesNotThrow(() => InvokePrivate("FixedUpdate"));
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
            Assert.DoesNotThrow(() => InvokePrivate("FixedUpdate"));
            Assert.DoesNotThrow(() => InvokePrivate("FixedUpdate"));
            LogAssert.NoUnexpectedReceived();

            RestoreDragProvider();
            Launch();
            AssertSpeed(7f);
        }

        [TestCase("berserkExit")]
        [TestCase("unfreeze")]
        public void ProviderFailureDuringImmediateVelocityUpdateStopsAndDoesNotEscape(string transition)
        {
            Launch();
            SetDragging(true);
            if (transition == "berserkExit") _ball.SetBerserk(true);
            else _ball.SetFrozen(true);
            FailDragProvider();

            ExpectProviderError();
            Assert.DoesNotThrow(() =>
            {
                if (transition == "berserkExit") _ball.SetBerserk(false);
                else _ball.SetFrozen(false);
            });
            AssertStopped();
            Assert.IsFalse(_ball.IsLaunched);
            Assert.AreEqual(7f, EffectiveSpeed(), Tolerance);
            _ball.SetFrozen(false);
            AssertStopped();

            RestoreDragProvider();
            Launch();
            AssertSpeed(7f);
        }

        [Test]
        public void EndingDraggingDoesNotReadFailedProviderAndRestoresBaseVelocity()
        {
            Launch();
            SetDragging(true);
            FailDragProvider();
            Assert.DoesNotThrow(() => SetDragging(false));
            Assert.IsTrue(_ball.IsLaunched);
            AssertSpeed(7f);
            LogAssert.NoUnexpectedReceived();
        }

        private void FailDragProvider()
        {
            _ball.GlobalValueProvider = key => key == 17
                ? throw new KeyNotFoundException("Global17 missing") : 0f;
        }

        private void RestoreDragProvider() => _ball.GlobalValueProvider = key => key == 17 ? _multiplier : 0f;

        private static void ExpectProviderError() => LogAssert.Expect(LogType.Error,
            "能源球停止，请检查配置后调用 Launch 重试：Global17 missing");

        private static MethodInfo DraggingMethod()
        {
            var method = typeof(PhysicalCollisionBall).GetMethod("SetDragging", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(bool) }, null);
            Assert.IsNotNull(method, "能源球必须提供 public void SetDragging(bool dragging)。");
            return method;
        }

        private void SetDragging(bool dragging) => DraggingMethod().Invoke(_ball, new object[] { dragging });

        private float EffectiveSpeed()
        {
            var property = typeof(PhysicalCollisionBall).GetProperty("EffectiveSpeed", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, "能源球必须提供只读 EffectiveSpeed。");
            return (float)property.GetValue(_ball);
        }

        private void Launch() => _ball.LaunchAt(Vector2.zero, 45f);
        private void AssertSpeed(float expected, string message = null) => Assert.AreEqual(expected, _body.velocity.magnitude, Tolerance, message);

        private void AssertStopped()
        {
            AssertSpeed(0f);
            Assert.IsFalse(_body.simulated, "冻结、停止或 gate 关闭时不得开启物理模拟。");
        }

        private void SetField(string name, object value)
        {
            var field = typeof(PhysicalCollisionBall).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(_ball, value);
        }

        private void InvokePrivate(string name)
        {
            var method = typeof(PhysicalCollisionBall).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(_ball, null);
        }
    }
}
