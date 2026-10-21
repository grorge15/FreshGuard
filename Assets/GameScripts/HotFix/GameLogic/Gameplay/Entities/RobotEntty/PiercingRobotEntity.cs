using GameConfig.robot;

namespace GameLogic
{
    /// <summary>穿透机器人；技能行为后续实现。</summary>
    public sealed class PiercingRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 503;

        internal PiercingRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
