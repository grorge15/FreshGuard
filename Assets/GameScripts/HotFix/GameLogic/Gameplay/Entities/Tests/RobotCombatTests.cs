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
            Call(number, "Initialize", 20, Vector3.zero, Vector2.left);
            var label = go.GetComponent<TextMesh>();
            Assert.IsNotNull(label.font, "伤害数字必须有可显示的字体");
            Assert.AreEqual("FreshGuard/ComboOutlinedText", go.GetComponent<MeshRenderer>().sharedMaterial.shader.name);
            Assert.AreEqual(FontStyle.Bold, label.fontStyle);
            Assert.That((float)type.GetProperty("Lifetime").GetValue(number), Is.EqualTo(0.5f));
            Assert.IsFalse((bool)Call(number, "Advance", 0.25f));
            Assert.That(go.transform.position.y, Is.GreaterThan(0.20f), "前半段先向上弯出");
            Assert.That(go.transform.position.x, Is.LessThan(-0.18f), "伤害数字必须同时向外侧飘移");
            Assert.That(label.color.a, Is.EqualTo(0.5f).Within(1f / 255f));
            Assert.IsFalse((bool)Call(number, "Advance", 0.24f));
            Assert.IsTrue((bool)Call(number, "Advance", 0.01f));
            Assert.That(label.color.a, Is.EqualTo(0f).Within(0.0001f));
        }

        [TestCase(-1f, 0f)]
        [TestCase(1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(0f, -1f)]
        public void DamageNumberBendsBeforeStraighteningTowardTheOutside(float x, float y)
        {
            var go = new GameObject("CurvedDamageNumber");
            go.transform.SetParent(_fixture.Root.transform);
            var number = go.AddComponent<DamageNumberView>();
            var start = new Vector3(2f, 3f, 4f);
            var outward = new Vector2(x, y);
            number.Initialize(20, start, outward);
            Assert.That(Vector3.Distance(start, go.transform.position), Is.LessThan(0.04f), "起点只允许小幅随机偏移");
            var actualStart = go.transform.position;
            number.Advance(0.05f);
            var early = (Vector2)(go.transform.position - actualStart);
            Assert.Greater(Vector2.Dot(early, outward), 0f);
            Assert.Less(Vector2.Dot(early.normalized, outward), 0.75f, "起步应明显弯出，不能直线斜飘");
            number.Advance(0.40f);
            var beforeTail = go.transform.position;
            number.Advance(0.04f);
            var tailDirection = (Vector2)(go.transform.position - beforeTail);
            Assert.Greater(Vector2.Dot(tailDirection.normalized, outward), 0.99f, "末段应逐渐转为朝外侧平移");
            Assert.AreEqual(start.z, go.transform.position.z);
        }

        [TestCase(BattleSide.Player, false)]
        [TestCase(BattleSide.Player, true)]
        [TestCase(BattleSide.Opponent, false)]
        [TestCase(BattleSide.Opponent, true)]
        public void EnemyDamageStartsNearBodyCenterOutsideItsOwnBoardAndFreezes(BattleSide side, bool usePrefab)
        {
            var f = _fixture;
            var enemy = f.Views.First(v => f.Side(v) == side);
            var bounds = enemy.GetVisualBounds();
            var outward = side == BattleSide.Player ? Vector3.left : Vector3.right;
            var board = new GameObject("DamageNumberBoard").AddComponent<PlacementBoardView>();
            board.transform.SetParent(f.Root.transform);
            board.transform.position = bounds.center - outward * 2f;
            typeof(BattleShopSceneController).GetField(side == BattleSide.Player ? "_playerBoard" : "_opponentBoard",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(f.Host, board);
            if (usePrefab)
                typeof(EnemyBattleController).GetField("_damageNumberPrefab", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(f.Controller, UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/AssetRaw/Actor/DamageNumbers/Prefabs/DamageNumber.prefab"));
            Assert.IsTrue(((EnemyBattleController)f.Controller).TryPlayDamageFeedback(side, enemy.InstanceId, 20));
            var number = f.Root.GetComponentsInChildren<DamageNumberView>().Single();
            var start = number.transform.position;
            Assert.Greater(Vector3.Dot(start - bounds.center, outward), 0f, "出字点应偏向棋盘外侧");
            Assert.IsTrue(bounds.Contains(start), "出字点仍应在敌人身体中心附近");
            Assert.That(start.y, Is.EqualTo(bounds.center.y).Within(0.0251f));
            f.Host.SetFrozen(true);
            f.Advance(0.2f);
            Assert.AreEqual(start, number.transform.position);
            f.Host.SetFrozen(false);
            f.Advance(0.1f);
            Assert.Greater(Vector3.Dot(number.transform.position - start, outward), 0f);
            Assert.Greater(number.transform.position.y, start.y);
            f.Advance(0.4f);
            Assert.IsTrue(number == null, "动画结束后应沿现有链路回收");
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
