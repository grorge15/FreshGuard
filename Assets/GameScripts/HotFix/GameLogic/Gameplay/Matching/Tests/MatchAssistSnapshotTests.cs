using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class MatchAssistSnapshotTests
    {
        [Test]
        public void ReleaseFiltersMidnightExpiryAndLimitsOpponentKinds()
        {
            var midnight = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.FromHours(8));
            var snapshot = new MatchAssistSnapshot(new[]
            {
                new MatchBuff(1802, "加速", "speed", 2, midnight.UtcTicks),
                new MatchBuff(1803, "增伤", "damage", 1, midnight.AddDays(1).UtcTicks)
            }, new[] { new MatchBuff(1804, "玻璃", "glass", 1), new MatchBuff(1805, "暴走", "rage", 1) }, midnight);
            Assert.That(snapshot.PlayerBuffs.Count, Is.EqualTo(1));
            Assert.That(snapshot.PlayerBuffs[0].BuffId, Is.EqualTo(1803));
            Assert.That(snapshot.OpponentBuffs.Count, Is.EqualTo(1));
        }

        [Test]
        public void StartedBattleKeepsItsCopiedAssistAfterMidnight()
        {
            var midnight = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.FromHours(8));
            var source = new[] { new MatchBuff(1802, "加速", "speed", 3, midnight.UtcTicks) };
            var snapshot = new MatchAssistSnapshot(source, Array.Empty<MatchBuff>(), midnight.AddSeconds(-1));
            source[0] = new MatchBuff(1803, "其他", "other", 1);
            Assert.That(snapshot.PlayerBuffs[0].BuffId, Is.EqualTo(1802));
            Assert.That(snapshot.PlayerBuffs[0].Layers, Is.EqualTo(3));
            var next = new MatchAssistSnapshot(snapshot.PlayerBuffs, Array.Empty<MatchBuff>(), midnight);
            Assert.That(next.PlayerBuffs, Is.Empty);
        }
    }
}
