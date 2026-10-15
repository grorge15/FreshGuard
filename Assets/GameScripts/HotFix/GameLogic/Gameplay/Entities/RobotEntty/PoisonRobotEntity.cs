using GameConfig.robot;

namespace GameLogic
{
    /// <summary>毒素炮；技能行为后续实现。</summary>
    public sealed class PoisonRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 603;

        internal PoisonRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
