using GameConfig.robot;

namespace GameLogic
{
    /// <summary>单格机器人。</summary>
    public sealed class SingleCellRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 1004;

        internal SingleCellRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
