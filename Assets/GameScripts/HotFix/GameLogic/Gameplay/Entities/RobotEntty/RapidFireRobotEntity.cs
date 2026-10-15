using GameConfig.robot;

namespace GameLogic
{
    /// <summary>连射机器人；技能行为后续实现。</summary>
    public sealed class RapidFireRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 504;

        internal RapidFireRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
