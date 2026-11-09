using GameConfig.robot;

namespace GameLogic
{
    /// <summary>榨汁机机器人，技能由配置和技能系统驱动。</summary>
    public sealed class JuicerRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 704;

        internal JuicerRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
