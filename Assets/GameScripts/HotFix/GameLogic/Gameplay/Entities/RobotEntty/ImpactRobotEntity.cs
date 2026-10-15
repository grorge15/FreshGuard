using GameConfig.robot;

namespace GameLogic
{
    /// <summary>冲击机器人；技能行为后续实现。</summary>
    public sealed class ImpactRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 505;

        internal ImpactRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
