using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class BuffSelectSessionTests
    {
        private static BuffSelectRequest Request(float seconds = 5f)
        {
            return new BuffSelectRequest(new[] {
                new BuffSelectOption(101, 3, BuffQuality.Gold, "A", ""),
                new BuffSelectOption(102, 1, BuffQuality.Purple, "B", ""),
                new BuffSelectOption(103, 3, BuffQuality.Blue, "C", "")
            }, null, seconds);
        }

        [Test]
        public void TimeoutSelectsFirstExactlyOnce()
        {
            var request = Request();
            var session = new BuffSelectSession(request);
            Assert.That(session.Advance(4.9f, out _), Is.False);
            Assert.That(session.Advance(0.2f, out var result), Is.True);
            Assert.That(result.Index, Is.Zero);
            Assert.That(result.Option, Is.SameAs(request.Options[0]));
            Assert.That(result.IsAutomatic, Is.True);
            Assert.That(session.TrySelect(2, out _), Is.False);
            Assert.That(session.Advance(10f, out _), Is.False);
        }

        [Test]
        public void ClickWinsAndPreventsTimeoutOrSecondClick()
        {
            var session = new BuffSelectSession(Request());
            Assert.That(session.TrySelect(1, out var result), Is.True);
            Assert.That(result.Index, Is.EqualTo(1));
            Assert.That(result.IsAutomatic, Is.False);
            Assert.That(session.Advance(5f, out _), Is.False);
            Assert.That(session.TrySelect(0, out _), Is.False);
        }

        [Test]
        public void CancelStopsOldSessionAndNewSessionStartsFresh()
        {
            var old = new BuffSelectSession(Request());
            old.Advance(4f, out _);
            old.Cancel();
            Assert.That(old.Advance(2f, out _), Is.False);
            Assert.That(old.TrySelect(1, out _), Is.False);
            var next = new BuffSelectSession(Request());
            Assert.That(next.RemainingSeconds, Is.EqualTo(5f));
            Assert.That(next.TrySelect(2, out var result), Is.True);
            Assert.That(result.Option.BuffId, Is.EqualTo(103));
        }

        [Test]
        public void ZeroDurationSelectsFirstOnFirstAdvance()
        {
            var session = new BuffSelectSession(Request(0));
            Assert.That(session.Advance(0, out var result), Is.True);
            Assert.That(result.Index, Is.Zero);
        }

        [Test]
        public void InvalidClickDoesNotConsumeChoiceAndLevelIsIndependentOfQuality()
        {
            var request = Request();
            var session = new BuffSelectSession(request);
            Assert.That(session.TrySelect(-1, out _), Is.False);
            Assert.That(session.TrySelect(3, out _), Is.False);
            Assert.That(session.TrySelect(0, out var result), Is.True);
            Assert.That(result.Option.Level, Is.EqualTo(3));
            Assert.That(result.Option.Quality, Is.EqualTo(BuffQuality.Gold));
        }

        [Test]
        public void RequestRejectsIncompleteCandidatesAndInvalidDuration()
        {
            Assert.Throws<ArgumentException>(() => new BuffSelectRequest(new BuffSelectOption[2], null));
            Assert.Throws<ArgumentException>(() => new BuffSelectRequest(new BuffSelectOption[3], null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Request(float.NaN));
        }
    }
}
