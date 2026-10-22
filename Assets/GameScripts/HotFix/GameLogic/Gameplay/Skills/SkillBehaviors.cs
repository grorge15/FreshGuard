using System;
using UnityEngine;

namespace GameLogic
{
    internal static class SkillBehaviorUtility
    {
        internal static SkillCastSnapshot CreateDamageCast(SkillCastRequest request, SkillRuntimeContext context,
            SkillDefinition definition, long castId, int damage)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            var target = request.TargetInstanceId.HasValue
                ? new[] { request.TargetInstanceId.Value } : Array.Empty<int>();
            var action = new CombatActionSnapshot(request.BattleId, request.SourceInstanceId, request.SourceSide,
                request.SkillId, target, CombatOutcomeTag.SingleDamage, damage, request.Origin,
                request.AimPoint, castId, request.SourceKind);
            action = context.Battle.Combat.CaptureAttack(action);
            return new SkillCastSnapshot(castId, request, definition, target, definition.IntValues,
                definition.FloatValues, action);
        }
    }

    internal sealed class BasicProjectileSkill : ISkillBehavior
    {
        public SkillCastResult TryCast(SkillCastRequest request, SkillRuntimeContext context,
            SkillDefinition definition, long castId)
        {
            return new SkillCastResult(true, SkillBehaviorUtility.CreateDamageCast(request, context,
                definition, castId, request.BaseDamage));
        }

        public void OnHit(SkillCastSnapshot cast, SkillRuntimeContext context)
        {
            if (context.ResolutionPort == null) throw new InvalidOperationException("普通投射物缺少伤害结算端口。");
            context.Battle.Combat.Resolve(cast.DamageAction, context.ResolutionPort);
        }
    }

    internal sealed class DamageThenApplySlowSkill : ISkillBehavior
    {
        public SkillCastResult TryCast(SkillCastRequest request, SkillRuntimeContext context,
            SkillDefinition definition, long castId)
        {
            if (definition.IntValues.Count < 1 || definition.FloatValues.Count < 2)
                throw new ArgumentException("1505 必须配置一个直接伤害和两个减速参数。");
            return new SkillCastResult(true, SkillBehaviorUtility.CreateDamageCast(request, context,
                definition, castId, definition.IntValues[0]));
        }

        public void OnHit(SkillCastSnapshot cast, SkillRuntimeContext context)
        {
            if (context.ResolutionPort == null) throw new InvalidOperationException("1505 缺少伤害结算端口。");
            var result = context.Battle.Combat.Resolve(cast.DamageAction, context.ResolutionPort);
            if (context.StatusPort == null) return;
            foreach (var target in result.Targets)
            {
                if (!target.IsHit || target.IsKilled) continue;
                context.StatusPort.TryApplySlow(target.TargetInstanceId, cast.SourceInstanceId,
                    cast.FloatValues[0], cast.FloatValues[1]);
            }
        }
    }
}
