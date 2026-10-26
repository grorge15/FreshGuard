using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>直接施加战场或实体Buff的时间轴节点，不需要投射物。</summary>
    public sealed class AddBuffTimelineAction : ITimelineAction
    {
        private readonly BattleRuntime _battle;
        public AddBuffTimelineAction(BattleRuntime battle) { _battle = battle; }
        public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute)
        {
            execute = null;
            if (!node.Parameters.TryGetValue("buffId", out var id) || id != (int)id ||
                !_battle.Catalog.TryGetBuff((int)id, out var model) ||
                !node.Parameters.TryGetValue("duration", out var duration) || duration <= 0f ||
                (cast.Target.Kind != SkillTargetKind.Battlefield && cast.Target.Kind != SkillTargetKind.Entity)) return false;
            var reference = cast.Target.Kind == SkillTargetKind.Battlefield ?
                SkillOwnerRef.Battlefield(cast.Owner.BattleId, cast.Target.Side) :
                SkillOwnerRef.Entity(cast.Owner.BattleId, cast.Target.Side, cast.Target.EntityId);
            // 提前解析行为保证未知行为不会在提交冷却后才失败。
            _battle.BuffBehaviors.Create(model.BehaviorKey);
            var parameters = new Dictionary<string, float>();
            foreach (var pair in cast.Parameters) parameters[pair.Key] = pair.Value;
            foreach (var pair in node.Parameters) if (pair.Key != "buffId" && pair.Key != "duration") parameters[pair.Key] = pair.Value;
            execute = () =>
            {
                if (reference.Side == cast.Owner.Side && _battle.TryResolveOwner(reference, out var target))
                    _battle.AddBuff(target, model, cast.Owner, duration, parameters);
            };
            return true;
        }
    }
}
