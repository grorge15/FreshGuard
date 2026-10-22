using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class SkillDrivenArchitectureTests
    {
        [Test]
        public void SkillRuntimeLocks1505ParametersAtCastTime()
        {
            var definitions = new SkillDefinitionRegistry();
            definitions.Register(new SkillDefinition(1505, SkillType.Active,
                SkillBehaviorType.DamageThenApplySlow, new[] { 37 }, new[] { 0.4f, 3f }, true));
            using (var battle = new BattleRuntime(901, definitions))
            {
                var context = new SkillRuntimeContext(battle, null, null, (request, id) => true);
                var request = new SkillCastRequest(901, 101, BattleSide.Player, CombatSourceKind.Robot,
                    1505, SkillTriggerSource.RobotContact, 202, new Vector3(1f, 2f, 0f),
                    new Vector3(3f, 4f, 0f), new Vector3(5f, 6f, 0f), 20, 0.6f, 1f);

                var result = battle.Skills.TryCast(request, context);

                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(202, result.Cast.TargetInstanceIds[0]);
                Assert.AreEqual(37, result.Cast.IntValues[0]);
                Assert.AreEqual(0.4f, result.Cast.FloatValues[0]);
                Assert.AreEqual(3f, result.Cast.FloatValues[1]);
                Assert.AreEqual(0.6f, result.Cast.ProjectileFlightTime);
                Assert.IsFalse(result.Cast.IsFinished);
            }
        }

        [Test]
        public void SlowEntriesRefreshPerSourceAndUseTheStrongestActiveEntry()
        {
            var statuses = new EnemyCombatStatusState();

            Assert.IsTrue(statuses.TryApplySlow(101, 0.4f, 3f, 0.5f));
            Assert.IsTrue(statuses.TryApplySlow(102, 0.7f, 1f, 0.5f));
            Assert.That(statuses.GetEffectiveSlowRatio(), Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(statuses.GetEffectiveSpeed(10f), Is.EqualTo(6.5f).Within(0.0001f));

            statuses.AdvanceTime(1.1f);
            Assert.That(statuses.GetEffectiveSlowRatio(), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.IsTrue(statuses.TryApplySlow(101, 0.8f, 2f, 0.5f));
            Assert.That(statuses.GetEffectiveSlowRatio(), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(statuses.GetEffectiveSpeed(10f), Is.EqualTo(6f).Within(0.0001f));

            statuses.AdvanceTime(2f);
            Assert.AreEqual(0f, statuses.GetEffectiveSlowRatio());
            Assert.AreEqual(10f, statuses.GetEffectiveSpeed(10f));
        }

        [Test]
        public void SlowSpeedNeverFallsBelowHalfOfBaseSpeed()
        {
            var statuses = new EnemyCombatStatusState();
            statuses.TryApplySlow(101, 1f, 5f, 1f);

            Assert.AreEqual(5f, statuses.GetEffectiveSpeed(10f));
        }

        [Test]
        public void BattleRuntimeOwnsAndDisposesSkillRuntimeTogetherWithCombat()
        {
            using (var battle = new BattleRuntime(902))
            {
                Assert.AreSame(battle, battle.Skills.Battle);
                Assert.IsFalse(battle.IsDisposed);
                battle.AdvanceTime(0.25f);
                Assert.That(battle.BattleTime, Is.EqualTo(0.25f).Within(0.0001f));
            }
        }

        [Test]
        public void DamageThenApplySlowResolvesDamageBeforeApplyingSlowToSurvivor()
        {
            var definitions = new SkillDefinitionRegistry();
            definitions.Register(new SkillDefinition(1505, SkillType.Active,
                SkillBehaviorType.DamageThenApplySlow, new[] { 37 }, new[] { 0.4f, 3f }, true));
            using (var battle = new BattleRuntime(903, definitions))
            {
                var resolution = new RecordingResolutionPort(DamageResult.Damaged);
                var status = new RecordingStatusPort();
                var context = new SkillRuntimeContext(battle, resolution, status, (request, id) => true);
                var result = battle.Skills.TryCast(Create1505Request(903), context);

                battle.Skills.OnHit(result.Cast, context);

                Assert.AreEqual(37, resolution.Damage);
                Assert.AreEqual(1, status.Calls.Count);
                Assert.AreEqual(101, status.Calls[0].SourceInstanceId);
                Assert.That(status.Calls[0].Ratio, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(status.Calls[0].Duration, Is.EqualTo(3f).Within(0.0001f));
                Assert.IsTrue(result.Cast.IsFinished);
            }
        }

        [Test]
        public void DamageThenApplySlowDoesNotApplySlowAfterKillingTarget()
        {
            var definitions = new SkillDefinitionRegistry();
            definitions.Register(new SkillDefinition(1505, SkillType.Active,
                SkillBehaviorType.DamageThenApplySlow, new[] { 37 }, new[] { 0.4f, 3f }, true));
            using (var battle = new BattleRuntime(904, definitions))
            {
                var resolution = new RecordingResolutionPort(DamageResult.Killed);
                var status = new RecordingStatusPort();
                var context = new SkillRuntimeContext(battle, resolution, status, (request, id) => true);
                var result = battle.Skills.TryCast(Create1505Request(904), context);

                battle.Skills.OnHit(result.Cast, context);

                Assert.AreEqual(37, resolution.Damage);
                Assert.AreEqual(0, status.Calls.Count);
            }
        }

        [Test]
        public void EnemyStatusPortUsesEntityStateAndPoolReuseStartsClean()
        {
            using (var battle = new BattleRuntime(905))
            {
                var firstId = battle.Registry.CreateEnemy(2001, BattleSide.Opponent, 100, 10f);
                var statusPort = new EnemyCombatStatusPort(battle);

                Assert.IsTrue(statusPort.TryApplySlow(firstId, 101, 0.8f, 2f));
                Assert.IsTrue(battle.Registry.TryGet(firstId, out var firstEntity));
                Assert.That(((EnemyEntity)firstEntity).EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));

                Assert.IsTrue(battle.Registry.Remove(firstId, EntityRemovalReason.Manual, out _));
                var secondId = battle.Registry.CreateEnemy(2001, BattleSide.Opponent, 100, 10f);
                Assert.IsTrue(battle.Registry.TryGet(secondId, out var secondEntity));
                Assert.AreEqual(0, ((EnemyEntity)secondEntity).CombatStatuses.SlowEntryCount);
                Assert.AreEqual(10f, ((EnemyEntity)secondEntity).EffectiveSpeed);
            }
        }

        private static SkillCastRequest Create1505Request(long battleId)
        {
            return new SkillCastRequest(battleId, 101, BattleSide.Player, CombatSourceKind.Robot,
                1505, SkillTriggerSource.RobotContact, 202, new Vector3(1f, 2f, 0f),
                new Vector3(3f, 4f, 0f), new Vector3(5f, 6f, 0f), 20, 0.6f, 1f);
        }

        private sealed class RecordingResolutionPort : ICombatResolutionPort
        {
            private readonly DamageResult _result;
            public int Damage { get; private set; }

            public RecordingResolutionPort(DamageResult result) { _result = result; }

            public CombatResolutionResult Resolve(CombatOutcomeDraft outcome)
            {
                Damage = outcome.GetFinalDamage();
                var target = new CombatTargetResolution(outcome.TargetInstanceIds[0], Damage, _result,
                    _result == DamageResult.Ignored ? 0 : Damage);
                return new CombatResolutionResult(outcome, new[] { target });
            }
        }

        private sealed class RecordingStatusPort : ICombatStatusPort
        {
            public readonly List<SlowCall> Calls = new List<SlowCall>();

            public bool TryApplySlow(int targetInstanceId, int sourceInstanceId, float slowRatio, float duration)
            {
                Calls.Add(new SlowCall(targetInstanceId, sourceInstanceId, slowRatio, duration));
                return true;
            }
        }

        private readonly struct SlowCall
        {
            public readonly int TargetInstanceId;
            public readonly int SourceInstanceId;
            public readonly float Ratio;
            public readonly float Duration;

            public SlowCall(int targetInstanceId, int sourceInstanceId, float ratio, float duration)
            {
                TargetInstanceId = targetInstanceId;
                SourceInstanceId = sourceInstanceId;
                Ratio = ratio;
                Duration = duration;
            }
        }
    }
}
