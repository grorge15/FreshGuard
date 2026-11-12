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
            // 使用不同型号检验共享资源路径；701已有专属Prefab，同ID会走原配置快速路径。
            var incompatible = (GameConfig.robot.Robot)typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(config, null);
            typeof(GameConfig.robot.Robot).GetField(nameof(config.Id)).SetValue(incompatible, config.Id + 10000);
            typeof(GameConfig.robot.Robot).GetField(nameof(config.RobotPrefab)).SetValue(incompatible,
                differentResource ? config.RobotPrefab + "_other" : config.RobotPrefab);
            typeof(GameConfig.robot.Robot).GetField(nameof(config.ShapeId)).SetValue(incompatible,
                differentResource ? config.ShapeId : config.ShapeId + 1);
            Assert.IsFalse(view.TryUseConfiguration(incompatible, _f.Tables));
            Assert.AreEqual(original, view.ConfigId);
            Assert.AreEqual(0, view.InstanceId);
            Assert.IsTrue(view.TryUseConfiguration(config, _f.Tables), "拒绝不兼容配置后仍应允许合法的701共享配置");
            Assert.AreEqual(701, view.ConfigId);
        }

        [Test]
        public void FifthNormalAttackRemainsAndQueuedChildWaitsForCastAnimation()
        {
            Prepare(); var robot = AddRobot();
            for (var i = 0; i < 4; i++) Shoot(robot).Cancel();
            var portrait = _views[robot.InstanceId].transform.Find("RobotPortrait").GetComponent<SpriteRenderer>();
            var original = portrait.sprite;
            var before = States();
            _f.Context.Registry.AdvanceTime(1);
            Assert.IsTrue(Contact(robot));
            var normal = States().Except(before).Single();
            Assert.AreEqual(1605, normal.Action.SkillId, "第五次普通攻击保留");
            Assert.AreEqual(0, Counter(robot).AttackCount, "接触入口已接受额外子技能并扣除次数，发弹仍需等待前摇");
            _f.Context.Battle.TriggeredSkills.Process();
            Assert.IsTrue(_f.Context.Battle.Timelines.HasChannel(robot.OwnerRef), "子技能占用独立前摇通道");
            Assert.IsFalse(States().Any(b => b.Action.SkillId == 1600));
            normal.Cancel(); // 单独验证闪电链前摇，避免普通弹提前击杀目标。
            Assert.AreNotSame(original, portrait.sprite, "前摇应显示技能序列帧");
            var pausedFrame = portrait.sprite;
            _f.Host.SetFrozen(true); _combat.AdvanceExistingBullets(1);
            Assert.AreSame(pausedFrame, portrait.sprite, "暂停不得推进前摇动画");
            _f.Host.SetFrozen(false);
            _combat.AdvanceExistingBullets(.125f); _f.Context.Battle.Timelines.Advance(.125f);
            Assert.IsFalse(States().Any(b => b.Action.SkillId == 1600));
            Assert.AreNotSame(pausedFrame, portrait.sprite);
            _combat.AdvanceExistingBullets(.125f); _f.Context.Battle.Timelines.Advance(.125f);
            var shot = States().Single(b => b.Action.SkillId == 1600);
            Assert.AreEqual(1600, shot.Action.SkillId);
            Assert.AreEqual(3600, shot.Launcher.Model.Id);
            Assert.AreEqual(0, shot.Elapsed, "新弹不能多飞一整帧");
            Assert.IsFalse(_f.Context.Battle.Timelines.HasChannel(robot.OwnerRef));
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.AreSame(original, portrait.sprite, "发弹后恢复人像");
        }

        [Test]
        public void CancelledWindupRestoresPortraitAndNeverEmitsDeferredBullet()
        {
            Prepare(); var robot = AddRobot();
            for (var i = 0; i < 4; i++) Shoot(robot).Cancel();
            var portrait = _views[robot.InstanceId].transform.Find("RobotPortrait").GetComponent<SpriteRenderer>();
            var original = portrait.sprite;
            _f.Context.Registry.AdvanceTime(1);
            Assert.IsTrue(Contact(robot));
            _f.Context.Battle.TriggeredSkills.Process();
            var before = States();
            _f.Context.Battle.Timelines.Cancel(robot.OwnerRef);
            Assert.AreSame(original, portrait.sprite);
            _combat.AdvanceExistingBullets(1); _f.Context.Battle.Timelines.Advance(1);
            Assert.IsEmpty(States().Except(before));
            Assert.AreEqual(0, Counter(robot).AttackCount, "已接受的第五次取消后不退还次数");
            Assert.AreEqual(3605, Shoot(robot).Launcher.Model.Id);
        }

        [Test]
        public void EndingCombatDuringWindupRestoresPortraitAndReleasesDeferredShot()
        {
            Prepare(); var robot = AddRobot();
            for (var i = 0; i < 4; i++) Shoot(robot).Cancel();
            var portrait = _views[robot.InstanceId].transform.Find("RobotPortrait").GetComponent<SpriteRenderer>();
            var original = portrait.sprite;
            _f.Context.Registry.AdvanceTime(1);
            Assert.IsTrue(Contact(robot));
            _f.Context.Battle.TriggeredSkills.Process();
            Assert.AreNotSame(original, portrait.sprite);
            _combat.Dispose(); _combat = null;
            Assert.AreSame(original, portrait.sprite);
            Assert.IsFalse(_f.Context.Battle.Timelines.HasChannel(robot.OwnerRef));
            _f.Context.Battle.Timelines.Advance(1);
            Assert.IsEmpty(States());
            Assert.AreEqual(0, _f.Context.Battle.Bullets.Count);
        }

        [Test]
        public void UnpreparedChildKeepsFifthNormalAndWaitsWithoutRepeatingIt()
        {
            Prepare(); var robot = AddRobot();
            for (var i = 0; i < 4; i++) Shoot(robot).Cancel();
            var animation = _views[robot.InstanceId].GetComponent<RobotCastAnimationView>();
            animation.enabled = false;
            _f.Context.Registry.AdvanceTime(1);
            var before = States();
            Assert.IsTrue(Contact(robot));
            var normal = States().Except(before).Single();
            Assert.AreEqual(1605, normal.Action.SkillId);
            _f.Context.Battle.TriggeredSkills.Process();
            Assert.AreEqual(5, Counter(robot).AttackCount);
            Assert.IsTrue(Counter(robot).Pending);
            animation.enabled = true;
            _f.Context.Battle.TriggeredSkills.Process();
            _f.Context.Battle.Timelines.Advance(.25f);
            var emitted = States().Except(before).ToArray();
            Assert.AreEqual(2, emitted.Length);
            Assert.AreEqual(1, emitted.Count(b => b.Action.SkillId == 1605));
            Assert.AreEqual(1, emitted.Count(b => b.Action.SkillId == 1600));
            Assert.AreEqual(0, Counter(robot).AttackCount);
        }

        [Test]
        public void TwoCyclesEmitTenNormalShotsAndTwoAdditionalChainShots()
        {
            Prepare(); var robot = AddRobot(); var normal = 0; var chain = 0;
            for (var i = 0; i < 10; i++)
            {
                _f.Context.Registry.AdvanceTime(1); var before = States();
                Assert.IsTrue(Contact(robot));
                _f.Context.Battle.TriggeredSkills.Process();
                _f.Context.Battle.Timelines.Advance(.25f);
                var emitted = States().Except(before).ToArray();
                Assert.AreEqual(i % 5 == 4 ? 2 : 1, emitted.Length);
                normal += emitted.Count(b => b.Action.SkillId == 1605);
                chain += emitted.Count(b => b.Action.SkillId == 1600);
                foreach (var shot in emitted) Assert.AreEqual(20, shot.Action.BaseDamage);
            }
            Assert.AreEqual(10, normal); Assert.AreEqual(2, chain);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.AreEqual(12, _combat.LiveBulletCount);
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

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EveryInductionHitConnectsLaunchOriginAndKeepsDamageAndEffectLifetime(bool chain, bool singleTarget)
        {
            Prepare(); var robot = AddRobot();
            if (singleTarget)
            {
                var distant = _f.Views.Where(v => _f.Side(v) == BattleSide.Player)
                    .OrderBy(v => v.InstanceId).Last();
                distant.transform.position = Vector3.up * 8;
            }
            if (chain) for (var i = 0; i < 4; i++) Shoot(robot).Cancel();
            var shot = Shoot(robot);
            var target = _f.Views.Single(v => v.InstanceId == shot.Action.TargetInstanceIds[0]);
            var hitPosition = target.transform.position;
            // 连线起点使用发射快照，不随机器人在弹丸飞行期间移动。
            _views[robot.InstanceId].transform.position += Vector3.right * 2;
            var damage = new List<int>();
            _f.Context.Combat.TargetResolved += c => damage.Add(c.Result.RequestedDamage);
            _combat.AdvanceExistingBullets(1);
            CollectionAssert.AreEqual(chain && !singleTarget ? new[] { 20, 14 } : new[] { 20 }, damage);
            var effect = _f.Root.GetComponentsInChildren<LightningChainEffectView>().Single();
            var segments = effect.transform.Find("ChainSegments").GetComponentsInChildren<SpriteRenderer>()
                .OrderBy(r => r.name).ToArray();
            Assert.AreEqual(chain && !singleTarget ? 2 : 1, segments.Length);
            Assert.That(Vector3.Distance(shot.Action.Origin, segments[0].transform.position), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(hitPosition, segments[0].transform.TransformPoint(Vector3.right * 4)), Is.LessThan(.001f));
            _combat.AdvanceExistingBullets(0);
            Assert.IsTrue(effect.gameObject.activeInHierarchy, "战斗暂停时保留闪电");
            _combat.AdvanceExistingBullets(1);
            Assert.IsFalse(effect.gameObject.activeInHierarchy, "表现播放结束后回收");
            Assert.AreEqual(chain && !singleTarget ? 2 : 1, damage.Count, "表现不得再次造成伤害");
        }

        [Test]
        public void RejectedCastAndAttackHookCancellationDoNotCountBeforeLaunch()
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
        public void UpgradeResetsProgressAndInFlightChainDamageAndParametersStayFrozen()
        {
            Prepare(); var robot = AddRobot();
            SkillCastContext mutable = null;
            robot.Buffs.Add(new BuffModel(405, "test"), robot.OwnerRef, 100,
                behavior: new Hooks { Cast = c => { c.Parameters["chainTargetCount"] = 2; mutable = c; } });
            for (var i = 0; i < 3; i++) Shoot(robot).Cancel();
            Assert.AreEqual(3, Counter(robot).AttackCount);
            Assert.IsTrue(robot.TryUpgrade());
            Assert.AreEqual(0, Counter(robot).AttackCount);
            for (var i = 0; i < 4; i++)
            { var normal = Shoot(robot); Assert.AreEqual(30, normal.Action.BaseDamage); normal.Cancel(); }
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
            _f.Context.Battle.TriggeredSkills.Process();
            if (_f.Context.Battle.Timelines.HasChannel(robot.OwnerRef))
            {
                // 发射次数测试只推进施法，避免前几枚测试弹在前摇内提前击杀唯一目标。
                // 战斗分段与动画采样由独立前摇测试覆盖。
                _f.Context.Battle.Timelines.Advance(.25f);
            }
            var emitted = States().Except(before).ToArray();
            var child = emitted.SingleOrDefault(b => b.Action.SkillId == 1600);
            if (child == null) return emitted.Single();
            // 命中测试只推进目标弹；完整并行发射数量由TwoCycles测试覆盖。
            foreach (var ordinary in emitted.Where(b => b != child)) ordinary.Cancel();
            return child;
        }
        private bool Contact(RobotEntity robot)
        { _combat.SyncRobots(); return _combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId); }
        private BulletState[] States() => _f.Root.GetComponentsInChildren<RobotBulletView>()
            .Where(v => v.State != null).Select(v => v.State).ToArray();
        private static AttackCycleBuffBehavior Counter(RobotEntity robot) =>
            (AttackCycleBuffBehavior)robot.Buffs.Items.Single(b => b.Model.Id == 2602).Behavior;
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
