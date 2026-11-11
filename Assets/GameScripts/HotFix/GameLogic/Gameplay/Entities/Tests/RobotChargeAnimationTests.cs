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
    public sealed class RobotChargeAnimationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();

        [SetUp]
        public void Prepare()
        {
            _f = new EnemyBattleControllerTests.Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            _f.Prepare(); _f.Start();
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, _views, Load);
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
            var layout = _f.Root.AddComponent<BattleScreenLayoutController>();
            Set(layout, "_playerBounds", new Bounds(Vector3.zero, new Vector3(20, 20, 1)));
            Set(layout, "_opponentBounds", new Bounds(Vector3.right * 10, new Vector3(20, 20, 1)));
            Set(_f.Host, "_screenLayout", layout);
        }

        [TearDown]
        public void Clear() { _combat?.Dispose(); _f?.Dispose(); _views.Clear(); }

        [TestCase(704, 12, 18)]
        [TestCase(701, 4, 6)]
        public void LaunchedAttackChangesSegmentAndEachSegmentKeepsLooping(int configId, int startFrame, int middleFrame)
        {
            var robot = AddRobot(configId);
            var renderer = AnimatedPortrait(robot);
            var frames = Frames(configId);
            Assert.AreSame(frames[0], renderer.sprite);
            _combat.AdvanceExistingBullets(.1f);
            Assert.AreNotSame(frames[0], renderer.sprite, "无攻击时当前阶段也应持续播放");
            Assert.IsTrue(Fire(robot));
            Assert.AreSame(frames[startFrame], renderer.sprite, "成功发射应立即切到下一阶段起点");
            _combat.AdvanceExistingBullets(.09f);
            Assert.AreSame(frames[middleFrame], renderer.sprite);
            _combat.AdvanceExistingBullets(.09f);
            Assert.AreSame(frames[startFrame], renderer.sprite, "段内循环，不自动跳到下一阶段");
            Assert.AreEqual(1, Counter(robot).AttackCount);
        }

        [TestCase(704)] [TestCase(701)]
        public void FreezeAndDraggingPauseAnimationAndUpgradeResetsCounterAndDefaultSegment(int configId)
        {
            var robot = AddRobot(configId); var renderer = AnimatedPortrait(robot);
            Assert.IsTrue(Fire(robot)); _combat.AdvanceExistingBullets(.05f);
            var paused = renderer.sprite;
            _f.Host.SetFrozen(true); _combat.AdvanceExistingBullets(2);
            Assert.AreSame(paused, renderer.sprite);
            Assert.IsFalse(Fire(robot));
            _f.Host.SetFrozen(false);
            robot.TrySetDragging(true); View(robot).Render(robot);
            _combat.AdvanceExistingBullets(.08f);
            Assert.IsFalse(renderer.enabled, "拖拽不能被动画采样重新显示");
            robot.TrySetDragging(false); View(robot).Render(robot);
            Assert.AreSame(paused, renderer.sprite, "放回原阶段，不补播拖拽时间");
            Assert.IsTrue(robot.TryUpgrade()); View(robot).Render(robot);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.IsFalse(Counter(robot).Pending);
            Assert.AreSame(Frames(configId)[0], renderer.sprite);
            Assert.IsTrue(Fire(robot));
            Assert.AreEqual(1, Counter(robot).AttackCount);
        }

        [TestCase(704)] [TestCase(701)]
        public void AttackHookCancellationBeforeLaunchDoesNotAdvanceCharge(int configId)
        {
            var robot = AddRobot(configId);
            robot.Buffs.Add(new BuffModel(999, "test"), robot.OwnerRef, 10,
                behavior: new CancelBeforeLaunch(_f.Context.Battle));
            Assert.IsTrue(Fire(robot), "施法可以被接受，但实际发弹被钩子取消");
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0, Counter(robot).AttackCount, "只按成功发射计数");
            Assert.AreSame(Frames(configId)[0], AnimatedPortrait(robot).sprite);
        }

        [TestCase(704, 3)] [TestCase(701, 5)]
        public void CompletedCycleResetsToFirstSegmentAndTwoRobotsStayIndependent(int configId, int required)
        {
            var first = AddRobot(configId); var second = AddRobot(configId);
            var secondRenderer = AnimatedPortrait(second);
            for (var i = 0; i < required; i++)
            {
                _f.Context.Registry.AdvanceTime(1);
                Assert.IsTrue(Fire(first));
            }
            Assert.AreEqual(0, Counter(first).AttackCount);
            Assert.AreEqual(0, Counter(second).AttackCount);
            // 电磁炉先播放施法动画；结束后才重新显示累计动画默认段。
            _f.Context.Battle.Timelines.Cancel(first.OwnerRef);
            _combat.AdvanceExistingBullets(0);
            Assert.AreSame(Frames(configId)[0], AnimatedPortrait(first).sprite);
            Assert.AreSame(Frames(configId)[0], secondRenderer.sprite);
            Assert.IsTrue(Fire(second));
            Assert.AreEqual(1, Counter(second).AttackCount);
            Assert.AreEqual(0, Counter(first).AttackCount);
        }

        [TestCase(704)] [TestCase(701)]
        public void PendingChildPreservesEverySuccessfullyLaunchedAttack(int configId)
        {
            var robot = AddRobot(configId);
            var required = configId == 704 ? 3 : 5;
            robot.Buffs.Add(new BuffModel(998, "test", priority: 1000), robot.OwnerRef, 100,
                behavior: new ChildCastHook(robot, false));
            for (var i = 0; i < required + 1; i++)
            { _f.Context.Registry.AdvanceTime(1); Assert.IsTrue(Fire(robot)); }
            Assert.IsTrue(Counter(robot).Pending);
            Assert.AreEqual(required + 1, Counter(robot).AttackCount);
            robot.Buffs.Remove(robot.Buffs.Items.Single(b => b.Model.Id == 998));
            _f.Context.Battle.TriggeredSkills.Process();
            Assert.AreEqual(1, Counter(robot).AttackCount, "待发子技能恢复后保留额外发射的进度");
        }

        [TestCase(704)] [TestCase(701)]
        public void UpgradeDuringChildCastCannotConsumeResetProgressOrEmitOldChild(int configId)
        {
            var robot = AddRobot(configId);
            robot.Buffs.Add(new BuffModel(998, "test", priority: -1000), robot.OwnerRef, 100,
                behavior: new ChildCastHook(robot, true));
            var required = configId == 704 ? 3 : 5;
            for (var i = 0; i < required; i++)
            { _f.Context.Registry.AdvanceTime(1); Assert.IsTrue(Fire(robot)); }
            Assert.AreEqual(2, robot.Level);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.IsFalse(Counter(robot).Pending);
            Assert.AreEqual(required, _combat.LiveBulletCount, "升级前的普通弹仍有效，旧子技能不能开始");
            Assert.IsFalse(_f.Context.Battle.Timelines.HasChannel(robot.OwnerRef));
        }

        [TestCase(704)] [TestCase(701)]
        public void AnimatedPortraitMatchesAuthoredCanvasAndKeepsCastAnimationExclusive(int configId)
        {
            var robot = AddRobot(configId); var renderer = AnimatedPortrait(robot);
            var original = _views[robot.InstanceId].transform.Find("RobotPortrait").GetComponent<SpriteRenderer>();
            Assert.That(Vector3.Distance(original.bounds.center, renderer.bounds.center), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(original.bounds.size, renderer.bounds.size), Is.LessThan(.001f));
            Assert.IsFalse(original.enabled, "累计动画与静态立绘不能重叠显示");
            if (configId != 701) return;
            for (var i = 0; i < 5; i++) { _f.Context.Registry.AdvanceTime(1); Assert.IsTrue(Fire(robot)); }
            Assert.IsTrue(_views[robot.InstanceId].GetComponent<RobotCastAnimationView>().IsPlaying);
            Assert.IsTrue(original.enabled);
            Assert.IsFalse(renderer.enabled, "电磁炉施法动画期间让出立绘表现");
            _f.Context.Battle.Timelines.Cancel(robot.OwnerRef);
            _combat.AdvanceExistingBullets(0);
            Assert.IsFalse(original.enabled);
            Assert.IsTrue(renderer.enabled);
        }

        private RobotEntity AddRobot(int id)
        {
            var config = _f.Tables.TbRobot.Get(id);
            var robot = _f.Context.Registry.CreateRobotFromConfig(config, BattleSide.Player);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/" + config.RobotPrefab + ".prefab"), _f.Root.transform);
            go.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            Assert.IsTrue(go.transform.Find("RobotPortrait").GetComponent<SpriteRenderer>().enabled,
                "战斗绑定前保留静态立绘");
            _views.Add(robot.InstanceId, go); _combat.SyncRobots();
            return robot;
        }
        private RobotView View(RobotEntity robot) => _views[robot.InstanceId].GetComponent<RobotView>();
        private SpriteRenderer AnimatedPortrait(RobotEntity robot)
        {
            var visual = _views[robot.InstanceId].transform.Find("RobotPortrait/ChargeAnimation");
            Assert.IsNotNull(visual, "缺少接线后的累计分段动画");
            return visual.GetComponent<SpriteRenderer>();
        }
        private static Sprite[] Frames(int id)
        {
            var path = id == 704 ? "RobotJuicer/JuicerAccumulate" : "RobotInductionCooker/InductionCookerAccumulate";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AssetRaw/Actor/Animations/Robots/" + path + ".anim");
            return AnimationUtility.GetObjectReferenceCurve(clip, AnimationUtility.GetObjectReferenceCurveBindings(clip)[0])
                .Select(key => (Sprite)key.value).ToArray();
        }
        private static AttackCycleBuffBehavior Counter(RobotEntity robot) =>
            (AttackCycleBuffBehavior)robot.Buffs.Items.Single(b => b.Behavior is AttackCycleBuffBehavior).Behavior;
        private bool Fire(RobotEntity robot) => _combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        private static UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/bullets/" + name + ".prefab"), parent));
        }
        private sealed class CancelBeforeLaunch : BuffBehavior
        {
            private readonly BattleRuntime _battle;
            internal CancelBeforeLaunch(BattleRuntime battle) { _battle = battle; }
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft a) => _battle.Timelines.Cancel(c.Owner);
        }
        private sealed class ChildCastHook : BuffBehavior
        {
            private readonly RobotEntity _robot;
            private readonly bool _upgrade;
            internal ChildCastHook(RobotEntity robot, bool upgrade) { _robot = robot; _upgrade = upgrade; }
            public override void OnCast(BuffContainer c, BuffObj b, SkillCastContext cast)
            {
                if (cast.Request.Trigger != SkillTriggerSource.Buff) return;
                if (_upgrade) _robot.TryUpgrade(); else cast.Cancel();
            }
        }
    }
}
