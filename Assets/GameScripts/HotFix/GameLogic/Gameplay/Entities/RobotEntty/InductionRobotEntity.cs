using GameConfig.robot;

namespace GameLogic
{
    public sealed class InductionRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 701;
        internal override bool UsesProjectileAttackCycle => true;
        internal InductionRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
