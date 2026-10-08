using GameConfig.robot;

namespace GameLogic
{
    /// <summary>L 形折角机器人；技能行为后续实现。</summary>
    public sealed class CornerRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 1002;

        internal CornerRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
