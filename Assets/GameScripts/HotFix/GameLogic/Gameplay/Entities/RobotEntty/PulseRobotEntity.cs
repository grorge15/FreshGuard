using GameConfig.robot;

namespace GameLogic
{
    /// <summary>T 形脉冲机器人；技能行为后续实现。</summary>
    public sealed class PulseRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 1003;

        internal PulseRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
