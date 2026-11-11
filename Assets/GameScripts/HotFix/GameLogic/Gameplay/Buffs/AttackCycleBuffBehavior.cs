using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    /// <summary>每个被动实例独立累计攻击；弹丸周期机器人以成功发射为准，达标后排队释放子技能。</summary>
    public sealed class AttackCycleBuffBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        private readonly Queue<SkillTarget> _waitingTargets = new Queue<SkillTarget>();
        public int AttackCount { get; private set; }
        public bool Pending { get; private set; }

        public AttackCycleBuffBehavior(BattleRuntime battle)
        { _battle = battle ?? throw new ArgumentNullException(nameof(battle)); }

        public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context)
        {
            if (!context.Request.Owner.Equals(container.Owner) || !buff.Source.Equals(container.Owner) ||
                container.Owner.Kind != SkillOwnerKind.Entity) return;
            var model = buff.Model;
            if (context.Request.SkillId == model.TriggerSkillId)
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
                    if (!IsCurrent(container, buff) || !Pending ||
                        !_battle.TriggeredSkills.TryGet(container.Owner, buff.InstanceId, out var current) ||
                        !ReferenceEquals(current, pending)) return;
                    AttackCount -= pending.Cost; Pending = false;
                    _battle.TriggeredSkills.Cancel(container.Owner, buff.InstanceId);
                    // 等待表现资源期间成功发射的余量保留，并逐周期释放。
                    if (CountsLaunchedAttacks(container) && AttackCount >= pending.Cost)
                    {
                        Pending = true;
                        _battle.TriggeredSkills.Enqueue(container.Owner, buff.InstanceId, model.TriggerSkillId,
                            pending.Cost, _waitingTargets.Count > 0 ? _waitingTargets.Dequeue() : pending.Target);
                    }
                });
                return;
            }
            if (context.Request.SkillId != model.CountSkillId || Pending) return;
            if (CountsLaunchedAttacks(container)) return;
            var required = PositiveInteger(context.Parameters, "attackCycleCount");
            var next = Math.Min(required, AttackCount + 1);
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

        internal void OnProjectileLaunched(BuffContainer container, BuffObj buff, SkillCastSnapshot cast)
        {
            if (!IsCurrent(container, buff) || !buff.Source.Equals(container.Owner) ||
                !cast.Owner.Equals(container.Owner) || cast.SkillId != buff.Model.CountSkillId) return;
            var required = PositiveInteger(cast.Parameters, "attackCycleCount");
            AttackCount++;
            if (Pending)
            {
                // 每一轮记录自己的目标，不能让积压的多轮果汁沿用第一轮落点。
                if (AttackCount % required == 0) _waitingTargets.Enqueue(cast.Target);
                return;
            }
            if (AttackCount < required) return;
            Pending = true;
            _battle.TriggeredSkills.Enqueue(container.Owner, buff.InstanceId, buff.Model.TriggerSkillId, required, cast.Target);
        }

        private bool IsCurrent(BuffContainer container, BuffObj buff) => !_battle.IsDisposed &&
            _battle.TryResolveOwner(container.Owner, out _) && container.Items.Contains(buff);

        private bool CountsLaunchedAttacks(BuffContainer container) =>
            _battle.TryResolveOwner(container.Owner, out var owner) &&
            owner is RobotEntity robot && robot.UsesProjectileAttackCycle;

        private static int PositiveInteger(IReadOnlyDictionary<string, float> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || !EntityValidation.IsFiniteNonNegative(value) ||
                value < 1 || value >= int.MaxValue || value != Math.Truncate(value))
                throw new InvalidOperationException("攻击周期参数无效：" + key);
            return (int)value;
        }

        public override void OnRemoved(BuffContainer container, BuffObj buff) => Reset(container, buff);

        internal void Reset(BuffContainer container, BuffObj buff)
        {
            _battle.TriggeredSkills.Cancel(container.Owner, buff.InstanceId);
            _waitingTargets.Clear();
            Pending = false; AttackCount = 0;
        }
    }
}
