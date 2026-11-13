using System;

namespace GameLogic
{
    internal abstract class BattlefieldAssistBehavior : BuffBehavior
    {
        private readonly BattleRuntime _battle;
        protected BattlefieldAssistBehavior(BattleRuntime battle) { _battle = battle; }
        protected BattleAssistState State(BuffContainer container)
        {
            if (container.Owner.Kind != SkillOwnerKind.Battlefield || container.Owner.BattleId != _battle.BattleId)
                throw new InvalidOperationException("助力只能挂载到本局战场。");
            return (container.Owner.Side == BattleSide.Player ? _battle.Player : _battle.Opponent).Assists;
        }
    }

    internal sealed class OpeningBallSpeedBonusBehavior : BattlefieldAssistBehavior
    {
        public OpeningBallSpeedBonusBehavior(BattleRuntime battle) : base(battle) { }
        public override void OnOccur(BuffContainer container, BuffObj buff) => State(container).OpeningBallSpeedRate = buff.Get("speedRate");
        public override void OnRemoved(BuffContainer container, BuffObj buff) => State(container).OpeningBallSpeedRate = 0f;
    }

    internal sealed class ComboBallSpeedBonusBehavior : BattlefieldAssistBehavior
    {
        public ComboBallSpeedBonusBehavior(BattleRuntime battle) : base(battle) { }
        public override void OnOccur(BuffContainer container, BuffObj buff) =>
            State(container).ConfigureComboSpeed((int)buff.Get("comboStep"), buff.Get("speedRate"), buff.Get("comboCap"));
        public override void OnRemoved(BuffContainer container, BuffObj buff) => State(container).ConfigureComboSpeed(0, 0f, 0f);
    }

    internal sealed class ComboRobotDamageBonusBehavior : BattlefieldAssistBehavior
    {
        public ComboRobotDamageBonusBehavior(BattleRuntime battle) : base(battle) { }
        public override void OnOccur(BuffContainer container, BuffObj buff) =>
            State(container).ConfigureComboDamage((int)buff.Get("comboStep"), buff.Get("damageRate"), buff.Get("comboCap"));
        public override void OnRemoved(BuffContainer container, BuffObj buff) => State(container).ConfigureComboDamage(0, 0f, 0f);
        public override void OnAttack(BuffContainer container, BuffObj buff, CombatOutcomeDraft attack)
        {
            if (attack.Action.SourceKind == CombatSourceKind.Robot)
                attack.AddPercent(CombatModifierGroup.GlobalDamage, State(container).RobotDamageRate);
        }
    }

    internal sealed class GlassDurabilityOverrideBehavior : BattlefieldAssistBehavior
    {
        public GlassDurabilityOverrideBehavior(BattleRuntime battle) : base(battle) { }
        public override void OnOccur(BuffContainer container, BuffObj buff) => State(container).NormalBallGlassLossOverride = (int)buff.Get("glassLoss");
        public override void OnRemoved(BuffContainer container, BuffObj buff) => State(container).NormalBallGlassLossOverride = null;
    }

    internal sealed class ExtraBerserkBallBehavior : BattlefieldAssistBehavior
    {
        public ExtraBerserkBallBehavior(BattleRuntime battle) : base(battle) { }
        public override void OnOccur(BuffContainer container, BuffObj buff) => State(container).ExtraBerserkBallCount = (int)buff.Get("extraBalls");
        public override void OnRemoved(BuffContainer container, BuffObj buff) => State(container).ExtraBerserkBallCount = 0;
    }
}
