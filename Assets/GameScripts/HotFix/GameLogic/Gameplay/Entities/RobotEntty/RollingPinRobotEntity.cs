using GameConfig.robot;

namespace GameLogic
{
    /// <summary>擀面杖机器人，技能由配置和技能系统驱动。</summary>
    public sealed class RollingPinRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 702;

        internal RollingPinRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
