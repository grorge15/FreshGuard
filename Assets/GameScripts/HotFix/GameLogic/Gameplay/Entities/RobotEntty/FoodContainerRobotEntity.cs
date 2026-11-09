using GameConfig.robot;

namespace GameLogic
{
    /// <summary>保鲜盒机器人，技能由配置和技能系统驱动。</summary>
    public sealed class FoodContainerRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 703;

        internal FoodContainerRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
