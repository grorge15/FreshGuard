using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只保存值和 ID；静态修改在发射时锁定，不保存池化实体引用。</summary>
    public sealed class CombatActionSnapshot
    {
        public long BattleId { get; }
        public int SourceInstanceId { get; }
        public BattleSide SourceSide { get; }
        public CombatSourceKind SourceKind { get; }
        public int SkillId { get; }
        public IReadOnlyList<int> TargetInstanceIds { get; }
        public CombatOutcomeTag OutcomeTags { get; }
        public int BaseDamage { get; }
        public Vector3 Origin { get; }
        public Vector3 AimPoint { get; }
        public long Sequence { get; }
        public int LockedDamage => new CombatOutcomeDraft(this).GetFinalDamage();

        internal object Owner { get; private set; }
        internal bool Consumed { get; set; }
        internal IReadOnlyDictionary<CombatModifierGroup, double> Percents { get; private set; }
        public IReadOnlyList<CombatBuffApplication> HitBuffs { get; }

        public CombatActionSnapshot(long battleId, int sourceInstanceId, BattleSide sourceSide, int skillId,
            IReadOnlyList<int> targetInstanceIds, CombatOutcomeTag outcomeTags, int baseDamage,
            Vector3 origin, Vector3 aimPoint, long sequence, CombatSourceKind sourceKind = CombatSourceKind.Robot,
            IReadOnlyList<CombatBuffApplication> hitBuffs = null)
        {
            if (battleId <= 0 || sequence <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            EntityValidation.RequirePositive(sourceInstanceId, nameof(sourceInstanceId));
            EntityValidation.RequireSide(sourceSide);
            if (skillId < 0 || baseDamage < 0) throw new ArgumentOutOfRangeException(nameof(baseDamage));
            if (outcomeTags == CombatOutcomeTag.None) throw new ArgumentOutOfRangeException(nameof(outcomeTags));
            if (!Finite(origin) || !Finite(aimPoint)) throw new ArgumentOutOfRangeException(nameof(origin));
            if (targetInstanceIds == null) throw new ArgumentNullException(nameof(targetInstanceIds));
            var targets = new List<int>();
            var seen = new HashSet<int>();
            foreach (var id in targetInstanceIds)
            {
                EntityValidation.RequirePositive(id, nameof(targetInstanceIds));
                if (seen.Add(id)) targets.Add(id);
            }
            BattleId = battleId; SourceInstanceId = sourceInstanceId; SourceSide = sourceSide;
            SourceKind = sourceKind; SkillId = skillId; TargetInstanceIds = targets.AsReadOnly();
            OutcomeTags = outcomeTags; BaseDamage = baseDamage; Origin = origin; AimPoint = aimPoint; Sequence = sequence;
            HitBuffs = new List<CombatBuffApplication>(hitBuffs ?? Array.Empty<CombatBuffApplication>()).AsReadOnly();
        }

        internal CombatActionSnapshot Lock(object owner, long sequence, CombatOutcomeDraft draft)
        {
            return new CombatActionSnapshot(BattleId, SourceInstanceId, SourceSide, SkillId, TargetInstanceIds,
                OutcomeTags, BaseDamage, Origin, AimPoint, sequence, SourceKind, HitBuffs)
            {
                Owner = owner, Percents = draft.CopyPercents()
            };
        }

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
