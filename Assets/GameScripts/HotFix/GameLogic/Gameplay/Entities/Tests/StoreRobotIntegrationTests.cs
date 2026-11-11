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
    public sealed class StoreRobotIntegrationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private RobotCombatController _combat;
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        [TearDown] public void Clear() { _combat?.Dispose(); _combat = null; _f?.Dispose(); _f = null; _views.Clear(); }

        [Test]
        public void TwoCyclesEmitTenShotsAndOnlyFifthAndTenthCarryIsolation()
        {
            Prepare(); var robot = AddRobot(); var ordinary = 0; var special = 0;
            for (var i = 0; i < 10; i++)
            {
                var emitted = Shoot(robot);
                Assert.AreEqual(1, emitted.Length, "每次攻击都只能发射一枚子弹");
                ordinary += emitted.Count(s => s.Action.SkillId == 1607);
                special += emitted.Count(s => s.Action.HitBuffs.Any(b => b.BuffId == 2600));
                foreach (var shot in emitted)
                {
                    Assert.AreEqual(20, shot.Action.BaseDamage, "强化弹保留普通攻击伤害");
                    Assert.AreEqual(i % 5 == 4, shot.Action.HitBuffs.Any(b => b.BuffId == 2600));
                    Assert.AreEqual(0, shot.Elapsed);
                }
            }
            Assert.AreEqual(10, ordinary); Assert.AreEqual(2, special);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.AreEqual(0, _f.Context.Battle.TriggeredSkills.Count, "强化普攻不排队额外发射");
        }

        [Test]
        public void AcceptedMissCountsCancelledCastDoesNotAndRobotsKeepIndependentCounters()
        {
            Prepare(); var first = AddRobot(); var second = AddRobot();
            first.Buffs.Add(new BuffModel(999, "test"), first.OwnerRef, 20, behavior: new CancelCast());
            _f.Context.Registry.AdvanceTime(1);
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, first.Side, first.InstanceId));
            Assert.AreEqual(0, Counter(first).AttackCount);
            first.Buffs.Remove(first.Buffs.Items.Single(b => b.Model.Id == 999));
            Shoot(first); Shoot(first); Shoot(second);
            Assert.AreEqual(2, Counter(first).AttackCount); Assert.AreEqual(1, Counter(second).AttackCount);
            foreach (var view in _f.Views.ToArray())
            {
                _f.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal);
                ((EnemyBattleController)_f.Controller).NotifyKilled(removal);
            }
            _combat.AdvanceExistingBullets(0.6f);
            Assert.AreEqual(0, _combat.LiveBulletCount);
            Assert.AreEqual(2, Counter(first).AttackCount);
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, first.Side, first.InstanceId));
            Assert.AreEqual(2, Counter(first).AttackCount);
        }

        [Test]
        public void FifthProjectileKeepsAttackDamageAndFrozenIsolationAfterUpgrade()
        {
            Prepare(tables =>
            {
                typeof(GameConfig.skill.SkillLevel).GetField("IsolateDuration").SetValue(tables.TbSkillLevel.Get(160202), 3f);
                typeof(GameConfig.skill.SkillLevel).GetField("IsolateDuration").SetValue(tables.TbSkillLevel.Get(160203), 6f);
            });
            var robot = AddRobot(); Shoot(robot); Shoot(robot);
            Assert.IsTrue(robot.TryUpgrade()); Assert.AreEqual(2, Counter(robot).AttackCount);
            Assert.IsTrue(robot.Skills.TryGet(1602, out var child)); Assert.AreEqual(2, child.Level);
            Shoot(robot); Shoot(robot);
            var shot = Shoot(robot).Single();
            Assert.AreEqual(3, shot.Action.HitBuffs.Single(b => b.BuffId == 2600).Duration);
            var view = _f.PlayerView; _f.Context.Registry.TryGet(view.InstanceId, out var target);
            var enemy = (EnemyEntity)target; var hp = enemy.CurrentHp;
            robot.TryUpgrade();
            // 单独推进装盒弹，避免先前测试发出的普通弹改变受击目标。
            shot.Advance(0.45f);
            Assert.AreEqual(hp - 30, enemy.CurrentHp); Assert.IsTrue(enemy.IsIsolated);
            Assert.AreEqual(3, enemy.Buffs.Items.Single(b => b.Model.Id == 2600).Remaining);
        }

        [Test]
        public void CancelledFifthCastKeepsChargeAndRetryDoesNotLeakEffectsToNextNormalAttack()
        {
            Prepare(); var robot = AddRobot();
            for (var i = 0; i < 4; i++) Assert.IsEmpty(Shoot(robot).Single().Action.HitBuffs);
            robot.Buffs.Add(new BuffModel(999, "test"), robot.OwnerRef, 20, behavior: new CancelCast());
            _f.Context.Registry.AdvanceTime(1); var before = States();
            Assert.IsFalse(_combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId));
            Assert.AreEqual(4, Counter(robot).AttackCount); CollectionAssert.AreEquivalent(before, States());
            robot.Buffs.Remove(robot.Buffs.Items.Single(b => b.Model.Id == 999));
            var fifth = Shoot(robot).Single();
            Assert.AreEqual(20, fifth.Action.BaseDamage);
            Assert.AreEqual(2600, fifth.Action.HitBuffs.Single().BuffId);
            Assert.AreEqual(0, Counter(robot).AttackCount);
            Assert.IsEmpty(Shoot(robot).Single().Action.HitBuffs);
            Assert.AreEqual(1, Counter(robot).AttackCount);
        }

        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void EleventhDemoCandidateCanBePurchasedDeployedAndUpgradePrimaryAndChild(BattleSide side)
        {
            _f = new EnemyBattleControllerTests.Fixture(); var tables = _f.Tables;
            var profile = AssetDatabase.LoadAssetAtPath<DemoBattleProfile>("Assets/AssetRaw/Configs/DemoBattleProfile.asset");
            var container = profile.Robots.Single(r => r.RobotId == 703);
            Assert.IsTrue(container.Unlocked); Assert.AreEqual(5, container.ReferenceLevel);
            var candidates = profile.Robots.Where(r => r.Unlocked).Select(r => r.RobotId).ToArray();
            Assert.AreEqual(10, Array.IndexOf(candidates, 703));
            using (var context = new BattleShopContext(ShopRules.FromTables(tables), candidates, candidates, tables.TbRobot.Get,
                new ContainerRandom((Array.IndexOf(candidates, 703) + .5) / candidates.Length),
                isRobotEnabled: id => tables.TbSkill.Get(tables.TbRobot.Get(id).SkillId).Enabled))
            {
                context.Battle.Configure(SkillConfigAdapter.FromTables(tables));
                var offer = context.GetState(side).FindSlot(0).Offer;
                Assert.AreEqual(ShopOperationResult.Success, context.TryPurchase(side, 0, offer.OfferId, out var robot));
                Assert.IsInstanceOf<FoodContainerRobotEntity>(robot);
                Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(side, robot.InstanceId,
                    side, new BoardModel(5, 5), new BoardCoordinate(0, 0)));
                Assert.IsTrue(robot.TryUpgrade());
                robot.Skills.TryGet(1607, out var primary); robot.Skills.TryGet(1602, out var child);
                Assert.AreEqual(2, primary.Level); Assert.AreEqual(2, child.Level);
                Assert.AreEqual(1, robot.Buffs.Items.Count(b => b.Model.Id == 2604));
            }
        }

        private void Prepare(Action<GameConfig.Tables> configure = null)
        {
            _f = new EnemyBattleControllerTests.Fixture(WaveTestData.Tables(new[] { WaveTestData.Plan(1, 1) },
                new[] { WaveTestData.Spawn(1, 0, 1, 2001, 1) }));
            configure?.Invoke(_f.Tables);
            _f.Prepare((name, parent, token) => name == "ContainerBullet" ? Load(name, parent, token) : _f.Load(name, parent, token));
            _f.Start();
            _combat = new RobotCombatController(_f.Host, _f.Context, (EnemyBattleController)_f.Controller, _views, Load);
            _combat.PrepareAsync(_f.Tables, CancellationToken.None).GetAwaiter().GetResult();
            typeof(BattleShopSceneController).GetField("_combat", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_f.Host, _combat);
        }
        private RobotEntity AddRobot()
        {
            var robot = _f.Context.Registry.CreateRobotFromConfig(_f.Tables.TbRobot.Get(703), BattleSide.Player, 1);
            robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0));
            var view = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/Robots/RobotFoodContainer.prefab"), _f.Root.transform);
            view.GetComponent<RobotView>().Bind(_f.Context.Registry, robot.InstanceId);
            _views.Add(robot.InstanceId, view); _combat.SyncRobots(); return robot;
        }
        private BulletState[] Shoot(RobotEntity robot)
        {
            _f.Context.Registry.AdvanceTime(1); var before = States();
            Assert.IsTrue(_combat.TryAttackFromContact(_f.Context.BattleId, robot.Side, robot.InstanceId));
            return States().Except(before).ToArray();
        }
        private BulletState[] States() => _f.Root.GetComponentsInChildren<RobotBulletView>()
            .Where(v => v.State != null).Select(v => v.State).ToArray();
        private static AttackCycleBuffBehavior Counter(RobotEntity robot) =>
            (AttackCycleBuffBehavior)robot.Buffs.Items.Single(b => b.Model.Id == 2604).Behavior;
        private UniTask<GameObject> Load(string name, Transform parent, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/bullets/" + name + ".prefab"), parent));
        }
        private sealed class CancelCast : BuffBehavior
        {
            public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context) => context.Cancel();
        }
        private sealed class ContainerRandom : IShopRandom
        {
            private readonly double _value;
            public ContainerRandom(double value) { _value = value; }
            public double NextDouble() => _value;
            public int NextInt(int maxExclusive) => maxExclusive - 1;
        }
    }
}
