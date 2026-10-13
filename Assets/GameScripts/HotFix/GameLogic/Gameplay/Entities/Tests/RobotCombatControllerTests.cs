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
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(1, _combat.LiveBulletCount);
            Assert.AreEqual(0.2f, robot.TriggerCooldownRemaining);
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
        public void ContactsFireThenLaterFramesAdvanceFlightAndShareTheSameTarget()
        {
            Prepare(); var first = AddRobot(); var second = AddRobot();
            _combat.AdvanceExistingBullets(0.6f);
            _combat.AdvanceAttacks(0.6f);
            Assert.IsTrue(Contact(first));
            Assert.IsTrue(Contact(second));
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
            Assert.IsTrue(Contact(robot));
            robot.TrySetDragging(true);
            _f.Context.Registry.AdvanceTime(1f);
            _combat.AdvanceAttacks(1f);
            Assert.IsFalse(Contact(robot));
            Assert.AreEqual(1, _combat.LiveBulletCount);
            robot.TrySetLocation(RobotLocation.Storage, null);
            _combat.AdvanceAttacks(1f);
            Assert.IsFalse(Contact(robot));
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target, out var entity);
            Assert.AreEqual(80, ((EnemyEntity)entity).CurrentHp);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
        }

        [Test]
        public void ArrivalBeforeBulletPhaseLeaksOnceAndDoesNotDamageTheNextEnemy()
        {
            _f.Clear(); _f.SetRoutes(new[] { new Vector3(-0.61f, 0f, 0f) }); _f.Prepare(); _f.Start();
            Prepare(); var robot = AddRobot();
            Assert.IsTrue(Contact(robot));
            _f.Advance(0.6f);
            _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(4, _f.Host.Towers.PlayerTower.CurrentHp);
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
            fail = true; var robot = AddRobot();
            LogAssert.Expect(LogType.Error, "机器人子弹准备失败，1秒战斗时间后重试：expected bullet load failure");
            _combat.AdvanceAttacks(0f);
            Assert.IsFalse(Contact(robot));
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            var afterFailure = calls;
            _combat.AdvanceAttacks(0.5f);
            Assert.AreEqual(afterFailure, calls);
            fail = false;
            _combat.AdvanceAttacks(0.5f);
            Assert.AreEqual(0, _combat.LiveBulletCount, "准备完成不能补发此前被丢弃的接触");
            Assert.IsTrue(Contact(robot));
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
            Prepare(); var robot = AddRobot(level);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(damage, _f.Root.GetComponentInChildren<RobotBulletView>().Damage);
        }

        [Test]
        public void NoTargetDropsContactAndNewEnemyStillRequiresANewContact()
        {
            foreach (var view in _f.Views)
            {
                _f.Context.Registry.ApplyDamage(view.InstanceId, 100, out var removal);
                ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            }
            Prepare(); var robot = AddRobot();
            _combat.AdvanceAttacks(1f);
            Assert.IsFalse(Contact(robot));
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            Assert.AreEqual("Idle", robot.CombatState);
            _f.Advance(0.5f);
            _combat.AdvanceAttacks(0f);
            Assert.AreEqual(0, _combat.LiveBulletCount, "新目标不能补发此前的接触");
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(1, _combat.LiveBulletCount);
            Assert.AreEqual("Attack", robot.CombatState);
        }

        [Test]
        public void NoContactNeverAutomaticallyAttacksEvenWithReadyBulletAndTarget()
        {
            Prepare(); var robot = AddRobot();
            _combat.AdvanceAttacks(0f);
            _f.Context.Registry.AdvanceTime(10f);
            _combat.AdvanceAttacks(10f);
            TEngine.ModuleSystem.Update(10f, 10f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            Assert.AreEqual("Idle", robot.CombatState);
        }

        [Test]
        public void CooldownDropsContactAndExpiryStillRequiresANewContact()
        {
            Prepare(); var robot = AddRobot();
            Assert.IsTrue(Contact(robot));
            _f.Context.Registry.AdvanceTime(0.1f);
            Assert.IsFalse(Contact(robot));
            Assert.That(robot.TriggerCooldownRemaining, Is.EqualTo(0.1f).Within(0.00001f));
            _f.Context.Registry.AdvanceTime(0.1f);
            _combat.AdvanceAttacks(1f);
            Assert.AreEqual(1, _combat.LiveBulletCount, "冷却期间的接触不可排队补发");
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(2, _combat.LiveBulletCount);
            Assert.AreEqual(0.2f, robot.TriggerCooldownRemaining);
        }

        [Test]
        public void PendingBulletDropsContactAndCompletionStillRequiresANewContact()
        {
            var pending = new UniTaskCompletionSource<GameObject>();
            var delay = false;
            Prepare((name, parent, token) => delay ? pending.Task : Load(name, parent, token));
            delay = true; var robot = AddRobot();
            _combat.SyncRobots();
            Assert.IsFalse(Contact(robot));
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            var ready = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Bullets/Prefabs/RobotBullet.prefab"), _f.Root.transform);
            delay = false; pending.TrySetResult(ready);
            _combat.AdvanceAttacks(1f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(1, _combat.LiveBulletCount);
        }

        [Test]
        public void MissingReserveDropsContactAndSynchronizationDoesNotReplayIt()
        {
            Prepare(); var robot = AddRobot();
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId));
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            _combat.AdvanceAttacks(0f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.IsTrue(_combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId));
            Assert.AreEqual(1, _combat.LiveBulletCount);
        }

        [Test]
        public void StaleBattleWrongSideInvalidRobotAndFrozenBattleRejectContacts()
        {
            Prepare(); var robot = AddRobot();
            _combat.SyncRobots();
            Assert.IsFalse(Contact(robot, _f.Context.BattleId + 1));
            Assert.IsFalse(Contact(robot, side: BattleSide.Opponent));
            Assert.IsFalse(Contact(robot, side: (BattleSide)99));
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, BattleSide.Player, int.MaxValue));
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, BattleSide.Player,
                _f.Host.Towers.PlayerTower.InstanceId));
            _views[robot.InstanceId].SetActive(false);
            Assert.IsFalse(Contact(robot));
            _views[robot.InstanceId].SetActive(true);
            _f.Host.SetFrozen(true);
            Assert.IsFalse(Contact(robot));
            _f.Host.SetFrozen(false);
            Assert.AreEqual(0f, robot.TriggerCooldownRemaining);
            Assert.IsTrue(Contact(robot));
            _f.Context.Registry.Remove(robot.InstanceId, EntityRemovalReason.Consumed, out _);
            Assert.IsFalse(Contact(robot));
            _combat.Dispose();
            Assert.IsFalse(Contact(robot));
        }

        [TestCase(BattleSide.Player)] [TestCase(BattleSide.Opponent)]
        public void ContactUsesTheRobotsOwnBattleSide(BattleSide side)
        {
            Prepare(); var robot = AddRobot(side: side);
            Assert.IsTrue(Contact(robot));
            var bullet = _f.Root.GetComponentInChildren<RobotBulletView>();
            Assert.AreEqual(side, bullet.Side);
            Assert.IsTrue(_f.Context.Registry.TryGet(bullet.TargetInstanceId, out var target));
            Assert.AreEqual(side, target.Side);
        }

        [TestCase(1001)] [TestCase(1002)] [TestCase(1003)]
        public void EveryConfiguredRobotUsesTheMinimumContactTriggerInterval(int configId)
        {
            Prepare(); var robot = AddRobot(configId: configId);
            Assert.AreEqual(0.2f, robot.Configuration.MinTriggerInterval);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(0.2f, robot.TriggerCooldownRemaining);
        }

        private bool Contact(RobotEntity robot, long? battleId = null, BattleSide? side = null)
        {
            _combat.SyncRobots();
            return _combat.TryAttackFromContact(battleId ?? _f.Context.BattleId, side ?? robot.Side, robot.InstanceId);
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

        private RobotEntity AddRobot(int level = 1, BattleSide side = BattleSide.Player, int configId = 1001)
        {
            var config = _f.Tables.TbRobot.Get(configId);
            var robot = _f.Context.Registry.CreateRobotFromConfig(config, side, level);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab"), _f.Root.transform);
            go.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, go);
            return robot;
        }
    }
}
