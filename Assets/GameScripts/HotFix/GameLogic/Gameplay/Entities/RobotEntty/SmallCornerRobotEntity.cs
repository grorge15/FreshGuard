using GameConfig.robot;

namespace GameLogic
{
    /// <summary>上侧折角的三格机器人。</summary>
    public sealed class SmallCornerRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 1006;

        internal SmallCornerRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
