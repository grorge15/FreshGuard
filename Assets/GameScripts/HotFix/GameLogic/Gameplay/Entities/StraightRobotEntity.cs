using GameConfig.robot;

namespace GameLogic
{
    /// <summary>I 形长条机器人；技能行为后续实现。</summary>
    public sealed class StraightRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 1001;

        internal StraightRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
