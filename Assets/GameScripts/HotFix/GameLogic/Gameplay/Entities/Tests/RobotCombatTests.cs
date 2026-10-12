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
        public void EachRobotHasItsOwnFsmAndOnlyExplicitCombatTicksFire()
        {
            var registry = _fixture.Context.Registry;
            var robots = new[] { registry.CreateRobot(101, BattleSide.Player, 1, 1),
                registry.CreateRobot(101, BattleSide.Player, 1, 1) };
            var fired = 0;
            Func<RobotEntity, bool> fire = robot =>
            {
                if (!robot.TryConsumeAttackTrigger(1f)) return false;
                fired++; return true;
            };
            foreach (var robot in robots)
            {
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
                Call(robot, "InitializeCombat", _fixture.Context.BattleId);
                Call(robot, "TickCombat", true, fire);
                Call(robot, "TickCombat", true, fire);
            }
            Assert.AreEqual(2, fired, "同类型实例应独立攻击，冷却阻止重复触发");
            TEngine.ModuleSystem.Update(10f, 10f);
            Assert.AreEqual(2, fired, "框架自动更新不能绕过固定战斗顺序发射");
            registry.AdvanceTime(1f);
            robots[0].TrySetDragging(true);
            foreach (var robot in robots) Call(robot, "TickCombat", true, fire);
            Assert.AreEqual(3, fired, "拖拽只停止自己的新攻击");
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

        private Component Bullet(int targetId, int damage, Vector3 start)
        {
            var type = typeof(RobotEntity).Assembly.GetType("GameLogic.RobotBulletView");
            Assert.IsNotNull(type, "缺少独立子弹飞行与命中链路");
            var go = new GameObject("TestBullet"); go.transform.SetParent(_fixture.Root.transform);
            var bullet = go.AddComponent(type);
            Call(bullet, "Initialize", _fixture.Host, _fixture.Context, _fixture.Controller, BattleSide.Player,
                targetId, damage, start, 0.6f, 1f, 1L);
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
