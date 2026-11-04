using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class PoisonBuffTests
    {
        private static readonly BuffModel Poison = new BuffModel(2507, "poison");
        private static SkillOwnerRef Source(int id) => SkillOwnerRef.Entity(501, BattleSide.Player, id);
        private static void Add(BuffContainer container, int source, int damage, float duration = 5f,
            float interval = 1f, IBuffBehavior behavior = null) => container.Add(Poison, Source(source), duration,
                new Dictionary<string, float> { { "poisonDamage", damage }, { "poisonInterval", interval },
                    { "poisonMaxLayers", 3 } }, behavior);

        [Test]
        public void SameCasterKeepsThreeIndependentLayersAndDoesNotRefreshThem()
        {
            using (var container = new BuffContainer())
            {
                Add(container, 1, 10); container.Advance(0.2f);
                Add(container, 1, 10); container.Advance(0.2f); Add(container, 1, 10);
                Assert.AreEqual(3, container.Count);
                Assert.IsTrue(container.Items.All(b => b.Source.Equals(Source(1)) && b.Stacks == 1));
                Assert.That(container.Items[0].Remaining, Is.EqualTo(4.6f).Within(0.00001));
                Assert.That(container.Items[1].Remaining, Is.EqualTo(4.8f).Within(0.00001));
                Assert.AreEqual(30, container.Items.Sum(b => b.Get("poisonDamage")));
            }
        }

        [Test]
        public void MixedCastersCanContributeOneAndTwoLayers()
        {
            using (var container = new BuffContainer())
            {
                Add(container, 1, 5); Add(container, 2, 7); Add(container, 2, 7);
                Assert.AreEqual(3, container.Count);
                Assert.AreEqual(2, container.Items.Count(b => b.Source.Equals(Source(2))));
                Assert.AreEqual(19, container.Items.Sum(b => b.Get("poisonDamage")));
            }
        }

        [TestCase(5, 5)] [TestCase(6, 6)] [TestCase(0.9f, 0)] [TestCase(2.5f, 2)]
        public void FirstTickWaitsFullIntervalAndExpiryTickIsIncluded(float duration, int expected)
        {
            using (var container = new BuffContainer())
            {
                var ticks = new List<int>();
                Add(container, 1, 5, duration, behavior: new TickHook((c, b) => ticks.Add(1)));
                Assert.IsEmpty(ticks);
                container.Advance(duration + 2);
                Assert.AreEqual(expected, ticks.Count);
                Assert.AreEqual(0, container.Count);
            }
        }

        [Test]
        public void EqualTotalDamageIsRejectedAndStrictlyHigherReplacesOldestMinimum()
        {
            using (var container = new BuffContainer())
            {
                Add(container, 1, 5); Add(container, 2, 5); Add(container, 3, 10);
                var original = container.Items.ToArray();
                Add(container, 4, 5);
                CollectionAssert.AreEqual(original, container.Items);
                Add(container, 4, 6);
                Assert.AreEqual(3, container.Count);
                Assert.IsFalse(container.Items.Contains(original[0]));
                Assert.IsTrue(container.Items.Contains(original[1]));
                Assert.IsTrue(container.Items.Any(b => b.Source.Equals(Source(4))));
            }
        }

        [Test]
        public void RemainingDamageIncludesPartialTickProgressAndNewLayerStartsFresh()
        {
            using (var container = new BuffContainer())
            {
                var ticks = new List<int>();
                var hook = new TickHook((c, b) => ticks.Add((int)b.Get("poisonDamage")));
                Add(container, 1, 5, 1, behavior: hook); Add(container, 2, 7, behavior: hook);
                Add(container, 3, 7, behavior: hook); container.Advance(0.9f);
                var first = container.Items[0];
                Add(container, 4, 1, behavior: hook); // 5 == 剩余一跳5，必须拒绝。
                Assert.IsTrue(container.Items.Contains(first));
                Add(container, 4, 2, behavior: hook); // 新层10 > 旧层5，替换。
                Assert.IsFalse(container.Items.Contains(first));
                container.Advance(0.1f);
                CollectionAssert.AreEqual(new[] { 7, 7 }, ticks);
                container.Advance(0.9f);
                CollectionAssert.AreEqual(new[] { 7, 7, 2 }, ticks);
            }
        }

        [Test]
        public void DifferentIntervalsUseChronologicalOrderDuringLargeAdvance()
        {
            using (var container = new BuffContainer())
            {
                var ticks = new List<int>();
                var hook = new TickHook((c, b) => ticks.Add((int)b.Get("poisonDamage")));
                Add(container, 1, 1, 2, 1, hook); Add(container, 1, 2, 2, 0.5f, hook);
                container.Advance(2);
                CollectionAssert.AreEqual(new[] { 2, 1, 2, 2, 1, 2 }, ticks);
            }
        }

        [Test]
        public void ClearDuringTickStopsOtherLayersAndRemainingCatchupTicks()
        {
            using (var container = new BuffContainer())
            {
                var calls = 0;
                var hook = new TickHook((c, b) => { calls++; c.Clear(); });
                Add(container, 1, 10, behavior: hook); Add(container, 2, 10, behavior: hook);
                container.Advance(5);
                Assert.AreEqual(1, calls); Assert.AreEqual(0, container.Count);
            }
        }

        private sealed class TickHook : BuffBehavior
        {
            private readonly Action<BuffContainer, BuffObj> _tick;
            internal TickHook(Action<BuffContainer, BuffObj> tick) { _tick = tick; }
            public override void OnTick(BuffContainer container, BuffObj buff) => _tick(container, buff);
        }

        [Test]
        public void ReplacementRemovalCallbackCannotExceedCapacity()
        {
            using (var container = new BuffContainer())
            {
                Add(container, 1, 1, behavior: new RemoveHook(c => Add(c, 5, 2)));
                Add(container, 2, 2); Add(container, 3, 3);
                Add(container, 4, 4);
                Assert.AreEqual(3, container.Count);
                Assert.IsFalse(container.Items.Any(b => b.Source.Equals(Source(5))));
                Assert.AreEqual(3, container.Items.Select(b => b.InstanceId).Distinct().Count());
            }
        }
        private sealed class RemoveHook : BuffBehavior
        {
            private readonly Action<BuffContainer> _remove;
            internal RemoveHook(Action<BuffContainer> remove) { _remove = remove; }
            public override void OnRemoved(BuffContainer container, BuffObj buff) => _remove(container);
        }
    }
}
