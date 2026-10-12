using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class RobotCombatControllerTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private Dictionary<int, GameObject> _views;
        private RobotCombatController _combat;
        [SetUp]
        public void SetUp()
        {
            _f = new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            _views = new Dictionary<int, GameObject>();
        }
        [TearDown] public void TearDown() { _combat?.Dispose(); _f.Dispose(); }

        [Test]
        public void UpgradeThenConsumeSourceDoesNotChangeOrCancelAnAlreadyFiredBullet()
        {
            Prepare(); var robot = AddRobot();
            var target = _f.PlayerView.InstanceId;
            _combat.AdvanceAttacks(0f);
            Assert.AreEqual(1, _combat.LiveBulletCount);
            Assert.AreEqual(1f, robot.TriggerCooldownRemaining);
            Assert.IsTrue(robot.TryUpgrade());
            Assert.AreEqual(30, robot.GetAttackDamage());
            _f.Context.Registry.Remove(robot.InstanceId, EntityRemovalReason.Consumed, out _);
            _combat.SyncRobots();
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target, out var entity);
            Assert.AreEqual(80, ((EnemyEntity)entity).CurrentHp, "旧弹仍按20伤害命中");
            Assert.AreEqual(0, _combat.LiveBulletCount);
        }

        [Test]
        public void FirstFrameOnlyFiresThenLaterFramesAdvanceFlightAndShareTheSameTarget()
        {
            Prepare(); var first = AddRobot(); var second = AddRobot();
            _combat.AdvanceExistingBullets(0.6f);
            _combat.AdvanceAttacks(0.6f);
            Assert.AreEqual(2, _combat.LiveBulletCount);
            var bullets = _f.Root.GetComponentsInChildren<RobotBulletView>();
            Assert.AreEqual(2, bullets.Length);
            Assert.AreEqual(bullets[0].TargetInstanceId, bullets[1].TargetInstanceId);
            Assert.AreEqual(0f, bullets[0].Elapsed);
            _combat.AdvanceAttacks(10f);
            Assert.AreEqual(2, _combat.LiveBulletCount, "攻击阶段不另减冷却，不补发");
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(_f.PlayerView.InstanceId, out var entity);
            Assert.AreEqual(60, ((EnemyEntity)entity).CurrentHp);
        }

        [Test]
        public void DragAndStorageBlockNewShotsWhileOldBulletsKeepFlying()
        {
            Prepare(); var robot = AddRobot();
            var target = _f.PlayerView.InstanceId;
            _combat.AdvanceAttacks(0f);
            robot.TrySetDragging(true);
            _f.Context.Registry.AdvanceTime(1f);
            _combat.AdvanceAttacks(1f);
            Assert.AreEqual(1, _combat.LiveBulletCount);
            robot.TrySetLocation(RobotLocation.Storage, null);
            _combat.AdvanceAttacks(1f);
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target, out var entity);
            Assert.AreEqual(80, ((EnemyEntity)entity).CurrentHp);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
        }

        [Test]
        public void ArrivalBeforeBulletPhaseLeaksOnceAndDoesNotDamageTheNextEnemy()
        {
            _f.Clear(); _f.SetRoutes(new[] { new Vector3(-0.1f, 0f, 0f) }); _f.Prepare(); _f.Start();
            Prepare(); AddRobot();
            _combat.AdvanceAttacks(0f);
            _f.Advance(0.6f);
            _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(99, _f.Host.Towers.PlayerTower.CurrentHp);
            _f.Context.Registry.TryGet(_f.PlayerView.InstanceId, out var entity);
            Assert.AreEqual(100, ((EnemyEntity)entity).CurrentHp);
            Assert.AreEqual(0, _combat.LiveBulletCount);
        }

        [Test]
        public void FailedPreparationDoesNotSpendCooldownAndRetriesAfterOneCombatSecond()
        {
            var fail = false; var calls = 0;
            Prepare((name, parent, token) =>
            {
                calls++;
                if (fail) throw new InvalidOperationException("expected bullet load failure");
                return Load(name, parent, token);
            });
            fail = true; AddRobot();
            LogAssert.Expect(LogType.Error, "机器人子弹准备失败，1秒战斗时间后重试：expected bullet load failure");
            _combat.AdvanceAttacks(0f);
            var afterFailure = calls;
            _combat.AdvanceAttacks(0.5f);
            Assert.AreEqual(afterFailure, calls);
            fail = false;
            _combat.AdvanceAttacks(0.5f);
            Assert.AreEqual(1, _combat.LiveBulletCount);
        }

        [Test]
        public void LateLoadAfterDisposeIsDestroyedAndCannotCreateGhostBullets()
        {
            var pending = new UniTaskCompletionSource<GameObject>();
            var delay = false;
            Prepare((name, parent, token) => delay ? pending.Task : Load(name, parent, token));
            delay = true; var robot = AddRobot();
            _combat.SyncRobots();
            _combat.Dispose();
            var late = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Bullets/Prefabs/RobotBullet.prefab"));
            pending.TrySetResult(late);
            Assert.IsTrue(late == null);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
        }

        [TestCase(1, 20)] [TestCase(3, 40)] [TestCase(5, 60)]
        public void ConfiguredLevelDamageIsCapturedByTheFiredBullet(int level, int damage)
        {
            Prepare(); AddRobot(level);
            _combat.AdvanceAttacks(0f);
            Assert.AreEqual(damage, _f.Root.GetComponentInChildren<RobotBulletView>().Damage);
        }

        [Test]
        public void NoTargetDoesNotSpendCooldownAndNewEnemyEnablesTheFirstShot()
        {
            foreach (var view in _f.Views)
            {
                _f.Context.Registry.ApplyDamage(view.InstanceId, 100, out var removal);
                ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            }
            Prepare(); var robot = AddRobot();
            _combat.AdvanceAttacks(1f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            Assert.AreEqual("Idle", robot.CombatState);
            _f.Advance(0.5f);
            _combat.AdvanceAttacks(0f);
            Assert.AreEqual(1, _combat.LiveBulletCount);
            Assert.AreEqual("Attack", robot.CombatState);
        }

        private void Prepare(Func<string, Transform, CancellationToken, UniTask<GameObject>> load = null)
        {
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, _views, load ?? Load);
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Bullets/Prefabs/" + name + ".prefab"), parent));
        }

        private RobotEntity AddRobot(int level = 1)
        {
            var config = _f.Tables.TbRobot.Get(1001);
            var robot = _f.Context.Registry.CreateRobotFromConfig(config, BattleSide.Player, level);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Robots/Prefabs/RobotI.prefab"), _f.Root.transform);
            go.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, go);
            return robot;
        }
    }
}
