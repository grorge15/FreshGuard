using System;

namespace GameLogic
{
    /// <summary>单局战斗容器。商店上下文只持有它，不参与技能执行。</summary>
    public sealed class BattleRuntime : IDisposable
    {
        public long BattleId { get; }
        public BattleEntityRegistry Registry { get; }
        public CombatRuntime Combat { get; }
        public SkillRuntime Skills { get; }
        public float BattleTime { get; private set; }
        public bool IsDisposed { get; private set; }

        public BattleRuntime(long battleId, SkillDefinitionRegistry definitions = null)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            BattleId = battleId;
            Registry = new BattleEntityRegistry();
            Combat = new CombatRuntime(battleId);
            Skills = new SkillRuntime(this, definitions ?? SkillDefinitionRegistry.CreateDefaults());
        }

        public void AdvanceTime(float deltaTime)
        {
            ThrowIfDisposed();
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            Registry.AdvanceTime(deltaTime);
            Combat.AdvanceTime(deltaTime);
            Skills.AdvanceTime(deltaTime);
            BattleTime += deltaTime;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Skills.Dispose();
            Combat.Dispose();
            Registry.Dispose();
            BattleTime = 0f;
        }

        private void ThrowIfDisposed()
        { if (IsDisposed) throw new ObjectDisposedException(nameof(BattleRuntime)); }
    }
}
