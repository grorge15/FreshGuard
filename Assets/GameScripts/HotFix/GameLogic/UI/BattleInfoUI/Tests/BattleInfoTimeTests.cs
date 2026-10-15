using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class BattleInfoTimeTests
    {
        [TestCase(0f, "00:00")]
        [TestCase(59.9f, "00:59")]
        [TestCase(60f, "01:00")]
        [TestCase(90f, "01:30")]
        [TestCase(3600f, "60:00")]
        [TestCase(-1f, "00:00")]
        public void WholeBattleElapsedTimeDoesNotWrapAtAnHour(float seconds, string expected)
        {
            Assert.That(BattleInfoView.FormatElapsedTime(seconds), Is.EqualTo(expected));
        }
        [Test]
        public void InvalidElapsedTimeHasSafeDisplay()
        {
            Assert.That(BattleInfoView.FormatElapsedTime(float.NaN), Is.EqualTo("00:00"));
            Assert.That(BattleInfoView.FormatElapsedTime(float.PositiveInfinity), Is.EqualTo("00:00"));
        }
    }
}
