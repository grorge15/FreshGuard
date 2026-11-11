using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class StaffRobotIntegrationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private EnemyBattleController Enemies => (EnemyBattleController)_f.Controller;
        private UniTaskCompletionSource<GameObject> _pending;
        private Transform _pendingParent;
        private bool _holdStaff;
        private int _staffLoads;
        [TearDown] public void Clear() { _combat?.Dispose(); _combat = null; _f?.Dispose(); _f = null; _views.Clear(); }
        private void Prepare(bool offsetRoutes = false)
        {
            _f = new EnemyBattleControllerTests.Fixture();
            if (offsetRoutes)
                foreach (var field in new[] { "_playerSpawnRadius", "_playerP1Radius", "_playerP2Radius" })
                    typeof(EnemyBattleController).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_f.Controller, .4f);
            _f.Prepare(); _f.Start();
            _combat = new RobotCombatController(_f.Host, _f.Context, Enemies, _views, Load);
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
        }
        private UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (name == "StaffBullet" && ++_staffLoads > 1 && _holdStaff)
            { _pendingParent = parent; _pending = new UniTaskCompletionSource<GameObject>(); return _pending.Task; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/bullets/" + name + ".prefab");
            Assert.IsNotNull(prefab, name);
            return UniTask.FromResult(UnityEngine.Object.Instantiate(prefab, parent));
        }
        private RobotEntity AddRobot(BattleSide side = BattleSide.Player)
        {
            var robot = _f.Context.Registry.CreateRobotFromConfig(_f.Tables.TbRobot.Get(702), side, 1);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var view = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/RobotRollingPin.prefab"), _f.Root.transform);
            view.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, view); _combat.SyncRobots(); return robot;
        }
        private void Shoot(RobotEntity robot)
        {
            _f.Context.Registry.AdvanceTime(1);
            Assert.IsTrue(_combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId));
        }
        private static AttackCycleBuffBehavior Counter(RobotEntity r) => (AttackCycleBuffBehavior)r.Buffs.Items.Single(b => b.Model.Id == 2603).Behavior;
        private EnemyEntity Target() { _f.Context.Registry.TryGet(_f.PlayerView.InstanceId, out var e); return (EnemyEntity)e; }
        private void Summon(RobotEntity r) { for (var i = 0; i < 5; i++) Shoot(r); }

        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void TenNormalAttacksKeepTheirBulletsAndSummonTwoIndependentStaffs(BattleSide side)
        {
            Prepare(); var robot = AddRobot(side);
            for (var i = 0; i < 10; i++)
            {
                Shoot(robot);
                Assert.AreEqual(i + 1, _combat.LiveBulletCount, "普通第5弹也必须保留");
                Assert.AreEqual((i + 1) / 5, Enemies.Staffs.Count);
                Assert.AreEqual((i + 1) % 5, Counter(robot).AttackCount);
            }
            Assert.IsTrue(Enemies.Staffs.States.All(s => s.Side == side && s.Elapsed == 0));
        }
        [Test]
        public void SeparateRobotsDoNotShareCountsAndCancelledCastDoesNotCount()
        {
            Prepare(); var a = AddRobot(); var b = AddRobot();
            a.Buffs.Add(new BuffModel(999, "test"), a.OwnerRef, 20, behavior: new CancelCast());
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, a.Side, a.InstanceId));
            Assert.AreEqual(0, Counter(a).AttackCount);
            a.Buffs.Remove(a.Buffs.Items.Single(x => x.Model.Id == 999));
            Shoot(a); Shoot(a); Shoot(b);
            Assert.AreEqual(2, Counter(a).AttackCount); Assert.AreEqual(1, Counter(b).AttackCount);
        }
        [Test]
        public void MissingReservedStaffRetainsFullCountUntilRetrySucceeds()
        {
            Prepare(); var a = AddRobot(); _holdStaff = true; Summon(a);
            Assert.AreEqual(1, Enemies.Staffs.Count); Assert.IsNotNull(_pending);
            Summon(a); Shoot(a);
            Assert.IsTrue(Counter(a).Pending); Assert.AreEqual(5, Counter(a).AttackCount);
            Assert.AreEqual(1, _f.Context.Battle.TriggeredSkills.Count);
            _holdStaff = false;
            _pending.TrySetResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/bullets/StaffBullet.prefab"), _pendingParent));
            _combat.AdvanceAttacks(0);
            Assert.AreEqual(2, Enemies.Staffs.Count); Assert.AreEqual(0, Counter(a).AttackCount);
        }
        [Test]
        public void StaffPushesWithoutDamageAndPauseStopsAllStaffTime()
        {
            Prepare(); var robot = AddRobot(); _f.Advance(1); var target = Target();
            var hp = target.CurrentHp; var before = target.RouteProgress; Summon(robot);
            var state = Enemies.Staffs.States.Single();
            _f.Host.SetFrozen(true); _f.Advance(2); Assert.AreEqual(0, state.Elapsed);
            _f.Host.SetFrozen(false); _f.Advance(1.1f);
            Assert.Less(target.RouteProgress, before); Assert.AreEqual(hp, target.CurrentHp, "Staff不结算伤害，也不推进普通弹");
            Assert.AreEqual(0, target.Buffs.Count);
        }
        [Test]
        public void RemovingRobotDoesNotCancelStaffAndEndingBattleClearsIt()
        {
            Prepare(); var robot = AddRobot(); Summon(robot);
            var state = Enemies.Staffs.States.Single();
            _f.Context.Registry.Remove(robot.InstanceId, EntityRemovalReason.Manual, out _);
            _combat.SyncRobots(); _f.Advance(.2f); Assert.Greater(state.Elapsed, 0);
            _f.Clear(); Assert.IsNull(Enemies.Staffs); Assert.IsEmpty(_f.Root.GetComponentsInChildren<StaffView>());
        }
        [Test]
        public void StaffEndInsideLargeFrameAllowsRemainingMovementAndMatchesSmallFrames()
        {
            Prepare(); var robot = AddRobot(); _f.Advance(1); var enemy = Target(); Summon(robot);
            _f.Advance(2.2f); var large = enemy.RouteProgress;
            Assert.AreEqual(0, Enemies.Staffs.Count);
            Clear(); Prepare(); robot = AddRobot(); _f.Advance(1); enemy = Target(); Summon(robot);
            for (var i = 0; i < 220; i++) _f.Advance(.01f);
            Assert.That(enemy.RouteProgress, Is.EqualTo(large).Within(.002f));
        }
        [Test]
        public void MultipleStaffsAtSameLocationDoNotAddDisplacement()
        {
            Prepare(); var a = AddRobot(); _f.Advance(1); var enemy = Target(); Summon(a);
            _f.Advance(1); var one = enemy.RouteProgress;
            Clear(); Prepare(); a = AddRobot(); var b = AddRobot(); _f.Advance(1); enemy = Target(); Summon(a); Summon(b);
            _f.Advance(1); Assert.That(enemy.RouteProgress, Is.EqualTo(one).Within(.0001f));
        }
        [Test]
        public void StaffSummonRechecksFrontmostInsteadOfLockedNormalTarget()
        {
            Prepare(); var robot = AddRobot(); _f.Advance(1);
            var old = Target(); Shoot(robot);
            Assert.IsTrue(Enemies.TryKnockback(BattleSide.Player, old.InstanceId, 1000));
            var front = _f.Views.Where(v => _f.Side(v) == BattleSide.Player).Select(v =>
            { _f.Context.Registry.TryGet(v.InstanceId, out var e); return (EnemyEntity)e; }).OrderByDescending(e => e.RouteProgress).First();
            Assert.AreNotEqual(old.InstanceId, front.InstanceId);
            for (var i = 0; i < 4; i++) Shoot(robot);
            Assert.That(Enemies.Staffs.States.Single().Progress,
                Is.EqualTo(front.RouteProgress + front.Configuration.HitRadius + .1f + .15f).Within(.0001f));
        }
        [Test]
        public void ZeroKnockbackReceivedRatioDoesNotReduceStaffPush()
        {
            Prepare(); var robot = AddRobot(); _f.Advance(1); var enemy = Target();
            typeof(GameConfig.enemy.Enemy).GetField("KnockbackResist").SetValue(enemy.Configuration, 0f);
            var before = enemy.RouteProgress; Summon(robot); _f.Advance(1.1f);
            Assert.Less(enemy.RouteProgress, before);
        }
        [Test]
        public void ReusedViewResetsAppearanceAndKeepsConfiguredWidth()
        {
            Prepare(); var robot = AddRobot(); Summon(robot);
            var view = _f.Root.GetComponentInChildren<StaffView>();
            _f.Advance(.3f);
            var renderer = view.GetComponentInChildren<SpriteRenderer>();
            // 第一段道路竖直，Staff横向宽度映射到世界X。
            Assert.That(renderer.bounds.size.x, Is.EqualTo(1.2f).Within(.001f));
            _f.Advance(1.6f); Assert.AreEqual(0, Enemies.Staffs.Count);
            Summon(robot); view = _f.Root.GetComponentInChildren<StaffView>();
            Assert.AreEqual(0, view.State.Elapsed); Assert.AreEqual(0, view.GetComponentInChildren<SpriteRenderer>().bounds.size.x);
        }
        [Test]
        public void UpgradeKeepsActiveSnapshotAndChildLevelFollowsRobot()
        {
            Prepare(); var robot = AddRobot(); Shoot(robot); Shoot(robot); Assert.IsTrue(robot.TryUpgrade());
            Assert.AreEqual(2, Counter(robot).AttackCount); Summon(robot);
            var state = Enemies.Staffs.States.Single();
            Assert.IsTrue(robot.Skills.TryGet(1601, out var child)); Assert.AreEqual(2, child.Level);
            Assert.AreEqual(1.2f, state.Config.RollDistance);
        }
        [Test]
        public void OppositeSideStaffDoesNotPreventNearEndEnemyFromArrivingAtZeroTimeBoundary()
        {
            Prepare(); var robot = AddRobot(); Summon(robot);
            var opponent = _f.Views.First(v => _f.Side(v) == BattleSide.Opponent);
            _f.Context.Registry.TryGet(opponent.InstanceId, out var entity);
            var enemy = (EnemyEntity)entity;
            var field = typeof(EnemyBattleController).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance);
            var active = (System.Collections.IDictionary)field.GetValue(Enemies);
            var entry = active[enemy.InstanceId];
            var points = (Vector3[])entry.GetType().GetField("Points", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
            var route = new BattleRouteGeometry(points);
            enemy.TrySetRouteProgress(route.Length - .000005f);
            opponent.transform.position = route.Position(enemy.RouteProgress);
            var arrived = (bool)typeof(EnemyBattleController).GetMethod("MoveWithStaffs", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(Enemies, new[] { entry, enemy, (object)0f });
            Assert.IsTrue(arrived, "零时步抵塔边界必须能够推进，不能被另一侧Staff卡死");
        }
        [Test]
        public void TurningStaffPushesEnemyBackThroughCornerAlongItsOwnOffsetRoute()
        {
            Prepare(true); var robot = AddRobot(); var enemy = Target();
            var id = enemy.InstanceId;
            var active = (System.Collections.IDictionary)typeof(EnemyBattleController)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Enemies);
            var entry = active[id];
            var points = (Vector3[])entry.GetType().GetField("Points", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
            var individual = new BattleRouteGeometry(points);
            var center = new BattleRouteGeometry(new[] { new Vector3(-3, 1), new Vector3(-3, 0), new Vector3(-1, 0), Vector3.zero });
            // 固定在转角后0.1单位，避免随机路线长度使Staff提前到出生端而影响本测试。
            enemy.TrySetRouteProgress(center.ConvertTo(individual, 1.1f));
            entry.GetType().GetField("NextPoint", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(entry, individual.Segment(enemy.RouteProgress));
            _f.PlayerView.transform.position = individual.Position(enemy.RouteProgress);
            var before = enemy.RouteProgress; Summon(robot); var state = Enemies.Staffs.States.Single();
            _f.Advance(1.25f);
            Assert.Less(state.Progress, 1, "Staff反向穿过首个转角");
            Assert.Less(enemy.RouteProgress, before);
            var view = _f.Views.Single(v => v.InstanceId == id);
            Assert.That(Vector3.Distance(individual.Position(enemy.RouteProgress), view.transform.position), Is.LessThan(.0001f));
            Assert.That(Mathf.Abs(view.transform.position.x + 3), Is.GreaterThan(.00001f), "保留个体横向偏移，不能吸到中心线");
            var staffView = _f.Root.GetComponentInChildren<StaffView>();
            Assert.IsNotNull(staffView);
            Assert.That(staffView.transform.position.x, Is.EqualTo(-3).Within(.0001f));
        }
        [Test]
        public void IsolatedEnemyStillGetsPushedWhileItsBuffExpiresNormally()
        {
            Prepare(); var robot = AddRobot(); _f.Advance(1); var enemy = Target();
            var before = enemy.RouteProgress; var speed = enemy.MoveSpeed;
            Summon(robot);
            _f.Context.Battle.AddBuff(enemy, new BuffModel(2600, "store_isolation"), robot.OwnerRef, 2);
            Assert.IsTrue(enemy.IsIsolated); _f.Advance(1.1f);
            Assert.Less(enemy.RouteProgress, before); Assert.IsTrue(enemy.IsIsolated); Assert.AreEqual(speed, enemy.MoveSpeed);
            _f.Advance(1.0f); Assert.IsFalse(enemy.IsIsolated); Assert.AreEqual(speed, enemy.MoveSpeed);
        }
        private sealed class CancelCast : BuffBehavior
        { public override void OnCast(BuffContainer c, BuffObj b, SkillCastContext x) => x.Cancel(); }
    }
}
