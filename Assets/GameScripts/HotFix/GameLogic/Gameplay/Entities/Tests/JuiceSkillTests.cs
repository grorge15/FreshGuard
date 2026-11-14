using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameConfig;
using GameConfig.bullet;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class JuiceSkillTests
    {
        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void DemoCandidateCanBePurchasedDeployedAndUpgradeBothSkills(BattleSide side)
        {
            var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
            var profile = AssetDatabase.LoadAssetAtPath<DemoBattleProfile>("Assets/AssetRaw/Configs/DemoBattleProfile.asset");
            var juicer = profile.Robots.Single(r => r.RobotId == 704);
            Assert.IsTrue(juicer.Unlocked);
            Assert.AreEqual(5, juicer.ReferenceLevel);
            var candidates = profile.Robots.Where(r => r.Unlocked).Select(r => r.RobotId).ToArray();
            var rules = ShopRules.FromTables(tables);
            foreach (var weights in rules.SlotCandidateWeights)
            {
                Assert.Greater(weights[candidates.Length], 0);
                Assert.AreEqual(weights[candidates.Length - 1], weights[candidates.Length]);
            }
            using (var context = new BattleShopContext(rules, candidates, candidates, tables.TbRobot.Get,
                new CandidateRandom(Array.IndexOf(candidates, 704), candidates.Length),
                isRobotEnabled: id => tables.TbSkill.Get(tables.TbRobot.Get(id).SkillId).Enabled))
            {
                context.Battle.Configure(SkillConfigAdapter.FromTables(tables));
                var offer = context.GetState(side).FindSlot(0).Offer;
                Assert.AreEqual(ShopOperationResult.Success, context.TryPurchase(side, 0, offer.OfferId, out var robot));
                Assert.IsInstanceOf<JuicerRobotEntity>(robot);
                Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(side, robot.InstanceId,
                    side, new BoardModel(5, 5), new BoardCoordinate(0, 0)));
                Assert.IsTrue(robot.TryUpgrade());
                Assert.IsTrue(robot.Skills.TryGet(1603, out var primary));
                Assert.IsTrue(robot.Skills.TryGet(1604, out var child));
                Assert.AreEqual(2, primary.Level); Assert.AreEqual(2, child.Level);
                Assert.IsEmpty(robot.Buffs.Items);
                Assert.AreEqual(1604, robot.AttackCycle.TriggerSkillId);
            }
        }

        [Test]
        public void ExportedJuicerSkillsAreEnabledAndExposeNamedParameters()
        {
            var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
            var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGet(1603, out _), "1603应启用");
            Assert.IsTrue(catalog.TryGet(1604, out _), "1604应启用");
            Assert.IsTrue(catalog.TryGetLevel(1603, 1, out var normal));
            Assert.AreEqual(3, normal.Parameters["attackCycleCount"]);
            Assert.IsTrue(catalog.TryGetLevel(1604, 5, out var juice));
            Assert.AreEqual(36, juice.Parameters["damage"]);
            Assert.AreEqual(5, juice.Parameters["dropCount"]);
            Assert.AreEqual(0.3f, juice.Parameters["dropSpacing"]);
            Assert.AreEqual(0.2f, juice.Parameters["splashRadius"]);
        }

        [Test]
        public void FixedLandingNeedsNoEnemyAndResolvesExactlyOnceAfterFullFlight()
        {
            var model = new BulletModel(3604, "JuiceBullet", BulletFlightType.Parabolic, false,
                BulletHitMode.AreaAtLanding, 0, 0.7f, 1.2f, 0, "splash");
            var aim = new Vector3(2, 3, 0);
            var action = new CombatActionSnapshot(704, 1, BattleSide.Player, 1604, Array.Empty<int>(),
                CombatOutcomeTag.SingleDamage, 12, Vector3.zero, aim, 1);
            var calls = 0;
            var state = new BulletState(new BulletLauncher(model, action), new EmptyWorld(), s =>
            {
                calls++;
                Assert.IsTrue(s.IsResolved);
                Assert.AreEqual(aim, s.Position);
                Assert.IsTrue(s.Advance(1));
            });
            Assert.IsFalse(state.Advance(0.35f));
            Assert.That(state.Position.y, Is.GreaterThan(aim.y * 0.5f));
            Assert.IsFalse(state.Advance(0));
            Assert.IsTrue(state.Advance(0.35f));
            Assert.IsTrue(state.Advance(1));
            Assert.AreEqual(1, calls);
        }

        private sealed class CandidateRandom : IShopRandom
        {
            private readonly double _sample;
            public CandidateRandom(int index, int count) { _sample = (index + 0.5) / count; }
            public double NextDouble() => _sample;
            public int NextInt(int maxExclusive) => maxExclusive - 1;
        }

        private sealed class EmptyWorld : IProjectileWorld
        {
            public bool IsRunning => true;
            public bool IsValid(long battleId) => battleId == 704;
            public Bounds GetBattleBounds(BattleSide side) => new Bounds(Vector3.zero, Vector3.one * 100);
            public IReadOnlyList<BulletTarget> GetTargets(BattleSide side) => Array.Empty<BulletTarget>();
            public bool TryGetPosition(BattleSide side, int id, out Vector3 position) { position = default; return false; }
            public bool TryGetDeathPosition(BattleSide side, int id, out Vector3 position) { position = default; return false; }
        }
    }
}
