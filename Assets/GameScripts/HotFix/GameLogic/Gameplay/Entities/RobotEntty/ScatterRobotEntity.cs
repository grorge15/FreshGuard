using GameConfig.robot;

namespace GameLogic
{
    /// <summary>散射机器人；技能行为后续实现。</summary>
    public sealed class ScatterRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 502;

        internal ScatterRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
