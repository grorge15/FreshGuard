using System;
using System.IO;
using GameConfig;
using GameConfig.bullet;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class RobotSkillConfigTests
    {
        [Test]
        public void RobotSkillsUseDistinctTimelinesAndLevelDamage()
        {
            var tables = LoadTables();
            var catalog = SkillConfigAdapter.FromTables(tables);
            var timelines = new[] { "robot.single", "robot.scatter", "robot.pierce", "robot.burst", "robot.impact" };
            for (var index = 0; index < timelines.Length; index++)
            {
                Assert.IsTrue(catalog.TryGet(1500 + index, out var skill));
                Assert.AreEqual(SkillType.Robot, skill.Type, "Skill " + skill.Id);
                Assert.AreEqual(timelines[index], skill.TimelineKey);
                for (var level = 1; level <= 5; level++)
                {
                    Assert.IsTrue(catalog.TryGetLevel(skill.Id, level, out var values));
                    Assert.AreEqual(10 + level * 10, values.Parameters["damage"]);
                }
            }
        }

        [Test]
        public void RobotProjectileConfigurationIsOwnedBySkillAndBulletTables()
        {
            var tables = LoadTables();
            var catalog = SkillConfigAdapter.FromTables(tables);
            foreach (var row in tables.TbSkill.DataList)
            {
                Assert.IsTrue(catalog.TryGet(row.Id, out var skill));
                Assert.IsTrue(catalog.TryGetBullet(skill.BulletId, out var bullet));
                Assert.AreEqual("RobotBullet", bullet.Prefab);
                if (row.Id == 1501 || row.Id == 1502)
                {
                    Assert.AreEqual(BulletFlightType.Straight, bullet.FlightType);
                    Assert.IsFalse(bullet.TrackTarget);
                    Assert.AreEqual(row.Id == 1501 ? BulletHitMode.FirstEnemy : BulletHitMode.PierceEnemies, bullet.HitMode);
                    Assert.AreEqual(10f, bullet.Speed);
                    Assert.AreEqual(0.1f, bullet.HitRadius);
                }
                else
                {
                    Assert.AreEqual(BulletFlightType.Parabolic, bullet.FlightType);
                    Assert.IsTrue(bullet.TrackTarget);
                    Assert.AreEqual(BulletHitMode.LockedTarget, bullet.HitMode);
                    Assert.AreEqual(0.6f, bullet.FlightTime);
                    Assert.AreEqual(1f, bullet.ArcHeight);
                }
            }
            Assert.IsNull(typeof(GameConfig.robot.Robot).GetField("BulletFlightTime"));
            Assert.IsNull(typeof(GameConfig.robot.Robot).GetField("BulletArcHeight"));
            Assert.IsNull(typeof(GameConfig.robot.Robot).GetField("BulletPrefab"));
        }

        [Test]
        public void MissingBulletReferenceRejectsConfigurationBeforeBattleStarts()
        {
            var tables = LoadTables();
            var buffer = new ByteBuf();
            buffer.WriteSize(0);
            tables.TbBullet = new TbBullet(buffer);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [TestCase(BulletFlightType.Straight, false, BulletHitMode.FirstEnemy, 0f, 0f)]
        [TestCase(BulletFlightType.Straight, true, BulletHitMode.FirstEnemy, 10f, 0f)]
        [TestCase(BulletFlightType.Parabolic, true, BulletHitMode.LockedTarget, 0f, 0f)]
        [TestCase(BulletFlightType.Parabolic, true, BulletHitMode.PierceEnemies, 0f, 0.6f)]
        public void UnsupportedBulletCombinationsRejectConfiguration(BulletFlightType flightType,
            bool trackTarget, BulletHitMode hitMode, float speed, float flightTime)
        {
            var tables = LoadTables();
            var buffer = new ByteBuf();
            buffer.WriteSize(1);
            buffer.WriteInt(3500); buffer.WriteString("RobotBullet"); buffer.WriteInt((int)flightType);
            buffer.WriteBool(trackTarget); buffer.WriteInt((int)hitMode); buffer.WriteFloat(speed);
            buffer.WriteFloat(flightTime); buffer.WriteFloat(1f); buffer.WriteFloat(0.1f);
            tables.TbBullet = new TbBullet(buffer);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [TestCase(1501, 0, 60f, 0, 0, 0f, 0f)]
        [TestCase(1501, 5, 361f, 0, 0, 0f, 0f)]
        [TestCase(1502, 0, 0f, 0, 0, 0f, 0f)]
        [TestCase(1503, 0, 0f, 0, 0, 0.2f, 0f)]
        [TestCase(1503, 0, 0f, 0, 4, 0f, 0f)]
        [TestCase(1504, 0, 0f, 0, 0, 0f, -1f)]
        public void InvalidRobotLevelParametersRejectConfiguration(int skillId, int scatterCount,
            float scatterAngle, int pierceCount, int burstCount, float burstInterval, float knockbackDistance)
        {
            var tables = LoadTables();
            var buffer = new ByteBuf();
            buffer.WriteSize(1);
            buffer.WriteInt(skillId * 100 + 1); buffer.WriteInt(skillId); buffer.WriteInt(1);
            buffer.WriteInt(20); buffer.WriteFloat(0f); buffer.WriteFloat(0f);
            buffer.WriteInt(scatterCount); buffer.WriteFloat(scatterAngle); buffer.WriteInt(pierceCount);
            buffer.WriteInt(burstCount); buffer.WriteFloat(burstInterval); buffer.WriteFloat(knockbackDistance);
            tables.TbSkillLevel = new GameConfig.skill.TbSkillLevel(buffer);
            Assert.Throws<InvalidOperationException>(() => SkillConfigAdapter.FromTables(tables));
        }

        [Test]
        public void NamedRobotParametersAndExistingFrostLevelsSurviveMigration()
        {
            var catalog = SkillConfigAdapter.FromTables(LoadTables());
            Assert.IsTrue(catalog.TryGetLevel(1501, 1, out var scatter));
            Assert.AreEqual(5f, scatter.Parameters["scatterCount"]);
            Assert.AreEqual(60f, scatter.Parameters["scatterAngle"]);
            Assert.IsTrue(catalog.TryGetLevel(1502, 1, out var pierce));
            Assert.AreEqual(3f, pierce.Parameters["pierceTargetCount"]);
            Assert.IsTrue(catalog.TryGetLevel(1503, 1, out var burst));
            Assert.AreEqual(4f, burst.Parameters["burstCount"]);
            Assert.AreEqual(0.2f, burst.Parameters["burstInterval"]);
            Assert.IsTrue(catalog.TryGetLevel(1504, 1, out var impact));
            Assert.AreEqual(1f, impact.Parameters["knockbackDistance"]);
            Assert.AreEqual(0f, impact.Parameters["slow"]);
            var damages = new[] { 20f, 28f, 40f, 55f, 77f };
            for (var level = 1; level <= damages.Length; level++)
            {
                Assert.IsTrue(catalog.TryGetLevel(1505, level, out var frost));
                Assert.AreEqual(damages[level - 1], frost.Parameters["damage"]);
                Assert.AreEqual(level < 3 ? 0.3f : 0.4f, frost.Parameters["slow"]);
                Assert.AreEqual(3f, frost.Parameters["duration"]);
                Assert.AreEqual(1, frost.HitBuffs.Count);
            }
        }

        private static Tables LoadTables() => new Tables(name => new ByteBuf(File.ReadAllBytes(
            Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
    }
}
