using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public enum SkillCastFailure { None, BattleEnded, InvalidOwner, NotAllowed, MissingSkill, Cooldown, Busy,
        InvalidTarget, MissingLevel, Cancelled, InvalidTimeline, NotPrepared }
    public readonly struct SkillCastResult
    {
        public SkillCastFailure Failure { get; }
        public bool Accepted => Failure == SkillCastFailure.None;
        public SkillCastResult(SkillCastFailure failure) { Failure = failure; }
    }
    public sealed class SkillCastContext
    {
        private readonly SkillLevelModel _level;
        public StaffConfig Staff => _level.Staff;
        private readonly List<Action> _acceptanceCommits = new List<Action>();
        public SkillCastRequest Request { get; }
        public int Level { get; }
        public string TimelineKey { get; set; }
        public int BulletId { get; set; }
        public Dictionary<string, float> Parameters { get; } = new Dictionary<string, float>();
        public IReadOnlyList<CombatBuffApplication> HitBuffs => _level.CaptureHitBuffs(Parameters);
        public bool IsCancelled { get; private set; }
        internal SkillCastContext(SkillCastRequest request, SkillObj skill, SkillLevelModel level)
        {
            Request = request; Level = skill.Level; TimelineKey = skill.Model.TimelineKey; BulletId = skill.Model.BulletId;
            _level = level;
            foreach (var pair in level.Parameters) Parameters.Add(pair.Key, pair.Value);
        }
        public void Cancel() => IsCancelled = true;
        // 仅暂存本次同步施法的状态变化；不是Buff生命周期回调。
        public void DeferUntilAccepted(Action commit) => _acceptanceCommits.Add(commit ?? throw new ArgumentNullException(nameof(commit)));
        internal void CommitAcceptance()
        {
            var commits = _acceptanceCommits.ToArray();
            _acceptanceCommits.Clear();
            foreach (var commit in commits) commit();
        }
    }
    public sealed class SkillCastSnapshot
    {
        public StaffConfig Staff { get; }
        public SkillOwnerRef Owner { get; }
        public int SkillId { get; }
        public int Level { get; }
        public SkillTriggerSource Trigger { get; }
        public SkillTarget Target { get; }
        public string TimelineKey { get; }
        public int BulletId { get; }
        public IReadOnlyDictionary<string, float> Parameters { get; }
        public IReadOnlyList<CombatBuffApplication> HitBuffs { get; }
        internal SkillCastSnapshot(SkillCastContext context)
        {
            Owner = context.Request.Owner; SkillId = context.Request.SkillId; Target = context.Request.Target;
            Level = context.Level; Trigger = context.Request.Trigger; TimelineKey = context.TimelineKey;
            Parameters = BuffObj.Copy(context.Parameters);
            BulletId = context.BulletId;
            HitBuffs = context.HitBuffs;
            Staff = context.Staff;
        }
        public float Get(string key, float fallback = 0f) => Parameters.TryGetValue(key, out var value) ? value : fallback;
    }
    public sealed class SkillManager
    {
        private readonly BattleRuntime _battle;
        private readonly HashSet<SkillOwnerRef> _preparing = new HashSet<SkillOwnerRef>();
        public SkillManager(BattleRuntime battle) { _battle = battle; }
        public SkillCastResult TryCast(SkillCastRequest request) => TryCast(request, null);
        internal SkillCastResult TryCast(SkillCastRequest request, Func<bool> commitContact)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var failure = Validate(request, out var owner, out var skill);
            if (failure != SkillCastFailure.None) return new SkillCastResult(failure);
            if (!_battle.Catalog.TryGetLevel(skill.Model.Id, skill.Level, out var level))
                return new SkillCastResult(SkillCastFailure.MissingLevel);
            _preparing.Add(request.Owner);
            try
            {
                var robot = owner as RobotEntity;
                var actionVersion = robot?.ActionVersion;
                var context = new SkillCastContext(request, skill, level);
                owner.Buffs.OnCast(context);
                if (context.IsCancelled || (robot != null && robot.ActionVersion != actionVersion))
                    return new SkillCastResult(SkillCastFailure.Cancelled);
                failure = Validate(request, out var finalOwner, out var finalSkill, true);
                if (failure != SkillCastFailure.None || !ReferenceEquals(skill, finalSkill) || !ReferenceEquals(owner, finalOwner))
                    return new SkillCastResult(failure == SkillCastFailure.None ? SkillCastFailure.InvalidOwner : failure);
                var snapshot = new SkillCastSnapshot(context);
                if (!_battle.Timelines.TryPrepare(snapshot, out var timeline, out failure)) return new SkillCastResult(failure);
                var started = false;
                try
                {
                    failure = Validate(request, out _, out _, true);
                    if (failure != SkillCastFailure.None) return new SkillCastResult(failure);
                    if (robot != null && robot.ActionVersion != actionVersion)
                        return new SkillCastResult(SkillCastFailure.Cancelled);
                    if (commitContact != null && !commitContact()) return new SkillCastResult(SkillCastFailure.NotAllowed);
                    skill.CooldownRemaining = skill.Model.Cooldown;
                    context.CommitAcceptance();
                    // 接受回调也可能升级或移除机器人，旧动作不得再启动。
                    if (_battle.IsDisposed || !_battle.TryResolveOwner(request.Owner, out var committedOwner) ||
                        !ReferenceEquals(owner, committedOwner) ||
                        (robot != null && robot.ActionVersion != actionVersion))
                        return new SkillCastResult(SkillCastFailure.Cancelled);
                    started = true;
                    _battle.Timelines.Start(timeline);
                    return new SkillCastResult(SkillCastFailure.None);
                }
                finally { if (!started) timeline.ReleasePreparation(); }
            }
            finally { _preparing.Remove(request.Owner); }
        }
        private SkillCastFailure Validate(SkillCastRequest request, out ISkillOwner owner, out SkillObj skill, bool ownPreparation = false)
        {
            owner = null; skill = null;
            if (_battle.IsDisposed) return SkillCastFailure.BattleEnded;
            if (!_battle.TryResolveOwner(request.Owner, out owner)) return SkillCastFailure.InvalidOwner;
            if (request.SourceBuffInstanceId.HasValue)
            {
                var exists = false;
                foreach (var buff in owner.Buffs.Items) if (buff.InstanceId == request.SourceBuffInstanceId.Value) { exists = true; break; }
                if (!exists || request.Trigger != SkillTriggerSource.Buff) return SkillCastFailure.Cancelled;
            }
            if (!owner.CanCast || !_battle.CanCastNow() || !_battle.Combat.AllowsGameplayTriggers) return SkillCastFailure.NotAllowed;
            if (!owner.Skills.TryGet(request.SkillId, out skill)) return SkillCastFailure.MissingSkill;
            if (skill.CooldownRemaining > 0f) return SkillCastFailure.Cooldown;
            if ((!ownPreparation && _preparing.Contains(request.Owner)) || _battle.Timelines.HasChannel(request.Owner)) return SkillCastFailure.Busy;
            return IsTargetValid(request.Owner, skill.Model.TargetRule, request.Target) ? SkillCastFailure.None : SkillCastFailure.InvalidTarget;
        }
        private bool IsTargetValid(SkillOwnerRef owner, SkillTargetRule rule, SkillTarget target)
        {
            if (rule == SkillTargetRule.None) return target.Kind == SkillTargetKind.None;
            if (target.Side != owner.Side) return false;
            switch (rule)
            {
                case SkillTargetRule.SameSideEnemy:
                    return target.Kind == SkillTargetKind.Entity && _battle.Registry.TryGet(target.EntityId, out var entity) &&
                        entity.Side == owner.Side && entity is EnemyEntity enemy && enemy.IsAlive;
                case SkillTargetRule.SameSideBattlefield: return target.Kind == SkillTargetKind.Battlefield;
                case SkillTargetRule.SameSideRobot:
                case SkillTargetRule.SameSideEntity:
                    return target.Kind == SkillTargetKind.Entity && _battle.Registry.TryGet(target.EntityId, out var ownEntity) &&
                        !ownEntity.IsRemoved && ownEntity.Side == owner.Side &&
                        (rule == SkillTargetRule.SameSideEntity || ownEntity is RobotEntity);
                case SkillTargetRule.SameSidePosition:
                    var p = target.Position;
                    return target.Kind == SkillTargetKind.Position && !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
                        !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
                default: return false;
            }
        }
    }
}
