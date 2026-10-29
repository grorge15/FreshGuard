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
        [Test]
        public void BuffAttackIsFrozenAtLaunchAndTargetDefenseIsEvaluatedAtImpact()
        {
            Prepare(); var robot = AddRobot(); var target = _f.PlayerView.InstanceId;
            var callbacks = new List<string>();
            robot.Buffs.Add(new BuffModel(90, "custom"), robot.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Attack = attack => attack.AddPercent(CombatModifierGroup.GlobalDamage, 0.5f),
                Hit = context => callbacks.Add("hit")
            });
            Assert.IsTrue(Contact(robot));
            _f.Context.Registry.TryGet(target, out var entity);
            entity.Buffs.Add(new BuffModel(91, "custom"), entity.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Hurt = context => { callbacks.Add("hurt"); context.Outcome.AddPercent(CombatModifierGroup.TargetCondition, -0.5f); }
            });
            _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(85, ((EnemyEntity)entity).CurrentHp);
            CollectionAssert.AreEqual(new[] { "hit", "hurt" }, callbacks);
        }

        [Test]
        public void LethalHitNotifiesVictimBeforePoolReturnAndKillerAfterConfirmedDamage()
        {
            Prepare(); var robot = AddRobot(); var target = _f.PlayerView.InstanceId;
            var callbacks = new List<string>();
            robot.Buffs.Add(new BuffModel(90, "custom"), robot.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Attack = attack => attack.AddPercent(CombatModifierGroup.GlobalDamage, 9f),
                Kill = context => { callbacks.Add("kill"); Assert.AreEqual(100, context.Result.AppliedDamage); }
            });
            _f.Context.Registry.TryGet(target, out var entity);
            entity.Buffs.Add(new BuffModel(91, "custom"), entity.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Killed = context => { callbacks.Add("killed"); Assert.AreEqual(0, ((EnemyEntity)entity).CurrentHp); }
            });
            Assert.IsTrue(Contact(robot)); _combat.AdvanceExistingBullets(0.6f);
            Assert.IsFalse(_f.Context.Registry.TryGet(target, out _));
            CollectionAssert.AreEqual(new[] { "killed", "kill" }, callbacks);
        }

        private sealed class CombatHookBuff : BuffBehavior
        {
            internal Action<CombatOutcomeDraft> Attack;
            internal Action<CombatDamageContext> Hit, Hurt, Kill, Killed;
            internal Action<SkillCastContext> Cast;
            public override void OnCast(BuffContainer c, BuffObj b, SkillCastContext x) => Cast?.Invoke(x);
            public override void OnAttack(BuffContainer c, BuffObj b, CombatOutcomeDraft x) => Attack?.Invoke(x);
            public override void OnHit(BuffContainer c, BuffObj b, CombatDamageContext x) => Hit?.Invoke(x);
            public override void OnBeHurt(BuffContainer c, BuffObj b, CombatDamageContext x) => Hurt?.Invoke(x);
            public override void OnKill(BuffContainer c, BuffObj b, CombatDamageContext x) => Kill?.Invoke(x);
            public override void OnBeKilled(BuffContainer c, BuffObj b, CombatDamageContext x) => Killed?.Invoke(x);
        }
        [SetUp]
        public void SetUp()
        {
            _f = new EnemyBattleControllerTests.Fixture(); _f.Prepare(); _f.Start();
            _views = new Dictionary<int, GameObject>();
        }
        [TearDown] public void TearDown() { _combat?.Dispose(); _f.Dispose(); }

        [Test]
        public void BuffAcquiredDuringFlightOnlyChangesTheNextBullet()
        {
            Prepare(); var robot = AddRobot();
            var target = _f.PlayerView.InstanceId;
            Assert.IsTrue(Contact(robot));
            AddDamageBuff(BattleSide.Player, 0.5f);
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target, out var entity);
            Assert.AreEqual(80, ((EnemyEntity)entity).CurrentHp);
            _f.Context.Registry.AdvanceTime(0.2f);
            Assert.IsTrue(Contact(robot));
            var bullet = _f.Root.GetComponentInChildren<RobotBulletView>();
            Assert.AreEqual(robot.InstanceId, bullet.Action.SourceInstanceId);
            Assert.AreEqual(robot.SkillId, bullet.Action.SkillId);
            Assert.AreEqual(30, bullet.Damage);
            _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(50, ((EnemyEntity)entity).CurrentHp);
        }

        [Test]
        public void LethalModifiedShotUsesRegistryAndCancelsOtherShotsToRemovedTarget()
        {
            Prepare(); var robot = AddRobot(); var second = AddRobot();
            var target = _f.PlayerView.InstanceId;
            AddDamageBuff(BattleSide.Player, 9f);
            Assert.IsTrue(Contact(robot)); Assert.IsTrue(Contact(second));
            _combat.AdvanceExistingBullets(0.6f);
            Assert.IsFalse(_f.Context.Registry.TryGet(target, out _));
            Assert.IsFalse(((EnemyBattleController)_f.Controller).TryGetTargetPosition(BattleSide.Player, target, out _));
            Assert.AreEqual(0, _combat.LiveBulletCount);
        }

        [Test]
        public void FrozenBattleKeepsBulletAndCombatTimeStillAndEndClearsRuntime()
        {
            Prepare(); var robot = AddRobot(); Assert.IsTrue(Contact(robot));
            AddDamageBuff(BattleSide.Player, 0.5f);
            _f.Host.SetFrozen(true);
            var time = _f.Context.Combat.BattleTime;
            typeof(BattleShopSceneController).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_f.Host, null);
            _combat.AdvanceExistingBullets(2f);
            Assert.AreEqual(time, _f.Context.Combat.BattleTime);
            Assert.AreEqual(0f, _f.Root.GetComponentInChildren<RobotBulletView>().Elapsed);
            _f.Context.EndBattle();
            Assert.IsTrue(_f.Context.Combat.IsDisposed);
            Assert.AreEqual(0, _f.Context.Battle.Player.Buffs.Count);
        }

        [Test]
        public void BuffTickFailureEndsBattleAndClearsRuntime()
        {
            Prepare(); var robot = AddRobot(); Assert.IsTrue(Contact(robot));
            var context = _f.Context;
            context.Battle.Player.Buffs.Add(new BuffModel(93, "custom", tickInterval: 0.0000001f),
                context.Battle.Player.OwnerRef, 10f, behavior: new ThrowingTickBuff());
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: expected buff tick failure"));
            Assert.DoesNotThrow(() => _f.Host.GetType().GetMethod("AdvanceBattle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_f.Host, new object[] { 0.1f }));
            Assert.IsTrue(context.IsEnded);
            Assert.IsTrue(context.Combat.IsDisposed);
            Assert.AreEqual(0, context.Battle.Player.Buffs.Count);
            Assert.IsFalse(_f.Host.IsCombatRunning);
        }

        private void AddDamageBuff(BattleSide side, float percent)
        {
            var owner = side == BattleSide.Player ? _f.Context.Battle.Player : _f.Context.Battle.Opponent;
            owner.Buffs.Add(new BuffModel(92, "custom"), owner.OwnerRef, 10f, behavior: new CombatHookBuff
            { Attack = attack => attack.AddPercent(CombatModifierGroup.GlobalDamage, percent) });
        }

        private sealed class ThrowingTickBuff : BuffBehavior
        {
            public override void OnTick(BuffContainer container, BuffObj buff)
                => throw new InvalidOperationException("expected buff tick failure");
        }

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

        [TestCase(false)]
        [TestCase(true)]
        public void DeadTargetBulletFinishesAtDeathPositionEvenAfterDeathViewIsReleased(bool beforeFirstTick)
        {
            Prepare(); var robot = AddRobot();
            var victim = _f.PlayerView;
            var victimId = victim.InstanceId;
            Assert.IsTrue(Contact(robot));
            var bullet = _f.Root.GetComponentInChildren<RobotBulletView>();
            var state = bullet.State;
            if (!beforeFirstTick) _combat.AdvanceExistingBullets(0.1f);
            _f.Advance(0.1f); // 敌人移动到子弹尚未采样的新位置后死亡。
            var deathPosition = victim.transform.position;
            Assert.AreEqual(DamageResult.Killed, _f.Context.Registry.ApplyDamage(victimId, int.MaxValue, out var removal));
            ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            victim.OnDeathAnimationComplete();
            Assert.IsFalse(((EnemyBattleController)_f.Controller).TryGetTargetPosition(BattleSide.Player, victimId, out _));
            var resolved = 0;
            _f.Context.Combat.TargetResolved += _ => resolved++;
            _combat.AdvanceExistingBullets(0.1f);
            Assert.AreEqual(1, _combat.LiveBulletCount, "死亡后在途子弹不能提前回收");
            Assert.IsFalse(state.IsResolved);
            var progress = state.Elapsed / state.Launcher.Model.FlightTime;
            var expected = Vector3.Lerp(state.Action.Origin, deathPosition, progress) + Vector3.up *
                (4f * state.Launcher.Model.ArcHeight * progress * (1f-progress));
            Assert.That(Vector3.Distance(expected, state.Position), Is.LessThan(0.00001f));
            _f.Advance(0.1f); // 后续新敌人移动不得改变这颗子弹的落点。
            _combat.AdvanceExistingBullets(1f);
            Assert.AreEqual(deathPosition, state.Position);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(0, resolved, "空落点不能再次结算伤害");
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

        [TestCase(501)] [TestCase(502)] [TestCase(505)]
        [TestCase(503)] [TestCase(504)] [TestCase(602)]
        [TestCase(601)] [TestCase(603)]
        public void EveryConfiguredRobotUsesTheMinimumContactTriggerInterval(int configId)
        {
            Prepare(); var robot = AddRobot(configId: configId);
            Assert.AreEqual(0.2f, robot.Configuration.MinTriggerInterval);
            Assert.IsTrue(Contact(robot));
            Assert.AreEqual(0.2f, robot.TriggerCooldownRemaining);
        }

        [Test]
        public void FrostUsesRobotSkillLevelThenSlowsAndRefreshesOnlyItsOwnSource()
        {
            Prepare(); var frost=AddRobot(configId:601); var second=AddRobot(configId:601);
            var target=_f.PlayerView.InstanceId;
            Assert.IsTrue(Contact(frost)); Assert.AreEqual(20,_f.Root.GetComponentInChildren<RobotBulletView>().Damage);
            Assert.IsTrue(frost.Skills.TryGet(1505,out var skill)); Assert.AreEqual(1,skill.Level);
            Assert.AreEqual(0f,skill.CooldownRemaining);
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target,out var entity); var enemy=(EnemyEntity)entity;
            Assert.AreEqual(80,enemy.CurrentHp); Assert.AreEqual(1,enemy.Buffs.Count);
            Assert.That(enemy.EffectiveSpeed,Is.EqualTo(enemy.BaseSpeed*0.7f).Within(0.00001f));
            enemy.Buffs.Advance(1f); _f.Context.Registry.AdvanceTime(0.2f);
            Assert.IsTrue(Contact(frost)); _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(1,enemy.Buffs.Count); Assert.AreEqual(3f,enemy.Buffs.Items[0].Remaining);
            Assert.IsTrue(Contact(second)); _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(2,enemy.Buffs.Count); Assert.AreEqual(40,enemy.CurrentHp);
            Assert.That(enemy.EffectiveSpeed,Is.EqualTo(enemy.BaseSpeed*0.7f).Within(0.00001f));
            enemy.Buffs.Advance(3f); Assert.AreEqual(0,enemy.Buffs.Count); Assert.AreEqual(enemy.BaseSpeed,enemy.EffectiveSpeed);
        }

        [TestCase(0.6f, 6f)]
        [TestCase(0f, 6f)]
        [TestCase(0.6f, 0f)]
        public void OnCastSlowParametersAreFrozenIntoTheLaunchedBullet(float slow, float duration)
        {
            Prepare(); var frost = AddRobot(configId: 601); var target = _f.PlayerView.InstanceId;
            SkillCastContext captured = null;
            frost.Buffs.Add(new BuffModel(94, "custom"), frost.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Cast = context => { captured = context; context.Parameters["slow"] = slow; context.Parameters["duration"] = duration; }
            });
            Assert.IsTrue(Contact(frost));
            captured.Parameters["slow"] = 0.9f; captured.Parameters["duration"] = 9f;
            _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target, out var entity);
            if (slow <= 0f || duration <= 0f) Assert.AreEqual(0, entity.Buffs.Count);
            else
            {
                Assert.AreEqual(slow, entity.Buffs.Items[0].Get("slow"));
                Assert.AreEqual(duration, entity.Buffs.Items[0].Remaining);
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void FreezingInsideDeathHooksStillCompletesCommittedKillNotifications(bool victimFreezes)
        {
            Prepare(); var robot = AddRobot(); var target = _f.PlayerView.InstanceId;
            var kills = 0; var notifications = 0;
            robot.Buffs.Add(new BuffModel(94, "custom"), robot.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Attack = attack => attack.AddPercent(CombatModifierGroup.GlobalDamage, 9f),
                Kill = context => { kills++; if (!victimFreezes) _f.Host.SetFrozen(true); }
            });
            _f.Context.Registry.TryGet(target, out var entity);
            entity.Buffs.Add(new BuffModel(95, "custom"), entity.OwnerRef, 10f, behavior: new CombatHookBuff
            { Killed = context => { if (victimFreezes) _f.Host.SetFrozen(true); } });
            _f.Context.Combat.TargetResolved += context => { if (context.Result.IsKilled) notifications++; };
            Assert.IsTrue(Contact(robot)); _combat.AdvanceExistingBullets(0.6f);
            Assert.IsFalse(_f.Context.Registry.TryGet(target, out _));
            Assert.AreEqual(1, kills);
            Assert.AreEqual(1, notifications);
        }

        [TestCase(true)] [TestCase(false)]
        public void RequestingBattleEndInsideDeathHooksStopsFollowingNotifications(bool victimEnds)
        {
            Prepare(); var robot = AddRobot(); var target = _f.PlayerView.InstanceId;
            var kills = 0; var notifications = 0;
            robot.Buffs.Add(new BuffModel(94, "custom"), robot.OwnerRef, 10f, behavior: new CombatHookBuff
            {
                Attack = attack => attack.AddPercent(CombatModifierGroup.GlobalDamage, 9f),
                Kill = context => { kills++; if (!victimEnds) _f.Context.RequestEndBattle(); }
            });
            _f.Context.Registry.TryGet(target, out var entity);
            entity.Buffs.Add(new BuffModel(95, "custom"), entity.OwnerRef, 10f, behavior: new CombatHookBuff
            { Killed = context => { if (victimEnds) _f.Context.RequestEndBattle(); } });
            _f.Context.Combat.TargetResolved += context => notifications++;
            Assert.IsTrue(Contact(robot)); _combat.AdvanceExistingBullets(0.6f);
            Assert.IsTrue(_f.Context.IsEndRequested);
            Assert.IsFalse(_f.Context.Registry.TryGet(target, out _));
            Assert.AreEqual(victimEnds ? 0 : 1, kills);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void FrostLethalDamageDoesNotAttachBuffToRecycledEnemyOrNextSpawn()
        {
            Prepare(); var frost=AddRobot(configId:601); var target=_f.PlayerView.InstanceId;
            _f.Context.Registry.TryGet(target,out var entity); var oldBuffs=entity.Buffs;
            AddDamageBuff(BattleSide.Player, 9f);
            Assert.IsTrue(Contact(frost)); _combat.AdvanceExistingBullets(0.6f);
            Assert.IsFalse(_f.Context.Registry.TryGet(target,out _)); Assert.IsTrue(oldBuffs.IsDisposed); Assert.AreEqual(0,oldBuffs.Count);
            _f.Advance(0.5f); var living=0;
            foreach(var view in _f.Views)
                if(_f.Context.Registry.TryGet(view.InstanceId,out var next)) { living++; Assert.AreEqual(0,next.Buffs.Count); }
            Assert.Greater(living,0);
        }

        [Test]
        public void FrostOldBulletAppliesFrozenSlowAfterItsSourceIsConsumed()
        {
            Prepare(); var frost=AddRobot(configId:601); var source=frost.OwnerRef; var target=_f.PlayerView.InstanceId;
            Assert.IsTrue(Contact(frost)); _f.Context.Registry.Remove(frost.InstanceId,EntityRemovalReason.Consumed,out _);
            _combat.SyncRobots(); _combat.AdvanceExistingBullets(0.6f);
            _f.Context.Registry.TryGet(target,out var entity);
            Assert.AreEqual(80,((EnemyEntity)entity).CurrentHp); Assert.IsNotNull(entity.Buffs.Find(2505,source));
        }

        [Test]
        public void CancelledAndInvalidTimelineDoNotConsumeRobotContactInterval()
        {
            Prepare(); var frost=AddRobot(configId:601); _combat.SyncRobots();
            frost.Buffs.Add(new BuffModel(99,"custom"),frost.OwnerRef,10f,behavior:new CancelCastBuff());
            Assert.IsFalse(Contact(frost)); Assert.AreEqual(0f,frost.TriggerCooldownRemaining); Assert.AreEqual(0,_combat.LiveBulletCount);
            frost.Buffs.Clear(); Assert.IsTrue(Contact(frost));
        }
        private sealed class CancelCastBuff : BuffBehavior
        { public override void OnCast(BuffContainer container,BuffObj buff,SkillCastContext context)=>context.Cancel(); }

        [Test]
        public void MultipleBulletNodesAreRejectedBeforeSpendingContactInterval()
        {
            Prepare(); var robot=AddRobot(); _combat.SyncRobots();
            _f.Context.Battle.Timelines.Register(new TimelineModel("two-shots",0.5f,new[]
            {new TimelineNode(0f,"robot.bullet"),new TimelineNode(0.5f,"robot.bullet")}));
            robot.Buffs.Add(new BuffModel(99,"custom"),robot.OwnerRef,10f,behavior:new ReplaceCastBuff("two-shots"));
            Assert.IsFalse(Contact(robot)); Assert.AreEqual(0f,robot.TriggerCooldownRemaining); Assert.AreEqual(0,_combat.LiveBulletCount);
            robot.Buffs.Clear(); Assert.IsTrue(Contact(robot));
        }
        private sealed class ReplaceCastBuff : BuffBehavior
        {
            private readonly string _key;
            internal ReplaceCastBuff(string key){_key=key;}
            public override void OnCast(BuffContainer container,BuffObj buff,SkillCastContext context)=>context.TimelineKey=_key;
        }

        [Test]
        public void EndBattleStillClearsRegistryAndShopWhenABuffRemovalThrows()
        {
            Prepare(); var robot=AddRobot(); Assert.IsTrue(Contact(robot));
            _f.Context.Battle.Player.Buffs.Add(new BuffModel(99,"custom"),_f.Context.Battle.Player.OwnerRef,10f,
                behavior:new ThrowRemovedBuff());
            Assert.Throws<InvalidOperationException>(()=>_f.Context.EndBattle());
            Assert.IsTrue(_f.Context.IsEnded); Assert.IsTrue(_f.Context.Combat.IsDisposed);
            Assert.AreEqual(0,_f.Context.Registry.CopyIds().Length); Assert.AreEqual(0,_combat.LiveBulletCount);
            foreach(var side in new[]{BattleSide.Player,BattleSide.Opponent})
                foreach(var slot in _f.Context.GetState(side).Slots) Assert.IsNull(slot.Offer);
        }
        private sealed class ThrowRemovedBuff : BuffBehavior
        { public override void OnRemoved(BuffContainer container,BuffObj buff)=>throw new InvalidOperationException("expected end cleanup failure"); }

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

        private RobotEntity AddRobot(int level = 1, BattleSide side = BattleSide.Player, int configId = 501)
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
