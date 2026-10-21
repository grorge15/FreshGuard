using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class CombatRuntimeTests
    {
        [Test]
        public void SameModifierGroupAddsAndDifferentGroupsMultiply()
        {
            using (var runtime = new CombatRuntime(101))
            {
                runtime.AddModifier(BattleSide.Player, new PercentModifier(0.10f, CombatModifierGroup.GlobalDamage));
                runtime.AddModifier(BattleSide.Player, new PercentModifier(0.20f, CombatModifierGroup.GlobalDamage));
                runtime.AddModifier(BattleSide.Player, new PercentModifier(0.50f, CombatModifierGroup.AreaDamage));

                var snapshot = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                var result = runtime.Resolve(snapshot, new RecordingResolutionPort());

                Assert.AreEqual(195, result.Targets[0].RequestedDamage);
            }
        }

        [Test]
        public void ModifiersAddedAfterAttackCaptureDoNotChangeLockedDamage()
        {
            using (var runtime = new CombatRuntime(102))
            {
                runtime.AddModifier(BattleSide.Player, new PercentModifier(0.10f, CombatModifierGroup.GlobalDamage));
                var snapshot = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                runtime.AddModifier(BattleSide.Player, new PercentModifier(0.50f, CombatModifierGroup.GlobalDamage));

                var result = runtime.Resolve(snapshot, new RecordingResolutionPort());

                Assert.AreEqual(110, result.Targets[0].RequestedDamage);
            }
        }

        [Test]
        public void PlayerAndOpponentModifiersRemainIndependent()
        {
            using (var runtime = new CombatRuntime(103))
            {
                runtime.AddModifier(BattleSide.Player, new PercentModifier(0.50f, CombatModifierGroup.GlobalDamage));
                runtime.AddModifier(BattleSide.Opponent, new PercentModifier(0.20f, CombatModifierGroup.GlobalDamage));

                var player = runtime.Resolve(runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100)),
                    new RecordingResolutionPort());
                var opponent = runtime.Resolve(runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Opponent, 100)),
                    new RecordingResolutionPort());

                Assert.AreEqual(150, player.Targets[0].RequestedDamage);
                Assert.AreEqual(120, opponent.Targets[0].RequestedDamage);
            }
        }

        [Test]
        public void CombatHistoryDeduplicatesRobotHitsWithinWindow()
        {
            var history = new CombatHistory();

            history.RecordHit(2001, 11, 0f, isEliteOrBoss: true);
            history.RecordHit(2001, 11, 0.5f, isEliteOrBoss: true);
            history.RecordHit(2001, 12, 1f, isEliteOrBoss: true);

            Assert.AreEqual(2, history.CountDistinctRobotHits(2001, 1.5f, 2f));
            Assert.IsTrue(history.HasDistinctRobotHits(2001, 2, 1.5f, 2f));
            Assert.IsFalse(history.HasDistinctRobotHits(2001, 3, 1.5f, 2f));
        }

        [Test]
        public void UpgradingAndRemovingAModifierPreservesOldShotButChangesNewShot()
        {
            using (var runtime = new CombatRuntime(104))
            {
                var modifier = new PercentModifier(0.1f, CombatModifierGroup.GlobalDamage);
                runtime.AddModifier(BattleSide.Player, modifier);
                var old = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                modifier.Percent = 0.5f;
                var upgraded = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                runtime.RemoveModifier(BattleSide.Player, modifier);
                var removed = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                var port = new RecordingResolutionPort();
                Assert.AreEqual(110, runtime.Resolve(old, port).FinalDamage);
                Assert.AreEqual(150, runtime.Resolve(upgraded, port).FinalDamage);
                Assert.AreEqual(100, runtime.Resolve(removed, port).FinalDamage);
                Assert.Less(old.Sequence, upgraded.Sequence);
                Assert.Less(upgraded.Sequence, removed.Sequence);
            }
        }

        [TestCase(CombatOutcomeTag.SingleDamage, 100)]
        [TestCase(CombatOutcomeTag.AreaDamage, 150)]
        [TestCase(CombatOutcomeTag.DamageOverTime, 100)]
        public void GenericPercentModifierMatchesOutcomeTags(CombatOutcomeTag tag, int expected)
        {
            using (var runtime = new CombatRuntime(105))
            {
                runtime.AddModifier(BattleSide.Player,
                    new PercentCombatModifier(CombatOutcomeTag.AreaDamage, CombatModifierGroup.AreaDamage, 0.5f));
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100, tag));
                Assert.AreEqual(expected, runtime.Resolve(action, new RecordingResolutionPort()).FinalDamage);
            }
        }

        [Test]
        public void DynamicConditionUsesHitTimeButKeepsCapturedModifierLevel()
        {
            using (var runtime = new CombatRuntime(106))
            {
                var modifier = new TimeConditionModifier(0.5f);
                runtime.AddModifier(BattleSide.Player, modifier);
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                modifier.Percent = 1f;
                runtime.AdvanceTime(2f);
                Assert.AreEqual(150, runtime.Resolve(action, new RecordingResolutionPort()).FinalDamage);
                Assert.AreEqual(1, modifier.ResolvedCount);
            }
        }

        [Test]
        public void NewDynamicModifierDoesNotAffectAlreadyCapturedAttack()
        {
            using (var runtime = new CombatRuntime(107))
            {
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                runtime.AddModifier(BattleSide.Player, new TimeConditionModifier(0.5f));
                runtime.AdvanceTime(2f);
                Assert.AreEqual(100, runtime.Resolve(action, new RecordingResolutionPort()).FinalDamage);
            }
        }

        [Test]
        public void SnapshotCopiesAndDeduplicatesTargetIds()
        {
            var targets = new[] { 2001, 2001 };
            var action = new CombatActionSnapshot(1, 11, BattleSide.Player, 0, targets,
                CombatOutcomeTag.SingleDamage, 100, Vector3.zero, Vector3.one, 1);
            targets[0] = 999;
            Assert.AreEqual(1, action.TargetInstanceIds.Count);
            Assert.AreEqual(2001, action.TargetInstanceIds[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)action.TargetInstanceIds)[0] = 999);
        }

        [Test]
        public void DuplicateAndForeignSnapshotsCannotReachTheResolutionPort()
        {
            using (var runtime = new CombatRuntime(108))
            using (var foreign = new CombatRuntime(108))
            {
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                var port = new RecordingResolutionPort();
                Assert.Throws<InvalidOperationException>(() => foreign.Resolve(action, port));
                Assert.Throws<InvalidOperationException>(() => runtime.CaptureAttack(CreateAction(999, BattleSide.Player, 100)));
                runtime.Resolve(action, port);
                Assert.Throws<InvalidOperationException>(() => runtime.Resolve(action, port));
                Assert.AreEqual(1, port.Calls);
            }
        }

        [Test]
        public void DisposalClearsBothSidesAndHistoryAndInvalidatesPendingAttack()
        {
            var runtime = new CombatRuntime(109);
            runtime.AddModifier(BattleSide.Player, new PercentModifier(0.1f, CombatModifierGroup.GlobalDamage));
            runtime.AddModifier(BattleSide.Opponent, new PercentModifier(0.2f, CombatModifierGroup.GlobalDamage));
            var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
            runtime.History.RecordHit(2001, 11, 0f, true);
            runtime.Dispose(); runtime.Dispose();
            Assert.AreEqual(0, runtime.GetModifierCount(BattleSide.Player));
            Assert.AreEqual(0, runtime.GetModifierCount(BattleSide.Opponent));
            Assert.AreEqual(0, runtime.History.CountDistinctRobotHits(2001, 0f, 2f));
            Assert.Throws<ObjectDisposedException>(() => runtime.Resolve(action, new RecordingResolutionPort()));
        }

        [Test]
        public void IgnoredTargetsDoNotCreateHistoryOrActualTags()
        {
            using (var runtime = new CombatRuntime(110))
            {
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                var result = runtime.Resolve(action, new RecordingResolutionPort { Result = DamageResult.Ignored });
                Assert.AreEqual(CombatOutcomeTag.None, result.ActualTags);
                Assert.AreEqual(0, result.HitCount);
                Assert.AreEqual(0, runtime.History.CountDistinctRobotHits(2001, 0f, 2f));
            }
        }

        [Test]
        public void SuccessfulEliteHitsEnterHistoryBeforeResolvedCallbacksAndExpire()
        {
            using (var runtime = new CombatRuntime(111))
            {
                var observer = new HistoryObserver();
                runtime.AddModifier(BattleSide.Player, observer);
                runtime.AdvanceTime(1f);
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                runtime.Resolve(action, new RecordingResolutionPort());
                Assert.AreEqual(1, observer.DistinctHits);
                runtime.AdvanceTime(31f);
                Assert.AreEqual(0, runtime.History.CountDistinctRobotHits(2001, runtime.BattleTime, 30f));
            }
        }

        [Test]
        public void TargetQueryRemovesMissingTargetsBeforeDynamicCondition()
        {
            using (var runtime = new CombatRuntime(112))
            {
                var probe = new DynamicProbeModifier();
                runtime.AddModifier(BattleSide.Player, probe);
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                var result = runtime.Resolve(action, new EmptyTargetPort());
                Assert.AreEqual(0, probe.State.SeenHitCount);
                Assert.AreEqual(1, probe.State.ModifyCalls);
                Assert.AreEqual(0, result.HitCount);
                Assert.AreEqual(0, result.Targets.Count);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ResolvedCallbacksKeepCapturedConfiguration(bool dynamic)
        {
            using (var runtime = new CombatRuntime(115))
            {
                ProbeModifier modifier = dynamic ? new DynamicProbeModifier() : new ProbeModifier();
                modifier.Percent = 0.1f;
                runtime.AddModifier(BattleSide.Player, modifier);
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                modifier.Percent = 0.5f;
                runtime.Resolve(action, new RecordingResolutionPort());
                Assert.AreEqual(0.1f, modifier.State.ResolvedPercent);
            }
        }

        [Test]
        public void RemovedModifierCannotBeReassignedToOtherSideWhileOldShotExists()
        {
            using (var runtime = new CombatRuntime(116))
            {
                var modifier = new ProbeModifier();
                runtime.AddModifier(BattleSide.Player, modifier);
                runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                runtime.RemoveModifier(BattleSide.Player, modifier);
                Assert.Throws<InvalidOperationException>(() => runtime.AddModifier(BattleSide.Opponent, modifier));
            }
        }

        [Test]
        public void DisposalDuringCaptureStopsFollowingModifiersAndDoesNotReturnSnapshot()
        {
            using (var runtime = new CombatRuntime(117))
            {
                var stop = new ProbeModifier { OnModify = _ => runtime.Dispose() };
                var next = new ProbeModifier();
                runtime.AddModifier(BattleSide.Player, stop);
                runtime.AddModifier(BattleSide.Player, next);
                Assert.Throws<ObjectDisposedException>(() =>
                    runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100)));
                Assert.AreEqual(0, next.State.ModifyCalls);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void DisposalBeforeResolutionStopsFollowingModifiersAndPort(bool duringTargetQuery)
        {
            using (var runtime = new CombatRuntime(118))
            {
                var stop = new DynamicProbeModifier { OnModify = _ => runtime.Dispose() };
                var next = new DynamicProbeModifier();
                if (!duringTargetQuery) runtime.AddModifier(BattleSide.Player, stop);
                runtime.AddModifier(BattleSide.Player, next);
                var action = runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100));
                var port = new EmptyTargetPort { OnQuery = duringTargetQuery ? (Action)(() => runtime.Dispose()) : null };
                Assert.Throws<ObjectDisposedException>(() => runtime.Resolve(action, port));
                Assert.AreEqual(0, next.State.ModifyCalls);
                Assert.AreEqual(0, port.Calls);
            }
        }

        [Test]
        public void SharedMutableModifierInstanceIsRejectedAcrossSides()
        {
            using (var runtime = new CombatRuntime(113))
            {
                var modifier = new PercentModifier(0.1f, CombatModifierGroup.GlobalDamage);
                runtime.AddModifier(BattleSide.Player, modifier);
                Assert.Throws<InvalidOperationException>(() => runtime.AddModifier(BattleSide.Opponent, modifier));
            }
        }

        [TestCase(-2f, 0)] [TestCase(-1f, 0)] [TestCase(0.5f, 150)]
        public void DamageCannotBecomeNegative(float percent, int expected)
        {
            using (var runtime = new CombatRuntime(114))
            {
                runtime.AddModifier(BattleSide.Player, new PercentModifier(percent, CombatModifierGroup.GlobalDamage));
                var result = runtime.Resolve(runtime.CaptureAttack(CreateAction(runtime.BattleId, BattleSide.Player, 100)),
                    new RecordingResolutionPort());
                Assert.AreEqual(expected, result.FinalDamage);
            }
        }

        private static CombatActionSnapshot CreateAction(long battleId, BattleSide side, int damage, CombatOutcomeTag tags = CombatOutcomeTag.SingleDamage)
        {
            return new CombatActionSnapshot(
                battleId: battleId,
                sourceInstanceId: side == BattleSide.Player ? 11 : 12,
                sourceSide: side,
                skillId: 0,
                targetInstanceIds: new[] { 2001 },
                outcomeTags: tags,
                baseDamage: damage,
                origin: Vector3.zero,
                aimPoint: Vector3.one,
                sequence: 1);
        }

        private sealed class PercentModifier : ICombatModifier
        {
            public float Percent;
            private readonly CombatModifierGroup _group;

            public PercentModifier(float percent, CombatModifierGroup group)
            {
                Percent = percent;
                _group = group;
            }

            public void Modify(CombatOutcomeDraft outcome, CombatModifierContext context)
            {
                outcome.AddPercent(_group, Percent);
            }

            public void OnResolved(CombatResolutionResult result, CombatModifierContext context) { }

            public void AdvanceTime(float deltaTime) { }
            public ICombatModifier CaptureForAction() => new PercentModifier(Percent, _group);
        }

        private sealed class RecordingResolutionPort : ICombatResolutionPort
        {
            internal int Calls;
            internal DamageResult Result = DamageResult.Damaged;
            public CombatResolutionResult Resolve(CombatOutcomeDraft outcome)
            {
                Calls++;
                var targets = new List<CombatTargetResolution>();
                foreach (var targetId in outcome.TargetInstanceIds)
                {
                    targets.Add(new CombatTargetResolution(targetId, outcome.GetFinalDamage(), Result,
                        outcome.GetFinalDamage(), isEliteOrBoss: true));
                }

                return new CombatResolutionResult(outcome, targets);
            }
        }

        private sealed class TimeConditionModifier : IDynamicCombatModifier
        {
            private sealed class State { internal int ResolvedCount; }
            private State _state = new State();
            internal float Percent;
            internal int ResolvedCount => _state.ResolvedCount;
            internal TimeConditionModifier(float percent) { Percent = percent; }
            public ICombatModifier CaptureForAction() => new TimeConditionModifier(Percent) { _state = _state };
            public void Modify(CombatOutcomeDraft outcome, CombatModifierContext context)
            { if (context.BattleTime >= 2f) outcome.AddPercent(CombatModifierGroup.TargetCondition, Percent); }
            public void OnResolved(CombatResolutionResult result, CombatModifierContext context) { _state.ResolvedCount++; }
            public void AdvanceTime(float deltaTime) { }
        }

        private sealed class HistoryObserver : ICombatModifier
        {
            private sealed class State { internal int DistinctHits; }
            private State _state = new State();
            internal int DistinctHits => _state.DistinctHits;
            public ICombatModifier CaptureForAction() => new HistoryObserver { _state = _state };
            public void Modify(CombatOutcomeDraft outcome, CombatModifierContext context) { }
            public void OnResolved(CombatResolutionResult result, CombatModifierContext context)
            { _state.DistinctHits = context.History.CountDistinctRobotHits(2001, context.BattleTime, 2f); }
            public void AdvanceTime(float deltaTime) { }
        }

        private sealed class EmptyTargetPort : ICombatResolutionPort, ICombatTargetQuery
        {
            internal Action OnQuery;
            internal int Calls;
            public IReadOnlyList<int> GetValidTargets(CombatActionSnapshot action)
            { OnQuery?.Invoke(); return Array.Empty<int>(); }
            public CombatResolutionResult Resolve(CombatOutcomeDraft outcome)
            { Calls++; return new CombatResolutionResult(outcome, Array.Empty<CombatTargetResolution>()); }
        }

        private class ProbeModifier : ICombatModifier
        {
            internal sealed class ProbeState
            {
                internal int ModifyCalls;
                internal int SeenHitCount = -1;
                internal float ResolvedPercent;
            }
            internal float Percent;
            internal ProbeState State = new ProbeState();
            internal Action<CombatModifierContext> OnModify;
            public virtual ICombatModifier CaptureForAction() =>
                new ProbeModifier { Percent = Percent, State = State, OnModify = OnModify };
            public void Modify(CombatOutcomeDraft outcome, CombatModifierContext context)
            { State.ModifyCalls++; State.SeenHitCount = outcome.HitCount; OnModify?.Invoke(context); }
            public void OnResolved(CombatResolutionResult result, CombatModifierContext context)
            { State.ResolvedPercent = Percent; }
            public void AdvanceTime(float deltaTime) { }
        }

        private sealed class DynamicProbeModifier : ProbeModifier, IDynamicCombatModifier
        {
            public override ICombatModifier CaptureForAction() =>
                new DynamicProbeModifier { Percent = Percent, State = State, OnModify = OnModify };
        }
    }
}

