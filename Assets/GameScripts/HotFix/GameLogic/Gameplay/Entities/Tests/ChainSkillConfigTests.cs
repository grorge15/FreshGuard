using System;
using System.IO;
using System.Linq;
using GameConfig;
using GameConfig.bullet;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class ChainSkillConfigTests
    {
        [Test]
        public void ExportedTablesKeepUnfinishedRowsButCatalogContainsOnlyEnabledDependencies()
        {
            var tables = LoadTables();
            var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.AreEqual(16, tables.TbSkill.DataList.Count);
            Assert.AreEqual(80, tables.TbSkillLevel.DataList.Count);
            Assert.AreEqual(8, tables.TbBullet.DataList.Count);
            Assert.AreEqual(14, tables.TbBuff.DataList.Count);
            Assert.AreEqual(7, tables.TbBuff.DataList.Count(row => row.IsShopItem));
            CollectionAssert.AreEquivalent(new[] { 1500, 1501, 1502, 1503, 1504, 1505, 1506, 1507, 1600, 1601, 1602, 1603, 1604, 1605, 1606, 1607 },
                tables.TbSkill.DataList.Where(row => row.Enabled).Select(row => row.Id));

            foreach (var row in tables.TbSkill.DataList)
                Assert.AreEqual(row.Enabled, catalog.TryGet(row.Id, out _), "技能 " + row.Id);
            foreach (var row in tables.TbSkillLevel.DataList)
                Assert.AreEqual(tables.TbSkill.Get(row.SkillId).Enabled,
                    catalog.TryGetLevel(row.SkillId, row.Level, out _), "等级记录 " + row.Id);
            Assert.AreEqual(16, tables.TbSkill.DataList.Count(row => catalog.TryGet(row.Id, out _)));
            Assert.AreEqual(80, tables.TbSkillLevel.DataList.Count(row => catalog.TryGetLevel(row.SkillId, row.Level, out _)));
            CollectionAssert.AreEquivalent(new[] { 3500, 3501, 3502, 3600, 3602, 3603, 3604, 3605 },
                tables.TbBullet.DataList.Where(row => catalog.TryGetBullet(row.Id, out _)).Select(row => row.Id));
            CollectionAssert.AreEquivalent(new[] { 2505, 2507, 2600, 2601, 2602, 2603, 2604 },
                tables.TbBuff.DataList.Where(row => catalog.TryGetBuff(row.Id, out _)).Select(row => row.Id));
            Assert.AreEqual(BulletHitMode.AreaAtLanding, tables.TbBullet.Get(3604).HitMode);
            Assert.IsTrue(catalog.TryGetBullet(3604, out _), "榨汁机落地区域弹已启用。");
            Assert.IsTrue(catalog.TryGet(1600, out _), "第五次攻击使用独立闪电链技能。");
        }

        [Test]
        public void InductionLevelsOwnDamageAndCycleParametersAndUseSharedProjectileResources()
        {
            var tables = LoadTables();
            var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGet(1605, out var skill));
            Assert.AreEqual(SkillType.Robot, skill.Type);
            Assert.AreEqual("robot.single", skill.TimelineKey);
            Assert.AreEqual(3605, skill.BulletId);
            CollectionAssert.AreEqual(new[] { 2602 }, skill.PassiveBuffIds);
            for (var level = 1; level <= 5; level++)
            {
                Assert.IsTrue(catalog.TryGetLevel(1605, level, out var values));
                Assert.AreEqual(10 + level * 10, values.Parameters["damage"]);
                Assert.AreEqual(5f, values.Parameters["attackCycleCount"]);
                Assert.IsFalse(values.Parameters.ContainsKey("cycleBulletId"));
                Assert.IsTrue(catalog.TryGetLevel(1600, level, out values));
                Assert.AreEqual(10 + level * 10, values.Parameters["damage"]);
                Assert.AreEqual(.25f, values.Parameters["castWindup"]);
                Assert.AreEqual(5f, values.Parameters["chainTargetCount"]);
                Assert.AreEqual(0.9f, values.Parameters["chainRange"]);
                Assert.AreEqual(0.7f, values.Parameters["chainDamageRatio"]);
                Assert.IsEmpty(values.HitBuffs, "攻击循环被动不能成为命中Buff。");
            }
            foreach (var id in new[] { 3600, 3605 })
            {
                Assert.IsTrue(catalog.TryGetBullet(id, out var bullet));
                Assert.AreEqual("InductionBolt", bullet.Prefab);
                Assert.AreEqual(BulletFlightType.Straight, bullet.FlightType);
                Assert.IsFalse(bullet.TrackTarget);
                Assert.AreEqual(BulletHitMode.FirstEnemy, bullet.HitMode);
                Assert.AreEqual(20f, bullet.Speed);
                Assert.AreEqual(0.05f, bullet.HitRadius);
                Assert.AreEqual(id == 3600 ? "chain" : "direct", bullet.BehaviorKey);
                Assert.AreEqual("LightningChainEffect", bullet.HitEffectPrefab);
            }
            Assert.IsTrue(catalog.TryGetBuff(2602, out var passive));
            Assert.AreEqual("attack_cycle", passive.BehaviorKey);
            Assert.AreEqual(1605, passive.CountSkillId);
            Assert.AreEqual(1, passive.MaxStacks);
            Assert.AreEqual(1600, tables.TbBuff.Get(2602).TriggerSkillId);
        }

        [Test]
        public void InductionRobotCreatesItsOwnEntityAndUsesExistingConfiguredPresentation()
        {
            var tables = LoadTables();
            var config = tables.TbRobot.Get(701);
            Assert.AreEqual(1605, config.SkillId);
            Assert.IsTrue(File.Exists(Path.Combine(Application.dataPath, "AssetRaw/Actor/Prefabs/Robots", config.RobotPrefab + ".prefab")));
            Assert.IsTrue(File.Exists(Path.Combine(Application.dataPath, "AssetRaw/Actor/Sprites/Robots/RobotsIcon", config.RobotIcon + ".png")));
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(config, BattleSide.Player, 5);
                Assert.IsInstanceOf<InductionRobotEntity>(robot);
                Assert.AreEqual(701, robot.ConfigId);
                Assert.AreEqual(1605, robot.SkillId);
                Assert.AreEqual(5, robot.Level);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ShopSlotsKeepOriginalCandidateIndicesAndGiveNinthTheEighthWeight(int slot)
        {
            var package = LoadTables().TbDrop.Get(slot).Package;
            Assert.GreaterOrEqual(package.Length, 18);
            Assert.AreEqual(0, package.Length % 2);
            for (var index = 0; index < 9; index++)
                Assert.AreEqual(index + 1, package[index * 2], "候选使用原序号，不能过滤后重新编号。");
            Assert.AreEqual(package[15], package[17]);
            Assert.Greater(package[17], 0);
        }

        [TestCase("AttackCycleCount", 0)]
        [TestCase("ChainTargetCount", 0)]
        [TestCase("CastWindup", 0f)]
        [TestCase("CastWindup", float.NaN)]
        [TestCase("ChainRange", 0f)]
        [TestCase("ChainRange", float.NaN)]
        [TestCase("ChainDamageRatio", 0f)]
        [TestCase("ChainDamageRatio", 1.01f)]
        [TestCase("ChainDamageRatio", float.PositiveInfinity)]
        public void InvalidEnabledCycleConfigurationFailsBeforeBattle(string field, object value)
        {
            var tables = LoadTables();
            SetField(tables.TbSkillLevel.Get(field == "AttackCycleCount" ? 160501 : 160001), field, value);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [Test]
        public void IndependentSkillCanPrepareItsOwnProjectilePrefab()
        {
            var tables = LoadTables();
            SetField(tables.TbBullet.Get(3600), "Prefab", "SeparateChainPrefab");
            Assert.DoesNotThrow(() => SkillConfigAdapter.FromTables(tables));
        }

        [TestCase(3600)]
        [TestCase(3605)]
        public void UnknownEnabledProjectileBehaviorFailsBeforeBattle(int bulletId)
        {
            var tables = LoadTables();
            SetField(tables.TbBullet.Get(bulletId), "BehaviorKey", "unknown_behavior");
            var exception = Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
            StringAssert.Contains("未知弹丸行为", exception.Message);
        }

        [Test]
        public void UnknownEnabledPassiveBehaviorFailsBeforeBattle()
        {
            var tables = LoadTables();
            SetField(tables.TbBuff.Get(2602), "BehaviorKey", "unknown_behavior");
            var exception = Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
            StringAssert.Contains("未知Buff行为", exception.Message);
        }

        [Test]
        public void InvalidDisabledDependenciesDoNotBlockTheEnabledCatalog()
        {
            var tables = LoadTables();
            SetField(tables.TbSkill.Get(1603), "Enabled", false);
            SetField(tables.TbSkill.Get(1604), "Enabled", false);
            SetField(tables.TbBullet.Get(3604), "BehaviorKey", "unfinished_behavior");
            SetField(tables.TbBullet.Get(3604), "Prefab", string.Empty);
            SetField(tables.TbBuff.Get(2601), "BehaviorKey", "unfinished_behavior");
            SetField(tables.TbSkillLevel.Get(160401), "Damage", -1);
            var catalog = SkillConfigAdapter.FromTables(tables);
            Assert.IsTrue(catalog.TryGet(1605, out _));
            Assert.IsFalse(catalog.TryGet(1604, out _));
            Assert.IsFalse(catalog.TryGetBullet(3604, out _));
            Assert.IsFalse(catalog.TryGetBuff(2601, out _));
        }

        [TestCase(2505)]
        [TestCase(2507)]
        [TestCase(2600)]
        [TestCase(2601)]
        [TestCase(2602)]
        [TestCase(2603)]
        [TestCase(2604)]
        public void BuffMaxLevelDoesNotRaiseRuntimeStacksOrClampRobotSkillLevels(int buffId)
        {
            var tables = LoadTables();
            SetField(tables.TbBuff.Get(buffId), "MaxLevel", 3);

            var catalog = SkillConfigAdapter.FromTables(tables);

            Assert.IsTrue(catalog.TryGetBuff(buffId, out var buff));
            Assert.AreEqual(1, buff.MaxStacks);
            Assert.IsTrue(catalog.TryGet(1505, out var frost));
            Assert.AreEqual(5, frost.MaxLevel);
            Assert.IsTrue(catalog.TryGetLevel(1505, 5, out var level));
            Assert.AreEqual(0.4f, level.Parameters["slow"]);
            Assert.AreEqual(3f, level.Parameters["duration"]);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveRequiredBuffMaxLevelFailsBeforeBattle(int maxLevel)
        {
            var tables = LoadTables();
            SetField(tables.TbBuff.Get(2505), "MaxLevel", maxLevel);

            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        private static Tables LoadTables() => new Tables(name => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));

        private static void SetField(object row, string field, object value)
        {
            var info = row.GetType().GetField(field);
            Assert.IsNotNull(info, field);
            info.SetValue(row, value);
        }
    }
}
