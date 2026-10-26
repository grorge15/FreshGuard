using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
namespace GameLogic.Tests
{
    public sealed class MatchBuffTests
    {
        private sealed class Sequence : IShopRandom
        {
            private readonly Queue<int> _values;
            public Sequence(params int[] values) { _values = new Queue<int>(values); }
            public int NextInt(int max) => _values.Dequeue();
            public double NextDouble() => throw new InvalidOperationException();
        }
        private static MatchBuff[] Pool() => new[] {new MatchBuff(1,"a","a",9),new MatchBuff(2,"b","b",7),new MatchBuff(3,"c","c",1)};
        [Test] public void ZeroPlayerKindsConsumesNoRandomAndCarriesNothing()
            => Assert.That(MatchBuffGenerator.Draw(Pool(),0,new Sequence()),Is.Empty);
        [Test] public void InclusiveMaximumSelectsAllKindsWithoutReplacementAndOneLayer()
        {
            var buffs = MatchBuffGenerator.Draw(Pool(),3,new Sequence(2,2,0,0));
            Assert.That(buffs.Select(x=>x.BuffId),Is.EqualTo(new[]{3,1,2}));
            Assert.That(buffs.All(x=>x.Layers==1),Is.True);
        }
        [Test] public void CanDrawUnlockedBuffNotCarriedByPlayer()
            => Assert.That(MatchBuffGenerator.Draw(Pool(),1,new Sequence(0,2))[0].BuffId,Is.EqualTo(3));
        [TestCase(0,1)] [TestCase(1,2)] [TestCase(2,3)]
        public void EachUniformCountIndexMapsToOneThroughN(int roll,int expected)
            => Assert.That(MatchBuffGenerator.Draw(Pool(),3,new Sequence(roll,0,0,0)).Count,Is.EqualTo(expected));
    }
    public sealed class MatchRecordTests
    {
        [Test] public void RestartRefundsMatchingEntryOnlyOnceAndAtCapacity()
        {
            var type=Type.GetType("GameLogic.MatchPlayerRecord, GameLogic");
            Assert.That(type,Is.Not.Null,"缺少匹配扣费恢复记录");
            var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
            var record=Activator.CreateInstance(type);
            type.GetMethod("BeginEntry").Invoke(record,new object[]{state,"entry",DateTimeOffset.UtcNow});
            Assert.That(state.Energy,Is.EqualTo(25));
            state.AddEnergy(4);
            Assert.That(type.GetMethod("RefundMatching").Invoke(record,new object[]{state,DateTimeOffset.UtcNow}),Is.True);
            Assert.That(state.Energy,Is.EqualTo(30));
            Assert.That(type.GetMethod("RefundMatching").Invoke(record,new object[]{state,DateTimeOffset.UtcNow}),Is.False);
        }
        [Test] public void SuccessRecordDoesNotRefundAfterRestart()
        {
            var type=Type.GetType("GameLogic.MatchPlayerRecord, GameLogic"); Assert.That(type,Is.Not.Null);
            var state=new MainMenuPlayerState(30,TimeSpan.FromMinutes(5),30,0,2500,DateTimeOffset.UtcNow);
            var record=Activator.CreateInstance(type);
            type.GetMethod("BeginEntry").Invoke(record,new object[]{state,"entry",DateTimeOffset.UtcNow});
            type.GetMethod("CommitSuccess").Invoke(record,new object[]{"entry"});
            Assert.That(type.GetMethod("RefundMatching").Invoke(record,new object[]{state,DateTimeOffset.UtcNow}),Is.False);
            Assert.That(state.Energy,Is.EqualTo(25));
        }
    }
}
