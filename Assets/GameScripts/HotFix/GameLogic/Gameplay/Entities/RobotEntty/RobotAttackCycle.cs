using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>机器人固有的攻击周期；不占Buff容器，子技能仍走正常施法与资源准备。</summary>
    public sealed class RobotAttackCycle
    {
        private readonly RobotEntity _robot;
        private readonly Queue<SkillTarget> _waitingTargets = new Queue<SkillTarget>();
        private BattleRuntime _battle;
        public int TriggerSkillId { get; private set; }
        public int AttackCount { get; private set; }
        public bool Pending { get; private set; }
        internal long Version { get; private set; } = 1;

        internal RobotAttackCycle(RobotEntity robot) { _robot = robot; }

        internal void Configure(BattleRuntime battle, int triggerSkillId)
        {
            if (battle == null) throw new ArgumentNullException(nameof(battle));
            if (triggerSkillId < 0 || (triggerSkillId != 0 && triggerSkillId == _robot.SkillId))
                throw new ArgumentOutOfRangeException(nameof(triggerSkillId));
            if (ReferenceEquals(_battle, battle) && TriggerSkillId == triggerSkillId) return;
            if (triggerSkillId > 0)
            {
                if (!battle.Catalog.TryGet(triggerSkillId, out var skill))
                    throw new InvalidOperationException("缺少机器人周期技能：" + triggerSkillId);
                RequiredCount(battle, _robot.SkillId, _robot.Level);
                battle.Grant(_robot, skill);
            }
            Reset();
            _battle = battle;
            TriggerSkillId = triggerSkillId;
        }

        internal void PrepareCast(SkillCastContext context)
        {
            if (TriggerSkillId == 0 || !IsCurrent() || !context.Request.Owner.Equals(_robot.OwnerRef)) return;
            if (context.Request.SkillId == TriggerSkillId)
            {
                if (context.Request.Trigger != SkillTriggerSource.AttackCycle ||
                    context.Request.SourceAttackCycleVersion != Version ||
                    !_battle.TriggeredSkills.TryGet(_robot.OwnerRef, out var pending) || !Matches(pending))
                { context.Cancel(); return; }
                context.DeferUntilAccepted(() => Complete(pending));
                return;
            }
            if (context.Request.SkillId != _robot.SkillId) return;
            context.AttackCycleVersion = Version;
            if (_robot.UsesProjectileAttackCycle || Pending) return;
            var required = PositiveInteger(context.Parameters, "attackCycleCount");
            var version = Version;
            var target = context.Request.Target;
            context.DeferUntilAccepted(() =>
            {
                if (version != Version || Pending || !IsCurrent()) return;
                AttackCount = Math.Min(required, AttackCount + 1);
                if (AttackCount >= required) Enqueue(required, target);
            });
        }

        internal void RecordProjectile(SkillCastSnapshot cast)
        {
            if (TriggerSkillId == 0 || !_robot.UsesProjectileAttackCycle || !IsCurrent() ||
                !cast.Owner.Equals(_robot.OwnerRef) || cast.SkillId != _robot.SkillId ||
                cast.Level != _robot.Level || cast.AttackCycleVersion != Version || !cast.TryCountAttackCycle()) return;
            var required = PositiveInteger(cast.Parameters, "attackCycleCount");
            AttackCount = checked(AttackCount + 1);
            if (Pending)
            {
                if (AttackCount % required == 0) _waitingTargets.Enqueue(cast.Target);
                return;
            }
            if (AttackCount >= required) Enqueue(required, cast.Target);
        }

        internal bool Matches(RobotTriggeredSkillQueue.PendingCast pending) => IsCurrent() && Pending &&
            pending.Owner.Equals(_robot.OwnerRef) && pending.Version == Version &&
            pending.SkillId == TriggerSkillId && AttackCount >= pending.Cost;

        private void Complete(RobotTriggeredSkillQueue.PendingCast pending)
        {
            if (!Matches(pending) || !_battle.TriggeredSkills.TryGet(_robot.OwnerRef, out var current) ||
                !ReferenceEquals(current, pending)) return;
            AttackCount -= pending.Cost;
            Pending = false;
            _battle.TriggeredSkills.Cancel(_robot.OwnerRef);
            if (_robot.UsesProjectileAttackCycle && AttackCount >= pending.Cost)
                Enqueue(pending.Cost, _waitingTargets.Count > 0 ? _waitingTargets.Dequeue() : pending.Target);
        }

        private void Enqueue(int cost, SkillTarget target)
        {
            Pending = true;
            _battle.TriggeredSkills.Enqueue(_robot.OwnerRef, Version, TriggerSkillId, cost, target);
        }

        internal void Reset()
        {
            _battle?.TriggeredSkills.Cancel(_robot.OwnerRef);
            _waitingTargets.Clear();
            AttackCount = 0;
            Pending = false;
            Version++;
        }

        private bool IsCurrent() => _battle != null && !_battle.IsDisposed &&
            _battle.TryResolveOwner(_robot.OwnerRef, out var owner) && ReferenceEquals(owner, _robot);

        internal static int RequiredCount(BattleRuntime battle, int skillId, int level) =>
            RequiredCountForConfig(battle.Catalog, skillId, level);

        internal static int RequiredCountForConfig(SkillCatalog catalog, int skillId, int level)
        {
            if (!catalog.TryGetLevel(skillId, level, out var values))
                throw new InvalidOperationException("缺少攻击周期等级配置：" + skillId + "/" + level);
            return PositiveInteger(values.Parameters, "attackCycleCount");
        }

        private static int PositiveInteger(IReadOnlyDictionary<string, float> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || !EntityValidation.IsFiniteNonNegative(value) ||
                value < 1 || value >= int.MaxValue || value != Math.Truncate(value))
                throw new InvalidOperationException("攻击周期参数无效：" + key);
            return (int)value;
        }
    }
}
