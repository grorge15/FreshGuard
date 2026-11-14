using System.IO;
using System;
using System.Linq;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class StaffSkillTests
    {
        [TestCase(BattleSide.Player)]
        [TestCase(BattleSide.Opponent)]
        public void RollingRobotCandidateCanPurchaseDeployAndUpgradeBothSkills(BattleSide side)
        {
            var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
            var profile = AssetDatabase.LoadAssetAtPath<DemoBattleProfile>("Assets/AssetRaw/Configs/DemoBattleProfile.asset");
            var entry = profile.Robots.Single(r => r.RobotId == 702);
            Assert.IsTrue(entry.Unlocked); Assert.AreEqual(5, entry.ReferenceLevel);
            var candidates = profile.Robots.Where(r => r.Unlocked).Select(r => r.RobotId).ToArray();
            Assert.AreEqual(11, Array.IndexOf(candidates, 702), "保留原有候选顺序");
            using (var context = new BattleShopContext(ShopRules.FromTables(tables), candidates, candidates, tables.TbRobot.Get,
                new CandidateRandom((Array.IndexOf(candidates, 702) + .5) / candidates.Length),
                isRobotEnabled: id => tables.TbSkill.Get(tables.TbRobot.Get(id).SkillId).Enabled))
            {
                context.Battle.Configure(SkillConfigAdapter.FromTables(tables));
                var offer = context.GetState(side).FindSlot(0).Offer;
                Assert.AreEqual(ShopOperationResult.Success, context.TryPurchase(side, 0, offer.OfferId, out var robot));
                Assert.IsInstanceOf<RollingPinRobotEntity>(robot);
                Assert.AreEqual(ShopOperationResult.Success, context.TryDeploy(side, robot.InstanceId,
                    side, new BoardModel(5, 5), new BoardCoordinate(0, 0)));
                Assert.IsTrue(robot.TryUpgrade());
                Assert.IsTrue(robot.Skills.TryGet(1606, out var primary)); Assert.AreEqual(2, primary.Level);
                Assert.IsTrue(robot.Skills.TryGet(1601, out var child)); Assert.AreEqual(2, child.Level);
                Assert.IsEmpty(robot.Buffs.Items);
                Assert.AreEqual(1601, robot.AttackCycle.TriggerSkillId);
            }
        }
        [Test]
        public void RollingSkillIsEnabledWithoutProjectileAndKeepsParallelAttackCycle()
        {
            var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", file + ".bytes"))));
            var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGet(1601, out var staff), "1601应启用");
            Assert.AreEqual(0, staff.BulletId);
            Assert.AreEqual("robot.roll", staff.TimelineKey);
            Assert.IsTrue(catalog.TryGet(1606, out var primary));
            Assert.AreEqual(3500, primary.BulletId);
            Assert.IsFalse(catalog.TryGetBuff(2603, out _));
            Assert.AreEqual(1601, tables.TbRobot.Get(702).AttackCycleSkillId);
            Assert.IsFalse(catalog.TryGetBullet(3601, out _));
            for (var i = 1; i <= 5; i++)
            {
                Assert.IsTrue(catalog.TryGetLevel(1601, i, out var level));
                Assert.AreEqual("StaffBullet", level.Staff.Prefab);
                Assert.AreEqual(.8f, level.Staff.RollSpeed); Assert.AreEqual(1.2f, level.Staff.RollDistance);
                Assert.AreEqual(0, level.Parameters["damage"]);
            }
        }
        private sealed class CandidateRandom : IShopRandom
        {
            private readonly double _value;
            internal CandidateRandom(double value) { _value = value; }
            public double NextDouble() => _value;
            public int NextInt(int maxExclusive) => maxExclusive - 1;
        }
    }
}
