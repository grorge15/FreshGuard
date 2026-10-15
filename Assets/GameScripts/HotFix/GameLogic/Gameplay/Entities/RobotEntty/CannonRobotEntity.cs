using GameConfig.robot;

namespace GameLogic
{
    /// <summary>重炮机器人；技能行为后续实现。</summary>
    public sealed class CannonRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 501;

        internal CannonRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
