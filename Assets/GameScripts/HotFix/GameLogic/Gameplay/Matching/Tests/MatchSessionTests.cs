using System;
using System.Reflection;
using NUnit.Framework;
namespace GameLogic.Tests
{
    public sealed class MatchSessionTests
    {
        private object NewSession() => Activator.CreateInstance(RequiredType("BattleMatchSession"), "entry");
        private static Type RequiredType(string name)
        {
            var type = Type.GetType("GameLogic." + name + ", GameLogic");
            Assert.That(type, Is.Not.Null, "缺少可执行的匹配状态机 " + name);
            return type;
        }
        private static object Call(object value, string name, params object[] args)
        {
            var method = value.GetType().GetMethod(name);
            Assert.That(method, Is.Not.Null, "匹配行为缺少 " + name);
            return method.Invoke(value, args);
        }
        private static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
        [Test]
        public void FastLoadWaitsTwoForegroundSecondsAndThenOneSuccessSecond()
        {
            var s = NewSession();
            Call(s, "MarkReady");
            Call(s, "Advance", 1.99f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Matching"));
            Call(s, "Advance", .01f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Success"));
            Call(s, "Advance", .99f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Success"));
            Call(s, "Advance", .02f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Entering"));
        }
        [Test]
        public void SlowLoadAndBackgroundDoNotSkipMatching()
        {
            var s = NewSession();
            Call(s, "Advance", 1f, true);
            Call(s, "Advance", 100f, false);
            Assert.That((float)Get(s, "MatchingSeconds"), Is.EqualTo(1f));
            Call(s, "Advance", 5f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Matching"));
            Call(s, "MarkReady");
            Call(s, "Advance", 0f, false);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Matching"));
            Call(s, "Advance", 0f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Success"));
        }
        [Test]
        public void CancellationIsOneShotAndRejectsSuccess()
        {
            var s = NewSession();
            Assert.That(Call(s, "TryCancel"), Is.True);
            Assert.That(Call(s, "TryCancel"), Is.False);
            Call(s, "MarkReady");
            Call(s, "Advance", 5f, true);
            Assert.That(Get(s, "Phase").ToString(), Is.EqualTo("Cancelled"));
            var ready = NewSession();
            Call(ready, "MarkReady"); Call(ready, "Advance", 2f, true);
            Assert.That(Call(ready, "TryCancel"), Is.False);
        }
    }
}
