using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只保存对局内身份和数值，释放通道后再尝试，不从OnCast重入施法。</summary>
    public sealed class BuffTriggeredSkillQueue
    {
        public sealed class PendingCast
        {
            public SkillOwnerRef Owner { get; }
            public long BuffInstanceId { get; }
            public int SkillId { get; }
            public int Cost { get; }
            public Vector3 Position { get; }
            internal PendingCast(SkillOwnerRef owner, long buffId, int skillId, int cost, Vector3 position)
            { Owner = owner; BuffInstanceId = buffId; SkillId = skillId; Cost = cost; Position = position; }
        }
        private readonly BattleRuntime _battle;
        private readonly Dictionary<SkillOwnerRef, PendingCast> _pending = new Dictionary<SkillOwnerRef, PendingCast>();
        private bool _processing;
        public int Count => _pending.Count;
        internal BuffTriggeredSkillQueue(BattleRuntime battle) { _battle = battle; }
        public bool TryGet(SkillOwnerRef owner, out PendingCast cast) => _pending.TryGetValue(owner, out cast);
        internal void Enqueue(SkillOwnerRef owner, long buffId, int skillId, int cost, Vector3 position)
        { if (!_battle.IsDisposed && !_pending.ContainsKey(owner)) _pending.Add(owner, new PendingCast(owner, buffId, skillId, cost, position)); }
        internal void Cancel(SkillOwnerRef owner, long buffId)
        { if (_pending.TryGetValue(owner, out var pending) && pending.BuffInstanceId == buffId) _pending.Remove(owner); }
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
                    if (!_pending.TryGetValue(pending.Owner, out var current) || !ReferenceEquals(current, pending)) continue;
                    if (!_battle.TryResolveOwner(pending.Owner, out var owner) ||
                        !owner.Buffs.Items.Any(b => b.InstanceId == pending.BuffInstanceId))
                    { Cancel(pending.Owner, pending.BuffInstanceId); continue; }
                    if (!owner.CanCast || _battle.Timelines.HasChannel(pending.Owner)) continue;
                    _battle.Skills.TryCast(new SkillCastRequest(pending.Owner, pending.SkillId, SkillTriggerSource.Buff,
                        SkillTarget.AtPosition(pending.Owner.Side, pending.Position), pending.BuffInstanceId));
                }
            }
            finally { _processing = false; }
        }
    }
}
