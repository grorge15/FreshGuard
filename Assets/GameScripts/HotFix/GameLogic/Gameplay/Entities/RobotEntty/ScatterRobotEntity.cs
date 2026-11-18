using GameConfig.robot;

namespace GameLogic
{
    /// <summary>散射机器人；单局控制器按成功碰撞攻击循环发射1、2、3道穿透激光。</summary>
    public sealed class ScatterRobotEntity : RobotEntity
    {
        public const int CONFIG_ID = 502;

        internal ScatterRobotEntity(int instanceId, Robot config, BattleSide side, int level, int maxLevel)
            : base(instanceId, config, side, level, maxLevel) { }
    }
}
