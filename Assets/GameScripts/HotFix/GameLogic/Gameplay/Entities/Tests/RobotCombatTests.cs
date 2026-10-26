using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class RobotCombatTests
    {
        private EnemyBattleControllerTests.Fixture _fixture;
        [SetUp] public void SetUp() { _fixture = new EnemyBattleControllerTests.Fixture(); _fixture.Prepare(); _fixture.Start(); }
        [TearDown] public void TearDown() { _fixture.Dispose(); }

        [Test]
        public void GeneratedGlobal15ContainsSixSecondRageDuration()
        {
            Assert.AreEqual(6f, _fixture.Tables.TbGlobal.Get(15).Value);
        }

        [Test]
        public void SharedTargetLocksUntilInvalidAndNeverCrossesBattleSides()
        {
            var f = _fixture;
            f.Advance(0.5f);
            var players = f.Views.Where(v => f.Side(v) == BattleSide.Player).OrderBy(v => v.InstanceId).ToArray();
            var selector = Create("SharedEnemyTargetSelector", f.Context, f.Controller);
            var first = players[0].InstanceId;
            var second = players[1].InstanceId;
            Assert.AreEqual(first, Call(selector, "GetTarget", BattleSide.Player));
            f.Context.Registry.TryGet(second, out var entity);
            ((EnemyEntity)entity).TrySetRouteProgress(100f);
            Assert.AreEqual(first, Call(selector, "GetTarget", BattleSide.Player), "超过锁定目标不能抢锁");
            f.Context.Registry.ApplyDamage(first, 100, out _);
            Assert.AreEqual(second, Call(selector, "GetTarget", BattleSide.Player));
            var opponentId = (int)Call(selector, "GetTarget", BattleSide.Opponent);
            f.Context.Registry.TryGet(opponentId, out entity);
            Assert.AreEqual(BattleSide.Opponent, entity.Side);
        }

        [Test]
        public void BulletTracksTheOriginalMovingTargetAndHitsOnlyOnce()
        {
            var f = _fixture;
            var target = f.PlayerView;
            var id = target.InstanceId;
            var start = target.transform.position + Vector3.left * 2f;
            var bullet = Bullet(id, 20, start);
            target.transform.position += Vector3.right;
            Assert.IsFalse((bool)Call(bullet, "Advance", 0.3f));
            Assert.That(Vector3.Distance(bullet.transform.position, new Vector3(-3.5f, 2f, 0f)), Is.LessThan(0.0001f));
            Assert.IsTrue((bool)Call(bullet, "Advance", 0.3f));
            Assert.IsTrue((bool)Call(bullet, "Advance", 1f));
            f.Context.Registry.TryGet(id, out var entity);
            Assert.AreEqual(80, ((EnemyEntity)entity).CurrentHp);
        }

        [Test]
        public void InvalidTargetReturnsBulletWithoutDamagingAPooledReplacement()
        {
            var f = _fixture;
            var id = f.PlayerView.InstanceId;
            var bullet = Bullet(id, 20, Vector3.zero);
            f.Context.Registry.ApplyDamage(id, 100, out _);
            var replacement = f.Context.Registry.CreateEnemyFromConfig(f.Tables.TbEnemy.Get(2001), BattleSide.Player);
            Assert.IsTrue((bool)Call(bullet, "Advance", 1f));
            f.Context.Registry.TryGet(replacement, out var entity);
            Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
        }

        [Test]
        public void FreezeStopsBulletFlightAndDamageWithoutCatchup()
        {
            var f = _fixture;
            var id = f.PlayerView.InstanceId;
            var bullet = Bullet(id, 20, Vector3.zero);
            f.Host.SetFrozen(true);
            Assert.IsFalse((bool)Call(bullet, "Advance", 10f));
            Assert.AreEqual(Vector3.zero, bullet.transform.position);
            f.Host.SetFrozen(false);
            Assert.IsFalse((bool)Call(bullet, "Advance", 0.3f));
            f.Context.Registry.TryGet(id, out var entity);
            Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
            Assert.IsTrue((bool)Call(bullet, "Advance", 0.3f));
            Assert.AreEqual(80, ((EnemyEntity)entity).CurrentHp);
        }

        [Test]
        public void KilledViewLeavesMovementButKeepsDeathUntilAnimationOrTimeout()
        {
            var f = _fixture;
            var view = f.PlayerView;
            var bullet = Bullet(view.InstanceId, 100, Vector3.zero);
            Call(bullet, "Advance", 0.6f);
            Assert.AreEqual("Death", view.AnimationState);
            Assert.AreEqual(1, f.LiveCount);
            f.Host.SetFrozen(true); f.Advance(10f);
            Assert.IsTrue(view != null);
            Assert.AreEqual(0f, view.GetComponent<Animator>().speed);
            f.Host.SetFrozen(false); f.Advance(3f);
            Assert.IsTrue(view == null, "动画缺少末帧事件也必须回收");
        }

        [Test]
        public void EachRobotKeepsItsOwnFsmAndCombatTicksNeverFire()
        {
            var registry = _fixture.Context.Registry;
            var robots = new[] { registry.CreateRobot(101, BattleSide.Player, 1, 1),
                registry.CreateRobot(101, BattleSide.Player, 1, 1) };
            foreach (var robot in robots)
            {
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
                Call(robot, "InitializeCombat", _fixture.Context.BattleId);
                Call(robot, "TickCombat", true);
                Assert.AreEqual("Attack", robot.CombatState);
                Assert.AreEqual(0f, robot.TriggerCooldownRemaining, "FSM 更新不能消费攻击触发");
            }
            TEngine.ModuleSystem.Update(10f, 10f);
            registry.AdvanceTime(1f);
            robots[0].TrySetDragging(true);
            foreach (var robot in robots) Call(robot, "TickCombat", true);
            Assert.AreEqual("Idle", robots[0].CombatState);
            Assert.AreEqual("Attack", robots[1].CombatState, "同类型实例的 FSM 应互相独立");
            Assert.IsTrue(robots.All(robot => robot.TriggerCooldownRemaining == 0f));
        }

        [Test]
        public void EqualProgressChoosesSmallerIdAndDeathCompletionRemainsIdempotent()
        {
            var f = _fixture;
            f.Advance(0.5f);
            var players = f.Views.Where(v => f.Side(v) == BattleSide.Player).OrderBy(v => v.InstanceId).ToArray();
            foreach (var view in players)
            {
                f.Context.Registry.TryGet(view.InstanceId, out var entity);
                ((EnemyEntity)entity).TrySetRouteProgress(1f);
            }
            var selector = new SharedEnemyTargetSelector(f.Context, (EnemyBattleController)f.Controller);
            Assert.AreEqual(players[0].InstanceId, selector.GetTarget(BattleSide.Player));
            var bullet = Bullet(players[0].InstanceId, 100, Vector3.zero);
            Call(bullet, "Advance", 0.6f);
            Assert.AreEqual(1, ((EnemyBattleController)f.Controller).DeathViewCount);
            players[0].OnDeathAnimationComplete();
            Assert.AreEqual(0, ((EnemyBattleController)f.Controller).DeathViewCount);
            Assert.AreEqual(players[1].InstanceId, selector.GetTarget(BattleSide.Player));
        }

        [Test]
        public void DeathFallbackStillRunsAfterAllWavesHaveFinished()
        {
            var f = _fixture;
            f.Advance(5f);
            foreach (var view in f.Views)
            {
                f.Context.Registry.ApplyDamage(view.InstanceId, 100, out var removal);
                ((EnemyBattleController)f.Controller).NotifyKilled(removal);
            }
            Assert.IsTrue(f.Completed);
            Assert.Greater(((EnemyBattleController)f.Controller).DeathViewCount, 0);
            f.Advance(3f);
            Assert.AreEqual(0, ((EnemyBattleController)f.Controller).DeathViewCount);
        }

        [Test]
        public void DamageNumberUsesHalfSecondLifetimeAndExpires()
        {
            var type = typeof(RobotEntity).Assembly.GetType("GameLogic.DamageNumberView");
            Assert.IsNotNull(type, "缺少 DamageNumberView");
            var go = new GameObject("DamageNumber");
            go.transform.SetParent(_fixture.Root.transform);
            var number = go.AddComponent(type);
            go.GetComponent<TextMesh>().font = null;
            go.GetComponent<MeshRenderer>().sharedMaterial = null;
            Call(number, "Initialize", 20, Vector3.zero);
            var label = go.GetComponent<TextMesh>();
            Assert.IsNotNull(label.font, "伤害数字必须有可显示的字体");
            Assert.AreSame(label.font.material, go.GetComponent<MeshRenderer>().sharedMaterial);
            Assert.That((float)type.GetProperty("Lifetime").GetValue(number), Is.EqualTo(0.5f));
            Assert.IsFalse((bool)Call(number, "Advance", 0.25f));
            Assert.That(go.transform.position.y, Is.EqualTo(0.175f).Within(0.0001f));
            Assert.That(label.color.a, Is.EqualTo(0.5f).Within(1f / 255f));
            Assert.IsFalse((bool)Call(number, "Advance", 0.24f));
            Assert.IsTrue((bool)Call(number, "Advance", 0.01f));
            Assert.That(label.color.a, Is.EqualTo(0f).Within(0.0001f));
        }

        private Component Bullet(int targetId, int damage, Vector3 start)
        {
            var type = typeof(RobotEntity).Assembly.GetType("GameLogic.RobotBulletView");
            Assert.IsNotNull(type, "缺少独立子弹飞行与命中链路");
            var go = new GameObject("TestBullet"); go.transform.SetParent(_fixture.Root.transform);
            var bullet = go.AddComponent(type);
            var enemies = (EnemyBattleController)_fixture.Controller;
            Assert.IsTrue(enemies.TryGetTargetPosition(BattleSide.Player, targetId, out var aimPoint));
            var robot = _fixture.Context.Registry.CreateRobot(101, BattleSide.Player, 1, 1);
            var action = _fixture.Context.Combat.CaptureAttack(new CombatActionSnapshot(_fixture.Context.BattleId,
                robot.InstanceId, robot.Side, robot.SkillId, new[] { targetId }, CombatOutcomeTag.SingleDamage,
                damage, start, aimPoint, sequence: 1));
            Call(bullet, "Initialize", _fixture.Host, _fixture.Context, _fixture.Controller, action,
                0.6f, 1f);
            return bullet;
        }

        private static object Create(string name, params object[] args)
        {
            var type = typeof(RobotEntity).Assembly.GetType("GameLogic." + name);
            Assert.IsNotNull(type, "缺少" + name);
            return Activator.CreateInstance(type, args);
        }

        private static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "缺少" + name);
            try { return method.Invoke(target, args); }
            catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
    }
}
