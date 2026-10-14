using GameConfig.robot;

namespace GameLogic
{
    /// <summary>竖向双格机器人。</summary>
    public sealed class VerticalRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 1005;

        internal VerticalRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
