using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameLogic
{
    /// <summary>主线程单局运行时。双方独立规则集合，攻击序号统一分配；终局使所有未结算快照失效。</summary>
    public sealed class CombatRuntime : IDisposable
    {
        private readonly List<ICombatModifier>[] _modifiers = { new List<ICombatModifier>(), new List<ICombatModifier>() };
        private readonly CombatModifierPipeline _pipeline = new CombatModifierPipeline();
        private readonly object _owner = new object();
        // 移除后仍保留本局归属，飞行中的冻结规则仍可能向本方状态回写事实。
        private readonly Dictionary<ICombatModifier, BattleSide> _modifierOwners =
            new Dictionary<ICombatModifier, BattleSide>(new ModifierReferenceComparer());
        private long _sequence;
        public long BattleId { get; }
        public float BattleTime { get; private set; }
        public CombatHistory History { get; } = new CombatHistory();
        public bool IsDisposed { get; private set; }

        public CombatRuntime(long battleId)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            BattleId = battleId;
        }

        public void AddModifier(BattleSide side, ICombatModifier modifier)
        {
            ThrowIfDisposed(); EntityValidation.RequireSide(side);
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (_modifierOwners.TryGetValue(modifier, out var ownerSide) && ownerSide != side)
                throw new InvalidOperationException("修改器实例在一局内不能改变阵营归属。");
            foreach (var list in _modifiers)
                foreach (var existing in list)
                    if (ReferenceEquals(existing, modifier)) throw new InvalidOperationException("修改器实例不能重复注册或跨阵营共享。");
            _modifiers[(int)side].Add(modifier);
            _modifierOwners[modifier] = side;
        }

        public bool RemoveModifier(BattleSide side, ICombatModifier modifier)
        {
            ThrowIfDisposed(); EntityValidation.RequireSide(side);
            return _modifiers[(int)side].Remove(modifier);
        }

        public int GetModifierCount(BattleSide side)
        { EntityValidation.RequireSide(side); return _modifiers[(int)side].Count; }

        public CombatActionSnapshot CaptureAttack(CombatActionSnapshot action)
        {
            ThrowIfDisposed();
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.BattleId != BattleId || action.Owner != null)
                throw new InvalidOperationException("攻击必须来自当前对局且尚未锁定。");
            return _pipeline.Capture(_owner, checked(++_sequence), action,
                _modifiers[(int)action.SourceSide].ToArray(), Context(CombatModifierPhase.Capture), ThrowIfDisposed);
        }

        public CombatResolutionResult Resolve(CombatActionSnapshot action, ICombatResolutionPort port)
        {
            ThrowIfDisposed();
            if (action == null || port == null) throw new ArgumentNullException(nameof(action));
            if (action.BattleId != BattleId || !ReferenceEquals(action.Owner, _owner) || action.Consumed)
                throw new InvalidOperationException("攻击快照不属于当前运行时或已结算。");
            // 先封住重入和重复扣血。结算失败不重试，场景调用方负责中止异常对局。
            action.Consumed = true;
            var draft = new CombatOutcomeDraft(action, (port as ICombatTargetQuery)?.GetValidTargets(action));
            ThrowIfDisposed();
            var context = Context(CombatModifierPhase.Resolution);
            _pipeline.ModifyAtResolution(draft, context, ThrowIfDisposed);
            var result = port.Resolve(draft);
            if (result == null || !ReferenceEquals(result.Action, action))
                throw new InvalidOperationException("结算端口必须返回本次攻击的结果。");
            if (IsDisposed) return result; // 结算端口可能同步触发终局，不能重新写回已清空的状态。
            if (action.SourceKind == CombatSourceKind.Robot)
                foreach (var target in result.Targets)
                    if (target.IsHit) History.RecordHit(target.TargetInstanceId, action.SourceInstanceId,
                        BattleTime, target.IsEliteOrBoss);
            foreach (var observer in action.Observers)
            {
                if (IsDisposed) break;
                observer.OnResolved(result, context);
            }
            return result;
        }

        public void AdvanceTime(float deltaTime)
        {
            ThrowIfDisposed(); EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            EntityValidation.RequireFiniteNonNegative(BattleTime + deltaTime, nameof(deltaTime));
            BattleTime += deltaTime; History.Prune(BattleTime);
            var modifiers = new List<ICombatModifier>();
            foreach (var list in _modifiers) modifiers.AddRange(list);
            foreach (var modifier in modifiers)
            {
                if (IsDisposed) break;
                modifier.AdvanceTime(deltaTime);
            }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            foreach (var list in _modifiers) list.Clear();
            _modifierOwners.Clear();
            History.Clear(); BattleTime = 0f; _sequence = 0;
        }

        private CombatModifierContext Context(CombatModifierPhase phase) =>
            new CombatModifierContext(BattleId, BattleTime, History, phase);
        private void ThrowIfDisposed() { if (IsDisposed) throw new ObjectDisposedException(nameof(CombatRuntime)); }

        private sealed class ModifierReferenceComparer : IEqualityComparer<ICombatModifier>
        {
            public bool Equals(ICombatModifier x, ICombatModifier y) => ReferenceEquals(x, y);
            public int GetHashCode(ICombatModifier modifier) => RuntimeHelpers.GetHashCode(modifier);
        }
    }
}
