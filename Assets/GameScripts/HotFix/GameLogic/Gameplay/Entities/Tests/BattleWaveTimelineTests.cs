using System;
using System.Collections.Generic;
using System.Linq;
using GameConfig.wave;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class BattleWaveTimelineTests
    {
        [Test]
        public void WindowIsLeftClosedAndRightOpenWithoutRoundingSpawnTimes()
        {
            var timeline = BattleWaveTimeline.FromTables(WaveTestData.Tables(
                new[] { WaveTestData.Plan(1, 1) }, new[] { WaveTestData.Spawn(1, 0, 20, 2001, 11) }));
            var due = new List<BattleWaveTimeline.SpawnCommand>();
            timeline.CollectDue(0, due); Assert.AreEqual(1, due.Count);
            timeline.CollectDue(1.8, due); Assert.IsEmpty(due);
            timeline.CollectDue(20d / 11d, due); Assert.AreEqual(1, due.Count);
            Assert.That(due[0].SpawnTime, Is.EqualTo(20d / 11d).Within(1e-10));
            timeline.CollectDue(20, due); Assert.AreEqual(9, due.Count);
            Assert.That(due.Last().SpawnTime, Is.EqualTo(200d / 11d).Within(1e-10));
            Assert.IsTrue(timeline.IsAllSpawnsCompleted);
            timeline.CollectDue(1000, due); Assert.IsEmpty(due);
        }

        [Test]
        public void OverlappingGroupsAndWavesProduceChronologicalCommandsOnce()
        {
            var timeline = BattleWaveTimeline.FromTables(WaveTestData.Tables(
                new[] { WaveTestData.Plan(1, 1, 2), WaveTestData.Plan(2, 3) },
                new[] { WaveTestData.Spawn(1, 0, 8, 2001, 4), WaveTestData.Spawn(2, 1, 3, 2002, 2),
                    WaveTestData.Spawn(3, 3, 5, 2003, 1) }));
            var due = new List<BattleWaveTimeline.SpawnCommand>();
            timeline.CollectDue(10, due);
            CollectionAssert.AreEqual(new double[] { 0, 1, 2, 2, 3, 4, 6 }, due.Select(c => c.SpawnTime));
            Assert.AreEqual(2, timeline.CurrentWave);
            Assert.AreEqual(1, due.Last().Group.Wave, "下一波开始后，前波生成组仍按自己的窗口出兵");
            timeline.CollectDue(10, due); Assert.IsEmpty(due);
        }

        [TestCase(0, 0, 1)]
        [TestCase(-1, 2, 1)]
        [TestCase(0, 2, 0)]
        public void InvalidWindowOrCountStopsPreparation(int start, int end, int count)
        {
            Assert.Throws<ArgumentException>(() => BattleWaveTimeline.FromTables(WaveTestData.Tables(
                new[] { WaveTestData.Plan(1, 1) }, new[] { WaveTestData.Spawn(1, start, end, 2001, count) })));
        }

        [Test]
        public void RejectsMissingSharedGroupsAndNonIncreasingWaveStart()
        {
            foreach (var plans in new[] {
                new[] { WaveTestData.Plan(1, 99) },
                new[] { WaveTestData.Plan(1, 1), WaveTestData.Plan(2, 1) },
                new[] { WaveTestData.Plan(1, 1), WaveTestData.Plan(3, 2) },
                new[] { WaveTestData.Plan(1, 1), WaveTestData.Plan(2, 2) } })
                Assert.Throws<ArgumentException>(() => BattleWaveTimeline.FromTables(WaveTestData.Tables(plans,
                    new[] { WaveTestData.Spawn(1, 0, 2, 2001, 1), WaveTestData.Spawn(2, 0, 3, 2001, 1) })));
        }

        [Test]
        [TestCase("EnemyType", 4)]
        [TestCase("CanDropRogue", 2)]
        [TestCase("ControlResist", 1.1f)]
        [TestCase("KnockbackResist", -0.1f)]
        [TestCase("Id", 9999)]
        public void InvalidEnemyStopsPreparationBeforeItsLaterWave(string field, object value)
        {
            var tables = WaveTestData.Tables(
                new[] { WaveTestData.Plan(1, 1), WaveTestData.Plan(2, 2) },
                new[] { WaveTestData.Spawn(1, 0, 20, 2001, 1), WaveTestData.Spawn(2, 20, 40, 2002, 1) });
            var enemy = tables.TbEnemy.Get(2002);
            typeof(GameConfig.enemy.Enemy).GetField(field).SetValue(enemy, value);
            Assert.AreEqual(value, typeof(GameConfig.enemy.Enemy).GetField(field).GetValue(enemy));
            var error = Assert.Throws<ArgumentException>(() => BattleWaveTimeline.FromTables(tables));
            StringAssert.Contains("WaveSpawn 2", error.Message);
            StringAssert.Contains("2002", error.Message);
        }

        [Test]
        public void DemoTablesExecuteTwentyWavesWithTwoHundredEnemiesPerSide()
        {
            var tables = new GameConfig.Tables(name => new Luban.ByteBuf(System.IO.File.ReadAllBytes(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var timeline = BattleWaveTimeline.FromTables(tables);
            var due = new List<BattleWaveTimeline.SpawnCommand>();
            timeline.CollectDue(400, due);
            Assert.AreEqual(200, due.Count);
            Assert.AreEqual(20, timeline.CurrentWave);
            Assert.AreEqual(4, due.Count(c => c.Group.Config.EnemyType == 2));
            Assert.AreEqual(2, due.Count(c => c.Group.Config.EnemyType == 3));
            Assert.IsTrue(due.Where(c => c.Group.Wave <= 10).All(c => c.Group.Cycle == 1));
            Assert.IsTrue(due.Where(c => c.Group.Wave > 10).All(c => c.Group.Cycle == 2));
        }
    }
}
