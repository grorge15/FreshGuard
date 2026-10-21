using GameConfig.robot;

namespace GameLogic
{
    /// <summary>冰霜炮；技能行为后续实现。</summary>
    public sealed class FrostRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 601;

        internal FrostRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
