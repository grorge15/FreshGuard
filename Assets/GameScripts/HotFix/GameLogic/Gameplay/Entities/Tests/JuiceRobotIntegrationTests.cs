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
    public sealed class JuiceRobotIntegrationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private bool _holdFirstChild;
        private int _juiceLoads;
        private UniTaskCompletionSource<GameObject> _pending;
        private Transform _pendingParent;
        private bool _holdEffect;
        private UniTaskCompletionSource<GameObject> _pendingEffect;
        private Transform _pendingEffectParent;

        [TearDown]
        public void Clear() { _combat?.Dispose(); _combat = null; _f?.Dispose(); _f = null; _views.Clear(); }

        [Test]
        public void RealControllerEmitsTwoCyclesWithThirdNormalPlusFiveFixedDrops()
        {
            Prepare(); var robot = AddRobot();
            var ordinary = 0; var drops = 0;
            for (var i = 0; i < 6; i++)
            {
                var emitted = Shoot(robot);
                ordinary += emitted.Count(s => s.Action.SkillId == 1603);
                drops += emitted.Count(s => s.Action.SkillId == 1604);
                Assert.AreEqual(i % 3 == 2 ? 6 : 1, emitted.Length);
                foreach (var drop in emitted.Where(s => s.Action.SkillId == 1604))
                {
                    Assert.IsEmpty(drop.Action.TargetInstanceIds);
                    Assert.AreEqual(12, drop.Action.BaseDamage);
                    Assert.AreEqual(0, drop.Elapsed, "子技能同刻发出，尚未飞行");
                }
            }
            Assert.AreEqual(6, ordinary); Assert.AreEqual(10, drops);
            Assert.AreEqual(0, Counter(robot).AttackCount);
        }

        [Test]
        public void MissingChildInstancesKeepEveryShotAndRetryOncePreparationCompletes()
        {
            Prepare(); _holdFirstChild = true; var robot = AddRobot();
            for (var i = 0; i < 5; i++) Assert.AreEqual(1, Shoot(robot).Length);
            Assert.IsTrue(Counter(robot).Pending); Assert.AreEqual(5, Counter(robot).AttackCount);
            Assert.AreEqual(1, _f.Context.Battle.TriggeredSkills.Count);
            Assert.IsNotNull(_pending);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/bullets/JuiceBullet.prefab");
            _pending.TrySetResult(UnityEngine.Object.Instantiate(prefab, _pendingParent));
            _combat.AdvanceAttacks(0);
            Assert.AreEqual(5, States().Count(s => s.Action.SkillId == 1604));
            Assert.AreEqual(2, Counter(robot).AttackCount);
            _combat.AdvanceAttacks(0);
            Assert.AreEqual(5, States().Count(s => s.Action.SkillId == 1604));
        }

        [Test]
        public void UpgradeChangesNextVolleyButOldDropsKeepDamageAndLandingPositions()
        {
            Prepare(); var robot = AddRobot(); Shoot(robot); Shoot(robot);
            var old = Shoot(robot).Where(s => s.Action.SkillId == 1604).ToArray();
            var positions = old.Select(s => s.Action.AimPoint).ToArray();
            Assert.IsTrue(robot.TryUpgrade());
            _f.PlayerView.transform.position += Vector3.down * 0.4f;
            Shoot(robot); Shoot(robot);
            var next = Shoot(robot).Where(s => s.Action.SkillId == 1604).ToArray();
            Assert.IsTrue(old.All(s => s.Action.BaseDamage == 12));
            Assert.IsTrue(next.All(s => s.Action.BaseDamage == 18));
            CollectionAssert.AreEqual(positions, old.Select(s => s.Action.AimPoint));
        }

        [Test]
        public void EmptyLandingsStillPlayFiveEffectsPauseAndRecycleByBattleTime()
        {
            Prepare(); var robot = AddRobot(); Shoot(robot); Shoot(robot); Shoot(robot);
            foreach (var view in _f.Views.ToArray())
            {
                _f.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal);
                ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            }
            _f.Host.SetFrozen(true);
            _combat.AdvanceExistingBullets(1);
            Assert.IsTrue(States().All(s => s.Elapsed == 0));
            _f.Host.SetFrozen(false); _combat.AdvanceExistingBullets(0.7f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(5, ActiveEffects());
            _combat.AdvanceExistingBullets(0.3f);
            Assert.AreEqual(5, ActiveEffects());
            _f.Host.SetFrozen(true); _combat.AdvanceExistingBullets(1);
            Assert.AreEqual(5, ActiveEffects());
            _f.Host.SetFrozen(false); _combat.AdvanceExistingBullets(0.326f);
            Assert.AreEqual(0, ActiveEffects());
        }

        [Test]
        public void TwoRobotsLandingTogetherKeepEffectsWhileAsyncPoolRefills()
        {
            Prepare(); var first = AddRobot(); var second = AddRobot();
            for (var i = 0; i < 3; i++) { Shoot(first); Shoot(second); }
            _holdEffect = true;
            _combat.AdvanceExistingBullets(0.7f);
            Assert.AreEqual(5, ActiveEffects());
            Assert.IsNotNull(_pendingEffect);
            _f.Host.SetFrozen(true);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/bullets/JuiceBulletLandingEffect.prefab");
            _pendingEffect.TrySetResult(UnityEngine.Object.Instantiate(prefab, _pendingEffectParent));
            _combat.AdvanceExistingBullets(0);
            Assert.AreEqual(5, ActiveEffects(), "异步就绪也不能在暂停中播放");
            _f.Host.SetFrozen(false); _combat.AdvanceExistingBullets(0);
            Assert.AreEqual(10, ActiveEffects(), "同帧第二轮落地表现应保留");
            _combat.Dispose();
            Assert.AreEqual(0, ActiveEffects());
        }

        private int ActiveEffects() => _f.Root.GetComponentsInChildren<JuiceLandingEffectView>().Length;
        private void Prepare()
        {
            _juiceLoads = 0; _holdFirstChild = false; _pending = null;
            _holdEffect = false; _pendingEffect = null;
            _f = new EnemyBattleControllerTests.Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            _f.Prepare(); _f.Start();
            foreach (var view in _f.Views) view.transform.position = new Vector3(-3, 1, 0) +
                (_f.Side(view) == BattleSide.Player ? Vector3.zero : Vector3.right * 10);
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, _views, Load);
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
            Set(_f.Host, "_combat", _combat);
        }
        private RobotEntity AddRobot()
        {
            var config = _f.Tables.TbRobot.Get(704);
            var robot = _f.Context.Registry.CreateRobotFromConfig(config, BattleSide.Player, 1);
            Assert.IsInstanceOf<JuicerRobotEntity>(robot);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var view = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/RobotJuicer.prefab"), _f.Root.transform);
            view.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, view); _combat.SyncRobots();
            return robot;
        }
        private BulletState[] Shoot(RobotEntity robot)
        {
            _f.Context.Registry.AdvanceTime(1);
            var before = States();
            Assert.IsTrue(_combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId));
            return States().Except(before).ToArray();
        }
        private BulletState[] States() => _f.Root.GetComponentsInChildren<RobotBulletView>()
            .Where(v => v.State != null).Select(v => v.State).ToArray();
        private static RobotAttackCycle Counter(RobotEntity robot) => robot.AttackCycle;
        private UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (name == "JuiceBulletLandingEffect" && _holdEffect)
            {
                _holdEffect = false; _pendingEffectParent = parent;
                _pendingEffect = new UniTaskCompletionSource<GameObject>();
                return _pendingEffect.Task;
            }
            if (name == "JuiceBullet" && ++_juiceLoads == 3 && _holdFirstChild)
            {
                _holdFirstChild = false;
                _pendingParent = parent;
                _pending = new UniTaskCompletionSource<GameObject>();
                return _pending.Task;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Prefabs/bullets/" + name + ".prefab");
            Assert.IsNotNull(prefab, name);
            return UniTask.FromResult(UnityEngine.Object.Instantiate(prefab, parent));
        }
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field,
            BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);
    }
}
