using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class ChainRobotIntegrationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private Dictionary<int, GameObject> _views;
        private Action _onBulletActivated;

        [TearDown]
        public void Clear() { _onBulletActivated = null; _combat?.Dispose(); _combat = null; _f?.Dispose(); _f = null; }

        [Test]
        public void BulletActivationCancellingCastStillCountsRegisteredShotAndKeepsItInFlight()
        {
            Prepare(); var robot = AddRobot(); var activations = 0;
            // 预检和待发实例加载结束后才启用，避免把资源准备误当作发射。
            _onBulletActivated = () =>
            {
                activations++;
                Assert.AreEqual(1, Counter(robot).AttackCount, "激活回调之前必须先提交发射计数");
                _f.Context.Battle.Timelines.Cancel(robot.OwnerRef);
            };
            var shot = Shoot(robot);
            Assert.AreEqual(1, activations);
            Assert.AreEqual(1, Counter(robot).AttackCount);
            Assert.AreEqual(0, _f.Context.Battle.Timelines.Count);
            Assert.AreEqual(1, _combat.LiveBulletCount);
            Assert.IsFalse(shot.IsResolved);
            _combat.AdvanceExistingBullets(0.01f);
            Assert.That(shot.Elapsed, Is.GreaterThan(0));
            Assert.IsFalse(shot.IsResolved);
            Assert.AreEqual(1, _combat.LiveBulletCount);
            var hits = 0;
            _f.Context.Combat.TargetResolved += c => hits++;
            _combat.AdvanceExistingBullets(1);
            Assert.AreEqual(1, hits);
            Assert.AreEqual(1, Counter(robot).AttackCount);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SharedPrefabRejectsDifferentResourceOrShapeWithoutChangingIdentity(bool differentResource)
        {
            Prepare();
            var config = _f.Tables.TbRobot.Get(701);
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/" + config.RobotPrefab + ".prefab"), _f.Root.transform);
            var view = instance.GetComponent<RobotView>();
            var original = view.ConfigId;
            var data = new ByteBuf();
            // 使用不同型号检验共享资源路径；701已有专属Prefab，同ID会走原配置快速路径。
            data.WriteInt(config.Id + 10000); data.WriteString(config.RobotName); data.WriteInt(config.QualityId);
            data.WriteString(differentResource ? config.RobotPrefab + "_other" : config.RobotPrefab);
            data.WriteString(config.RobotIcon); data.WriteInt(differentResource ? config.ShapeId : config.ShapeId + 1);
            data.WriteInt(config.SkillId); data.WriteFloat(config.MinTriggerInterval);
            data.WriteInt(config.AttackDamage); data.WriteInt(config.DamagePerLevel);
            var incompatible = new GameConfig.robot.Robot(data);
            Assert.IsFalse(view.TryUseConfiguration(incompatible, _f.Tables));
            Assert.AreEqual(original, view.ConfigId);
            Assert.AreEqual(0, view.InstanceId);
            Assert.IsTrue(view.TryUseConfiguration(config, _f.Tables), "拒绝不兼容配置后仍应允许合法的701共享配置");
            Assert.AreEqual(701, view.ConfigId);
        }

        [Test]
        public void RealControllerEmitsTwoCompleteFourDirectOneChainCycles()
        {
            Prepare(); var robot = AddRobot(); var ids = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                var shot = Shoot(robot);
                ids.Add(shot.Launcher.Model.Id);
                Assert.AreEqual(i % 5 == 4 ? "chain" : "direct", shot.Launcher.Model.BehaviorKey);
                Assert.AreEqual(20, shot.Action.BaseDamage);
            }
            CollectionAssert.AreEqual(new[] { 3605, 3605, 3605, 3605, 3600, 3605, 3605, 3605, 3605, 3600 }, ids);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.AreEqual(10, _combat.LiveBulletCount);
        }

        [Test]
        public void RealControllerKeepsBothRobotsCountersIndependent()
        {
            Prepare(); var first = AddRobot(); var second = AddRobot();
            for (var i = 0; i < 4; i++) Shoot(first);
            Assert.AreEqual(3605, Shoot(second).Launcher.Model.Id);
            Assert.AreEqual(3600, Shoot(first).Launcher.Model.Id);
            Assert.AreEqual(0, Counter(first).AttackCount);
            Assert.AreEqual(1, Counter(second).AttackCount);
            Assert.AreEqual(3605, Shoot(second).Launcher.Model.Id);
            Assert.AreEqual(2, Counter(second).AttackCount);
        }

        [Test]
        public void DisabledViewAndAttackHookCancellationDoNotCountAsLaunches()
        {
            Prepare(); var robot = AddRobot();
            _views[robot.InstanceId].SetActive(false);
            Assert.IsFalse(Contact(robot));
            Assert.AreEqual(0, Counter(robot).AttackCount);
            _views[robot.InstanceId].SetActive(true);
            robot.Buffs.Add(new BuffModel(404, "test"), robot.OwnerRef, 10,
                behavior: new Hooks { Attack = () => _f.Context.Battle.Timelines.Cancel(robot.OwnerRef) });
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            robot.Buffs.Remove(robot.Buffs.Items.Single(b => b.Model.Id == 404));
            Assert.AreEqual(3605, Shoot(robot).Launcher.Model.Id);
            Assert.AreEqual(1, Counter(robot).AttackCount);
        }

        [Test]
        public void MissedStraightShotStillAdvancesTheCycle()
        {
            Prepare(); var robot = AddRobot(); var shot = Shoot(robot);
            foreach (var enemy in _f.Views.Where(v => _f.Side(v) == BattleSide.Player))
                enemy.transform.position = Vector3.up * 8;
            var hits = 0;
            _f.Context.Combat.TargetResolved += c => hits++;
            _combat.AdvanceExistingBullets(2);
            Assert.IsTrue(shot.IsResolved);
            Assert.AreEqual(0, hits);
            Assert.AreEqual(1, Counter(robot).AttackCount);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            for (var i = 0; i < 3; i++) Assert.AreEqual(3605, Shoot(robot).Launcher.Model.Id);
            Assert.AreEqual(3600, Shoot(robot).Launcher.Model.Id);
        }

        [Test]
        public void UpgradeKeepsProgressAndInFlightChainDamageAndParametersStayFrozen()
        {
            Prepare(); var robot = AddRobot();
            SkillCastContext mutable = null;
            robot.Buffs.Add(new BuffModel(405, "test"), robot.OwnerRef, 100,
                behavior: new Hooks { Cast = c => { c.Parameters["chainTargetCount"] = 2; mutable = c; } });
            for (var i = 0; i < 3; i++) Shoot(robot).Cancel();
            Assert.AreEqual(3, Counter(robot).AttackCount);
            Assert.IsTrue(robot.TryUpgrade());
            Assert.AreEqual(3, Counter(robot).AttackCount);
            var fourth = Shoot(robot); Assert.AreEqual(30, fourth.Action.BaseDamage); fourth.Cancel();
            var chain = Shoot(robot);
            Assert.AreEqual(3600, chain.Launcher.Model.Id);
            Assert.AreEqual(30, chain.Action.BaseDamage);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            // 修改原上下文并再次升级；旧弹仍保留两目标、0.9距离、0.7倍率与30伤害。
            mutable.Parameters["chainTargetCount"] = 1;
            mutable.Parameters["chainRange"] = 0.01f;
            mutable.Parameters["chainDamageRatio"] = 0.1f;
            mutable.Parameters["damage"] = 999;
            Assert.IsTrue(robot.TryUpgrade());
            var next = Shoot(robot); Assert.AreEqual(40, next.Action.BaseDamage); next.Cancel();
            var damage = new List<int>();
            _f.Context.Combat.TargetResolved += c => damage.Add(c.Result.RequestedDamage);
            _combat.AdvanceExistingBullets(1);
            CollectionAssert.AreEqual(new[] { 30, 21 }, damage);
        }

        private void Prepare()
        {
            _f = new EnemyBattleControllerTests.Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 2) }));
            _f.Prepare(); _f.Start(); _f.Advance(0.5f);
            // 两名敌人相邻0.6单位，避免依赖随机出生或路线运动。
            foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
            {
                var enemies = _f.Views.Where(v => _f.Side(v) == side).OrderBy(v => v.InstanceId).ToArray();
                Assert.AreEqual(2, enemies.Length);
                for (var i = 0; i < enemies.Length; i++)
                    enemies[i].transform.position = new Vector3(-3, 1 + 0.6f * i, 0) +
                        (side == BattleSide.Player ? Vector3.zero : Vector3.right * 10);
            }
            _views = new Dictionary<int, GameObject>();
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, _views, Load);
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
            Set(_f.Host, "_combat", _combat);
            var layout = _f.Root.AddComponent<BattleScreenLayoutController>();
            Set(layout, "_playerBounds", new Bounds(Vector3.zero, new Vector3(20, 20, 1)));
            Set(layout, "_opponentBounds", new Bounds(Vector3.right * 10, new Vector3(20, 20, 1)));
            Set(_f.Host, "_screenLayout", layout);
        }
        private RobotEntity AddRobot()
        {
            var config = _f.Tables.TbRobot.Get(701);
            var robot = _f.Context.Registry.CreateRobotFromConfig(config, BattleSide.Player, 1);
            Assert.IsInstanceOf<InductionRobotEntity>(robot);
            Assert.IsTrue(robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)));
            var view = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/" + config.RobotPrefab + ".prefab"), _f.Root.transform);
            var robotView = view.GetComponent<RobotView>();
            Assert.IsTrue(robotView.TryUseConfiguration(config, _f.Tables));
            robotView.Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, view); _combat.SyncRobots();
            return robot;
        }
        private BulletState Shoot(RobotEntity robot)
        {
            _f.Context.Registry.AdvanceTime(1);
            var before = States();
            Assert.IsTrue(Contact(robot));
            return States().Except(before).Single();
        }
        private bool Contact(RobotEntity robot)
        { _combat.SyncRobots(); return _combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId); }
        private BulletState[] States() => _f.Root.GetComponentsInChildren<RobotBulletView>()
            .Where(v => v.State != null).Select(v => v.State).ToArray();
        private static ChainCycleBuffBehavior Counter(RobotEntity robot) =>
            (ChainCycleBuffBehavior)robot.Buffs.Items.Single(b => b.Model.Id == 2602).Behavior;
        private UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/bullets/" + name + ".prefab");
            Assert.IsNotNull(prefab, "缺少测试使用的弹丸或连线Prefab：" + name);
            var instance = UnityEngine.Object.Instantiate(prefab, parent);
            if (instance.GetComponent<RobotBulletView>() != null)
            {
                var probe = instance.AddComponent<ChainLaunchActivationProbe>();
                probe.Activated = () =>
                {
                    if (instance.GetComponent<RobotBulletView>().State != null) _onBulletActivated?.Invoke();
                };
            }
            return UniTask.FromResult(instance);
        }
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        private sealed class Hooks : BuffBehavior
        {
            internal Action Attack;
            internal Action<SkillCastContext> Cast;
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft d) => Attack?.Invoke();
            public override void OnCast(BuffContainer c, BuffObj b, SkillCastContext cast) => Cast?.Invoke(cast);
        }
    }

    [ExecuteAlways]
    public sealed class ChainLaunchActivationProbe : MonoBehaviour
    {
        internal Action Activated;
        private void OnEnable() => Activated?.Invoke();
    }
}
