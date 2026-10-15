using GameConfig.robot;

namespace GameLogic
{
    /// <summary>牵引炮；技能行为后续实现。</summary>
    public sealed class TractorRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 602;

        internal TractorRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
