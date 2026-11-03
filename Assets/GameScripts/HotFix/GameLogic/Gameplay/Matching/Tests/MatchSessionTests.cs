using System;
using NUnit.Framework;
namespace GameLogic.Tests
{
    public sealed class MatchSessionTests
    {
        [TestCase(1f)]
        [TestCase(1.5f)]
        [TestCase(2f)]
        public void MatchingFinishesAtItsDeadlineBeforeBattleIsReady(float duration)
        {
            var session = new BattleMatchSession("entry", duration);
            session.Advance(duration - .125f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Matching));
            session.Advance(.125f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Success));
            Assert.That(session.MatchingSeconds, Is.EqualTo(duration));
            Assert.That(session.MatchingDuration, Is.EqualTo(duration));
            Assert.That(session.IsReady, Is.False);
            Assert.That(session.SuccessSeconds, Is.Zero);
        }
        [TestCase(1f)]
        [TestCase(2f)]
        public void FastLoadStillDisplaysSuccessForOneForegroundSecond(float duration)
        {
            var session = new BattleMatchSession("entry", duration);
            session.Advance(duration, true);
            session.MarkReady();
            session.Advance(.875f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Success));
            session.Advance(.125f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Entering));
        }
        [Test]
        public void SlowLoadWaitsOnSuccessAndBothTimersPauseInBackground()
        {
            var session = new BattleMatchSession("entry", 1.5f);
            session.Advance(.75f, true);
            session.Advance(100f, false);
            Assert.That(session.MatchingSeconds, Is.EqualTo(.75f));
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Matching));
            session.Advance(5f, true);
            Assert.That(session.MatchingSeconds, Is.EqualTo(1.5f), "卡顿不应把匹配时间累计到范围之外。");
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Success));
            session.Advance(100f, false);
            Assert.That(session.SuccessSeconds, Is.Zero);
            session.Advance(5f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Success), "加载未完成时继续展示成功界面。");
            session.MarkReady();
            session.Advance(0f, false);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Success));
            session.Advance(0f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Entering), "加载完成后无需重新等待一秒。");
        }
        [Test]
        public void CancellationIsOneShotAndRejectsSuccess()
        {
            var session = new BattleMatchSession("entry", 1f);
            Assert.That(session.TryCancel(), Is.True);
            Assert.That(session.TryCancel(), Is.False);
            session.MarkReady();
            session.Advance(5f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Cancelled));
            Assert.That(session.IsReady, Is.False);
            var success = new BattleMatchSession("entry", 1f);
            success.Advance(1f, true);
            Assert.That(success.TryCancel(), Is.False, "成功后即使还在加载也不可取消。");
        }
        [Test]
        public void FailedLoadCannotEnterBattleAfterLateReadiness()
        {
            var session = new BattleMatchSession("entry", 1f);
            session.Advance(1f, true);
            session.Fail();
            session.MarkReady();
            session.Advance(10f, true);
            Assert.That(session.Phase, Is.EqualTo(BattleMatchPhase.Failed));
            Assert.That(session.IsReady, Is.False);
        }
        [TestCase(0f)]
        [TestCase(.999f)]
        [TestCase(2.001f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void MatchingDurationMustBeWithinOneToTwoSeconds(float duration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleMatchSession("entry", duration));
        }
    }
}
