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
                if (!row.Enabled)
                {
                    Assert.IsFalse(catalog.TryGet(row.Id, out _));
                    continue;
                }
                Assert.IsTrue(catalog.TryGet(row.Id, out var skill));
                if (skill.TimelineKey == "robot.roll")
                {
                    Assert.AreEqual(0, skill.BulletId);
                    Assert.IsTrue(catalog.TryGetLevel(skill.Id, 1, out var level));
                    Assert.IsNotNull(level.Staff);
                    continue;
                }
                Assert.IsTrue(catalog.TryGetBullet(skill.BulletId, out var bullet));
                if (row.Id == 1605)
                {
                    Assert.AreEqual(3605, bullet.Id);
                    Assert.AreEqual("InductionBolt", bullet.Prefab);
                    Assert.AreEqual(BulletFlightType.Straight, bullet.FlightType);
                    Assert.IsFalse(bullet.TrackTarget);
                    Assert.AreEqual(BulletHitMode.FirstEnemy, bullet.HitMode);
                    Assert.AreEqual(20f, bullet.Speed);
                    Assert.AreEqual(0.05f, bullet.HitRadius);
                    continue;
                }
                if (row.Id == 1603 || row.Id == 1604)
                {
                    Assert.AreEqual("JuiceBullet", bullet.Prefab);
                    Assert.AreEqual(BulletFlightType.Parabolic, bullet.FlightType);
                    Assert.AreEqual(row.Id == 1603, bullet.TrackTarget);
                    Assert.AreEqual(row.Id == 1603 ? BulletHitMode.LockedTarget : BulletHitMode.AreaAtLanding, bullet.HitMode);
                    Assert.AreEqual(row.Id == 1603 ? 0.6f : 0.7f, bullet.FlightTime);
                    Assert.AreEqual(row.Id == 1603 ? 1f : 1.2f, bullet.ArcHeight);
                    continue;
                }
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
                    Assert.AreEqual(row.Id == 1602 ? 0.45f : 0.6f, bullet.FlightTime);
                    Assert.AreEqual(row.Id == 1602 ? 0.6f : 1f, bullet.ArcHeight);
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
            var row = tables.TbBullet.Get(3500);
            SetField(row, "FlightType", flightType);
            SetField(row, "TrackTarget", trackTarget);
            SetField(row, "HitMode", hitMode);
            SetField(row, "Speed", speed);
            SetField(row, "FlightTime", flightTime);
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
            var row = tables.TbSkillLevel.Get(skillId * 100 + 1);
            SetField(row, "ScatterCount", scatterCount);
            SetField(row, "ScatterAngle", scatterAngle);
            SetField(row, "PierceTargetCount", pierceCount);
            SetField(row, "BurstCount", burstCount);
            SetField(row, "BurstInterval", burstInterval);
            SetField(row, "KnockbackDistance", knockbackDistance);
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

        private static void SetField(object row, string field, object value)
            => row.GetType().GetField(field).SetValue(row, value);
    }
}
