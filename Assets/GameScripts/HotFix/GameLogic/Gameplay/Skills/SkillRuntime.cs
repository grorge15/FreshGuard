using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>技能身份、参数快照和命中生命周期；不负责商店、资源或实体表现。</summary>
    public sealed class SkillRuntime : IDisposable
    {
        private readonly Dictionary<SkillBehaviorType, ISkillBehavior> _behaviors =
            new Dictionary<SkillBehaviorType, ISkillBehavior>();
        private readonly List<SkillCastSnapshot> _activeCasts = new List<SkillCastSnapshot>();
        private SkillDefinitionRegistry _definitions;
        private long _nextCastId;

        public BattleRuntime Battle { get; }
        public bool IsDisposed { get; private set; }

        public SkillRuntime(BattleRuntime battle, SkillDefinitionRegistry definitions)
        {
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _behaviors.Add(SkillBehaviorType.BasicProjectile, new BasicProjectileSkill());
            _behaviors.Add(SkillBehaviorType.DamageThenApplySlow, new DamageThenApplySlowSkill());
        }

        public void RegisterDefinition(SkillDefinition definition)
        {
            ThrowIfDisposed();
            _definitions.Register(definition);
        }

        public SkillCastResult TryCast(SkillCastRequest request, SkillRuntimeContext context)
        {
            ThrowIfDisposed();
            if (request == null || context == null || context.Battle != Battle ||
                request.BattleId != Battle.BattleId || Battle.IsDisposed) return SkillCastResult.Rejected();

            SkillDefinition definition;
            SkillLevelDefinition level;
            if (request.SkillId == 0)
            {
                definition = new SkillDefinition(1, SkillType.Active, SkillBehaviorType.BasicProjectile,
                    true, request.SkillLevel);
                level = new SkillLevelDefinition(1, request.SkillLevel, Array.Empty<int>(), Array.Empty<float>());
            }
            else if (!_definitions.TryGet(request.SkillId, out definition))
            {
                // 旧机器人表中的 1500~1504 仍是单体投射物；未登记的旧技能先保留这条兼容行为。
                definition = new SkillDefinition(request.SkillId, SkillType.Active,
                    SkillBehaviorType.BasicProjectile, true, request.SkillLevel);
                level = new SkillLevelDefinition(request.SkillId, request.SkillLevel,
                    Array.Empty<int>(), Array.Empty<float>());
            }
            else if (request.SkillLevel > definition.MaxLevel ||
                !_definitions.TryGetLevel(request.SkillId, request.SkillLevel, out level))
                return SkillCastResult.Rejected();
            if (definition.RequiresTarget &&
                (!request.TargetInstanceId.HasValue || request.TargetInstanceId.Value <= 0 ||
                 context.IsTargetValid == null || !context.IsTargetValid(request, request.TargetInstanceId.Value)))
                return SkillCastResult.Rejected();

            ISkillBehavior behavior;
            if (!_behaviors.TryGetValue(definition.BehaviorType, out behavior)) return SkillCastResult.Rejected();
            var result = behavior.TryCast(request, context, definition, level, checked(++_nextCastId));
            if (result.Succeeded && result.Cast != null) _activeCasts.Add(result.Cast);
            return result;
        }

        public void OnHit(SkillCastSnapshot cast, SkillRuntimeContext context)
        {
            ThrowIfDisposed();
            if (cast == null || context == null || context.Battle != Battle ||
                cast.BattleId != Battle.BattleId || cast.IsFinished) return;
            ISkillBehavior behavior;
            if (!_behaviors.TryGetValue(cast.BehaviorType, out behavior)) return;
            try { behavior.OnHit(cast, context); }
            finally
            {
                cast.Finish();
                _activeCasts.Remove(cast);
            }
        }

        /// <summary>投射物被回收、目标失效或对局结束前未命中时，使施放安全结束。</summary>
        public void Cancel(SkillCastSnapshot cast)
        {
            if (cast == null || cast.IsFinished || cast.BattleId != Battle.BattleId) return;
            cast.Finish();
            _activeCasts.Remove(cast);
        }

        public void AdvanceTime(float deltaTime)
        {
            ThrowIfDisposed();
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            for (var i = _activeCasts.Count - 1; i >= 0; i--)
                if (_activeCasts[i] == null || _activeCasts[i].IsFinished) _activeCasts.RemoveAt(i);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var cast in _activeCasts) cast.Finish();
            _activeCasts.Clear();
            _definitions = null;
        }

        private void ThrowIfDisposed()
        { if (IsDisposed) throw new ObjectDisposedException(nameof(SkillRuntime)); }
    }
}
