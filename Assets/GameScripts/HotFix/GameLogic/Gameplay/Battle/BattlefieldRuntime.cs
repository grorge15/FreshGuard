namespace GameLogic
{
    /// <summary>战场不是实体：没有伪造主角身份，也不承担网格占用。</summary>
    public sealed class BattlefieldRuntime : ISkillOwner
    {
        private readonly BattleRuntime _battle;
        public SkillOwnerRef OwnerRef { get; }
        public long BattleId => OwnerRef.BattleId;
        public BattleSide Side => OwnerRef.Side;
        public SkillContainer Skills { get; } = new SkillContainer();
        public BuffContainer Buffs { get; } = new BuffContainer();
        public bool CanCast => !_battle.IsDisposed;
        internal BattlefieldRuntime(BattleRuntime battle, BattleSide side)
        { _battle = battle; OwnerRef = SkillOwnerRef.Battlefield(battle.BattleId, side); }
        internal void Clear() { Skills.Clear(); Buffs.Dispose(); }
    }
}
