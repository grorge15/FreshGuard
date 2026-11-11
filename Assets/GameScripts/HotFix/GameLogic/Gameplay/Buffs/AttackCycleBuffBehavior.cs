using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    /// <summary>每个被动实例独立累计接受的攻击；达标动作由配置决定。</summary>
    public sealed class AttackCycleBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        public int AttackCount { get; private set; }
        public bool Pending { get; private set; }

        public AttackCycleBuffBehavior(BattleRuntime battle)
        { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }

        public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context)
        {
            if (!context.Request.Owner.Equals(container.Owner) || !buff.Source.Equals(container.Owner) ||
                container.Owner.Kind != SkillOwnerKind.Entity) return;
            var model = buff.Model;
            if (model.CycleAction == AttackCycleAction.CastSkill && context.Request.SkillId == model.TriggerSkillId)
            {
                // 同一机器人可以持有多个周期被动，其他被动发起的请求不由本实例消费。
                if (!context.Request.SourceBuffInstanceId.HasValue)
                { context.Cancel(); return; }
                if (context.Request.SourceBuffInstanceId != buff.InstanceId) return;
                if (context.Request.Trigger != SkillTriggerSource.Buff || !Pending ||
                    !_battle.TriggeredSkills.TryGet(container.Owner, buff.InstanceId, out var pending) ||
                    AttackCount < pending.Cost)
                { context.Cancel(); return; }
                context.DeferUntilAccepted(() =>
                {
                    if (!IsCurrent(container, buff)) return;
                    AttackCount -= pending.Cost; Pending = false;
                    _battle.TriggeredSkills.Cancel(container.Owner, buff.InstanceId);
                });
                return;
            }
            if (context.Request.SkillId != model.CountSkillId || Pending) return;
            var required = PositiveInteger(context.Parameters, "attackCycleCount");
            var next = Math.Min(required, AttackCount + 1);
            if (model.CycleAction == AttackCycleAction.ReplaceBullet || model.CycleAction == AttackCycleAction.EnhanceAttack)
            {
                if (next >= required)
                {
                    if (model.CycleAction == AttackCycleAction.ReplaceBullet)
                        context.BulletId = PositiveInteger(context.Parameters, "cycleBulletId");
                    else
                    {
                        // 子技能提供弹道与命中效果，仍是本次普攻的一枚弹，不再排队额外施法。
                        if (!_battle.TryResolveOwner(container.Owner, out var owner) ||
                            !owner.Skills.TryGet(model.TriggerSkillId, out var child) ||
                            !_battle.Catalog.TryGetLevel(child.Model.Id, child.Level, out var level))
                        { context.Cancel(); return; }
                        context.BulletId = child.Model.BulletId;
                        context.TimelineKey = child.Model.TimelineKey;
                        foreach (var effect in level.CaptureHitBuffs(level.Parameters)) context.AddHitBuff(effect);
                    }
                }
                context.DeferUntilAccepted(() =>
                {
                    if (IsCurrent(container, buff)) AttackCount = next >= required ? 0 : next;
                });
            }
            else if (model.CycleAction == AttackCycleAction.CastSkill)
            {
                var target = context.Request.Target;
                context.DeferUntilAccepted(() =>
                {
                    if (Pending || !IsCurrent(container, buff)) return;
                    AttackCount = next;
                    if (next < required) return;
                    Pending = true;
                    _battle.TriggeredSkills.Enqueue(container.Owner, buff.InstanceId, model.TriggerSkillId, required, target);
                });
            }
            else throw new InvalidOperationException("攻击周期缺少达标动作：" + model.Id);
        }

        private bool IsCurrent(BuffContainer container, BuffObj buff) => !_battle.IsDisposed &&
            _battle.TryResolveOwner(container.Owner, out _) && container.Items.Contains(buff);

        private static int PositiveInteger(IReadOnlyDictionary<string, float> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || !EntityValidation.IsFiniteNonNegative(value) ||
                value < 1 || value >= int.MaxValue || value != Math.Truncate(value))
                throw new InvalidOperationException("攻击周期参数无效：" + key);
            return (int)value;
        }

        public override void OnRemoved(BuffContainer container, BuffObj buff)
        {
            _battle.TriggeredSkills.Cancel(container.Owner, buff.InstanceId);
            Pending = false; AttackCount = 0;
        }
    }
}
