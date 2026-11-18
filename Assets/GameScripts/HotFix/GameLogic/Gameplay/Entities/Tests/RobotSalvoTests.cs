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
    public sealed class RobotSalvoTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private Dictionary<int, GameObject> _views;

        [TearDown]
        public void Clear() { _combat?.Dispose(); _combat = null; _f?.Dispose(); _f = null; }

        [Test]
        public void BurstLargeFrameKeepsFourEmissionTimesAndCorrectFlightAges()
        {
            var robot = Prepare(504);
            var emitted = new List<float>();
            robot.Buffs.Add(new BuffModel(401, "test"), robot.OwnerRef, 10,
                behavior: new AttackHook(() => emitted.Add(_f.Context.Combat.BattleTime)));
            Assert.IsTrue(Contact(robot));
            _f.Host.AdvanceBattle(0.6f);
            Assert.AreEqual(4, emitted.Count);
            for (var i = 0; i < emitted.Count; i++) Assert.That(emitted[i], Is.EqualTo(i * 0.2f).Within(0.00001));
            var ages = LiveBullets().Select(b => b.Elapsed).OrderBy(v => v).ToArray();
            Assert.AreEqual(3, ages.Length);
            for (var i = 0; i < ages.Length; i++) Assert.That(ages[i], Is.EqualTo(i * 0.2f).Within(0.00001));
        }

        [Test]
        public void ScatterCyclesOneTwoThreeWithThirtyDegreeTwoBeamSpread()
        {
            var robot = Prepare(502);
            robot.Buffs.Add(new BuffModel(406, "test"), robot.OwnerRef, 10,
                behavior: new CastHook(c => c.Parameters["damage"] = 0));
            foreach (var count in new[] { 1, 2, 3, 1, 2, 3 })
            {
                Assert.IsTrue(Contact(robot));
                var states = LiveBullets().Select(b => b.State).ToArray();
                Assert.AreEqual(count, states.Length);
                var center = (states[0].Action.AimPoint - states[0].Action.Origin).normalized;
                var angle = count == 2 ? 30f : 60f;
                for (var i = 0; i < count; i++)
                    Assert.That(Vector3.SignedAngle(center, states[i].Direction, Vector3.forward),
                        Is.EqualTo(count == 1 ? 0 : -angle / 2f + angle * i / (count - 1)).Within(0.0001));
                _combat.AdvanceExistingBullets(0.7f);
                _f.Context.Registry.AdvanceTime(1f);
            }
        }

        [Test]
        public void BurstWithoutReplacementCancelsRemainingShotsWithoutCatchup()
        {
            var robot = Prepare(504);
            Assert.IsTrue(Contact(robot));
            foreach (var view in _f.Views)
            {
                _f.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal);
                ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            }
            _f.Host.AdvanceBattle(0.2f);
            Assert.AreEqual(0, _f.Context.Battle.Timelines.Count);
            _f.Host.AdvanceBattle(0.6f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
        }

        [Test]
        public void PauseDoesNotAdvanceBurstOrAlreadyLaunchedBullet()
        {
            var robot = Prepare(504);
            Assert.IsTrue(Contact(robot));
            _f.Host.SetFrozen(true);
            _f.Host.AdvanceBattle(10f);
            Assert.AreEqual(1, _combat.LiveBulletCount);
            Assert.AreEqual(0, LiveBullets()[0].Elapsed);
            _f.Host.SetFrozen(false);
            _f.Host.AdvanceBattle(0.2f);
            Assert.AreEqual(2, _combat.LiveBulletCount);
        }

        [TestCase(501)] [TestCase(502)] [TestCase(504)]
        public void AttackHookCancellingTimelineCannotLaunchAReleasedReservation(int configId)
        {
            var robot = Prepare(configId);
            robot.Buffs.Add(new BuffModel(402, "test"), robot.OwnerRef, 10,
                behavior: new AttackHook(() => _f.Context.Battle.Timelines.Cancel(robot.OwnerRef)));
            Assert.DoesNotThrow(() => Contact(robot));
            Assert.AreEqual(0, _combat.LiveBulletCount, "取消的节点不能把已归还的表现重新发射");
            robot.Buffs.Clear();
            _f.Context.Registry.AdvanceTime(configId == 502 ? 1f : 0.2f);
            Assert.IsTrue(Contact(robot));
            var count = 1;
            Assert.AreEqual(count, _combat.LiveBulletCount);
            Assert.AreEqual(count, LiveBullets().Length);
        }

        [Test]
        public void ReplacementCastCannotMakeCancelledOldBurstEmitItsCurrentShot()
        {
            var robot = Prepare(504);
            var calls = 0;
            var target = _f.PlayerView.InstanceId;
            robot.Buffs.Add(new BuffModel(403, "test"), robot.OwnerRef, 10,
                behavior: new AttackHook(() =>
                {
                    if (++calls != 2) return;
                    _f.Context.Battle.Timelines.Cancel(robot.OwnerRef);
                    Assert.IsTrue(_f.Context.Battle.Skills.TryCast(new SkillCastRequest(robot.OwnerRef, robot.SkillId,
                        SkillTriggerSource.Contact, SkillTarget.Entity(robot.Side, target))).Accepted);
                }));
            Assert.IsTrue(Contact(robot));
            _f.Host.AdvanceBattle(0.2f);
            Assert.AreEqual(2, _combat.LiveBulletCount, "仅保留旧第一发与替代施法第一发，不得继续发出取消的旧第二发");
        }

        [Test]
        public void BurstReacquiresDeadTargetWithoutChangingAlreadyLaunchedShot()
        {
            var robot = Prepare(504);
            _f.Advance(0.5f);
            Assert.IsTrue(Contact(robot));
            var first = LiveBullets().Single();
            var victim = first.TargetInstanceId;
            _f.Context.Registry.ApplyDamage(victim, int.MaxValue, out var removal);
            ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            _f.Host.AdvanceBattle(0.2f);
            Assert.AreEqual(2, LiveBullets().Length);
            Assert.AreEqual(victim, first.TargetInstanceId);
            Assert.AreNotEqual(victim, LiveBullets().Single(b => b != first).TargetInstanceId);
        }

        [Test]
        public void SeparateScatterBeamsStackButDoNotRepeatDamageDuringTheirAnimation()
        {
            var robot = Prepare(502);
            robot.Buffs.Add(new BuffModel(404, "test"), robot.OwnerRef, 10,
                behavior: new CastHook(c => { c.Parameters["scatterAngle"] = 0; }));
            var target = _f.PlayerView.InstanceId;
            Assert.IsTrue(Contact(robot));
            _combat.AdvanceExistingBullets(0.1f);
            Assert.IsTrue(_f.Context.Registry.TryGet(target, out var enemy));
            Assert.AreEqual(80, ((EnemyEntity)enemy).CurrentHp);
            _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(80, ((EnemyEntity)enemy).CurrentHp, "亮束与消散期间不得重复扣血。");
            _f.Context.Registry.AdvanceTime(1f);
            Assert.IsTrue(Contact(robot));
            var states = LiveBullets().Select(b => b.State).ToArray();
            Assert.AreEqual(2, states.Length);
            Assert.IsTrue(states.All(b => Vector3.Distance(b.Direction, states[0].Direction) < 0.00001));
            _combat.AdvanceExistingBullets(0.7f);
            Assert.AreEqual(40, ((EnemyEntity)enemy).CurrentHp, "两束20伤害应叠加为40。");
            _f.Context.Registry.AdvanceTime(1f);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(3, LiveBullets().Length);
            _combat.AdvanceExistingBullets(0.1f);
            Assert.IsFalse(_f.Context.Registry.TryGet(target, out _));
        }

        [Test]
        public void ScatterCancellationAndCooldownDoNotConsumeTheNextStage()
        {
            var robot = Prepare(502);
            var cancel = true;
            robot.Buffs.Add(new BuffModel(407, "test"), robot.OwnerRef, 10,
                behavior: new CastHook(c => { if (cancel) c.Cancel(); c.Parameters["damage"] = 0; }));
            Assert.IsFalse(Contact(robot));
            cancel = false;
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(1, LiveBullets().Length);
            Assert.IsFalse(Contact(robot));
            _combat.AdvanceExistingBullets(0.7f);
            _f.Context.Registry.AdvanceTime(1f);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(2, LiveBullets().Length);
        }

        [Test]
        public void ScatterUpgradePreservesCooldownAndStageWithoutChangingFiredBeam()
        {
            var robot = Prepare(502);
            Assert.IsTrue(Contact(robot));
            var first = LiveBullets().Single();
            Assert.IsTrue(robot.TryUpgrade());
            Assert.IsFalse(Contact(robot), "升级不能绕过激光的一秒冷却。");
            Assert.AreEqual(20, first.Damage, "已发射光束保留发射时的伤害。");
            _combat.AdvanceExistingBullets(0.7f);
            _f.Context.Registry.AdvanceTime(1f);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(2, LiveBullets().Length, "升级保留下一次两束的阶段。");
            Assert.IsTrue(LiveBullets().All(b => b.Damage == 30));
        }

        [Test]
        public void ScatterRequiresOneSecondBetweenTriggersAndPauseDoesNotAdvanceCooldown()
        {
            var robot = Prepare(502);
            Assert.IsTrue(robot.Skills.TryGet(robot.SkillId, out var skill));
            Assert.AreEqual(1f, skill.Model.Cooldown);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(1f, skill.CooldownRemaining);
            _f.Host.SetFrozen(true);
            _f.Host.AdvanceBattle(5f);
            Assert.AreEqual(1f, skill.CooldownRemaining, "暂停期间不减少技能冷却。");
            _f.Host.SetFrozen(false);
            _f.Host.AdvanceBattle(0.75f);
            Assert.IsFalse(Contact(robot));
            Assert.IsFalse(Contact(robot));
            Assert.AreEqual(0.25f, skill.CooldownRemaining, "冷却中的碰撞不延长等待时间。");
            _f.Host.AdvanceBattle(0.25f);
            Assert.IsTrue(Contact(robot), "累计满一秒后允许下一次碰撞发射。");
            Assert.AreEqual(2, LiveBullets().Length, "冷却中的碰撞不推进数量循环。");
        }

        [Test]
        public void ScatterStagesAreIndependentAndSurviveDragging()
        {
            var first = Prepare(502);
            var second = AddRobot(502);
            Assert.IsTrue(Contact(first));
            Assert.IsTrue(Contact(second));
            Assert.AreEqual(2, LiveBullets().Length, "两台机器人的首次攻击各发一束。");
            Assert.IsTrue(first.TrySetDragging(true));
            Assert.IsFalse(Contact(first));
            Assert.IsTrue(first.TrySetDragging(false));
            Assert.IsFalse(Contact(first), "拖拽后仍须等待原有冷却。");
            _f.Context.Registry.AdvanceTime(1f);
            Assert.IsTrue(Contact(first));
            Assert.AreEqual(4, LiveBullets().Length);
            Assert.IsTrue(Contact(second));
            Assert.AreEqual(6, LiveBullets().Length);
            var newlyBought = AddRobot(502);
            Assert.IsTrue(Contact(newlyBought));
            Assert.AreEqual(7, LiveBullets().Length, "新机器人从一束开始。");
        }

        [Test]
        public void SingleScatterPelletDoesNotDivideByZero()
        {
            var robot = Prepare(502);
            robot.Buffs.Add(new BuffModel(405, "test"), robot.OwnerRef, 10,
                behavior: new CastHook(c => c.Parameters["scatterCount"] = 1));
            Assert.IsTrue(Contact(robot));
            var bullet = LiveBullets().Single();
            Assert.That(Vector3.Distance(bullet.State.Direction, (bullet.Action.AimPoint - bullet.Action.Origin).normalized), Is.LessThan(0.00001));
        }

        private RobotEntity Prepare(int configId)
        {
            _f = new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            _views = new Dictionary<int, GameObject>();
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, _views,
                (name, parent, token) => UniTask.FromResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/AssetRaw/Actor/Prefabs/bullets/" + name + ".prefab"), parent)));
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
            Set(_f.Host, "_combat", _combat);
            var layout = _f.Root.AddComponent<BattleScreenLayoutController>();
            Set(layout, "_playerBounds", new Bounds(Vector3.zero, new Vector3(20, 20, 1)));
            Set(layout, "_opponentBounds", new Bounds(Vector3.right * 10, new Vector3(20, 20, 1)));
            Set(_f.Host, "_screenLayout", layout);
            return AddRobot(configId);
        }
        private RobotEntity AddRobot(int configId)
        {
            var config = _f.Tables.TbRobot.Get(configId);
            var robot = _f.Context.Registry.CreateRobotFromConfig(config, BattleSide.Player, 1);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/" + config.RobotPrefab + ".prefab"), _f.Root.transform);
            instance.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, instance);
            return robot;
        }
        private bool Contact(RobotEntity robot)
        { _combat.SyncRobots(); return _combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId); }
        private RobotBulletView[] LiveBullets() => _f.Root.GetComponentsInChildren<RobotBulletView>().Where(b => b.State != null).ToArray();
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
        private sealed class AttackHook : BuffBehavior
        {
            private readonly Action _action;
            internal AttackHook(Action action) { _action = action; }
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft d) => _action();
        }
        private sealed class CastHook : BuffBehavior
        {
            private readonly Action<SkillCastContext> _action;
            internal CastHook(Action<SkillCastContext> action) { _action = action; }
            public override void OnCast(BuffContainer c, BuffObj b, SkillCastContext cast) => _action(cast);
        }
    }
}
