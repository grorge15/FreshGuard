using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    /// <summary>只保存对局内身份和数值，释放通道后再尝试，不从OnCast重入施法。</summary>
    public sealed class RobotTriggeredSkillQueue
    {
        public sealed class PendingCast
        {
            public SkillOwnerRef Owner { get; }
            public long Version { get; }
            public int SkillId { get; }
            public int Cost { get; }
            public SkillTarget Target { get; }
            internal PendingCast(SkillOwnerRef owner, long version, int skillId, int cost, SkillTarget target)
            { Owner = owner; Version = version; SkillId = skillId; Cost = cost; Target = target; }
        }
        private readonly BattleRuntime _battle;
        private readonly Dictionary<SkillOwnerRef, PendingCast> _pending = new Dictionary<SkillOwnerRef, PendingCast>();
        private bool _processing;
        public int Count => _pending.Count;
        internal RobotTriggeredSkillQueue(BattleRuntime battle) { _battle = battle; }
        public bool TryGet(SkillOwnerRef owner, out PendingCast cast) => _pending.TryGetValue(owner, out cast);
        internal void Enqueue(SkillOwnerRef owner, long version, int skillId, int cost, SkillTarget target)
        { if (!_battle.IsDisposed && !_pending.ContainsKey(owner)) _pending.Add(owner, new PendingCast(owner, version, skillId, cost, target)); }
        internal void Cancel(SkillOwnerRef owner) => _pending.Remove(owner);
        internal void Clear() => _pending.Clear();
        public void Process()
        {
            if (_processing || _battle.IsDisposed || !_battle.CanCastNow() || !_battle.Combat.AllowsGameplayTriggers) return;
            _processing = true;
            try
            {
                foreach (var pending in _pending.Values.OrderBy(p => p.Owner.EntityId).ToArray())
                {
                    if (_battle.IsDisposed || !_battle.CanCastNow()) break;
                    if (!TryGet(pending.Owner, out var current) || !ReferenceEquals(current, pending)) continue;
                    if (!_battle.TryResolveOwner(pending.Owner, out var owner) ||
                        !(owner is RobotEntity robot) || !robot.AttackCycle.Matches(pending))
                    { Cancel(pending.Owner); continue; }
                    if (!owner.CanCast || _battle.Timelines.HasChannel(pending.Owner)) continue;
                    if (!_battle.Catalog.TryGet(pending.SkillId, out var skill)) continue;
                    if (!TrySelectTarget(pending, skill, out var target)) continue;
                    _battle.Skills.TryCast(new SkillCastRequest(pending.Owner, pending.SkillId, SkillTriggerSource.AttackCycle,
                        target, sourceAttackCycleVersion: pending.Version));
                }
            }
            finally { _processing = false; }
        }

        private bool TrySelectTarget(PendingCast pending, SkillModel skill, out SkillTarget target)
        {
            target = pending.Target;
            if (skill.TargetRule == SkillTargetRule.None) { target = SkillTarget.None; return true; }
            if (skill.TargetRule == SkillTargetRule.SameSidePosition)
            { target = SkillTarget.AtPosition(pending.Owner.Side, pending.Target.Position); return true; }
            if (skill.TargetRule != SkillTargetRule.SameSideEnemy) return true;
            if (target.Kind == SkillTargetKind.Entity && _battle.Registry.TryGet(target.EntityId, out var current) &&
                current.Side == pending.Owner.Side && current is EnemyEntity currentEnemy && currentEnemy.IsAlive) return true;
            // 等待期间目标失效，按普通攻击相同的路线优先级重选；无目标时保留请求。
            EnemyEntity best = null;
            foreach (var id in _battle.Registry.CopyIds())
                if (_battle.Registry.TryGet(id, out var entity) && entity.Side == pending.Owner.Side &&
                    entity is EnemyEntity enemy && enemy.IsAlive &&
                    (best == null || enemy.RouteProgress > best.RouteProgress ||
                        enemy.RouteProgress == best.RouteProgress && enemy.InstanceId < best.InstanceId)) best = enemy;
            if (best == null) return false;
            target = SkillTarget.Entity(pending.Owner.Side, best.InstanceId); return true;
        }
    }
}
