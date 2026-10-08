using System;
using GameConfig.robot;

namespace GameLogic
{
    public sealed partial class BattleEntityRegistry
    {
        public RobotEntity CreateRobotFromConfig(Robot config, BattleSide side, int level = 1, int maxLevel = 5)
        {
            ThrowIfDisposed();
            if (config == null) throw new ArgumentNullException(nameof(config));
            EntityValidation.RequirePositive(config.Id, nameof(config.Id));
            EntityValidation.RequireSide(side);
            EntityValidation.RequirePositive(config.QualityId, nameof(config.QualityId));
            EntityValidation.RequirePositive(config.ShapeId, nameof(config.ShapeId));
            EntityValidation.RequirePositive(maxLevel, nameof(maxLevel));
            if (level <= 0 || level > maxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            // 0 是尚未配置技能；现有显式传参接口继续要求有效技能 ID。
            if (config.SkillId < 0) throw new ArgumentOutOfRangeException(nameof(config.SkillId));
            if (config.ShapeId_Ref == null || config.ShapeId_Ref.Id != config.ShapeId)
                throw new ArgumentException("机器人形状引用未解析或与形状 ID 不一致。", nameof(config));
            if (string.IsNullOrWhiteSpace(config.RobotPrefab) || string.IsNullOrWhiteSpace(config.RobotIcon))
                throw new ArgumentException("机器人预制体与图标资源名不能为空。", nameof(config));

            var nextId = checked(_nextInstanceId + 1);
            RobotEntity robot;
            switch (config.Id)
            {
                case StraightRobotEntity.CONFIG_ID:
                    robot = new StraightRobotEntity(_nextInstanceId, config, side, level, maxLevel);
                    break;
                case CornerRobotEntity.CONFIG_ID:
                    robot = new CornerRobotEntity(_nextInstanceId, config, side, level, maxLevel);
                    break;
                case PulseRobotEntity.CONFIG_ID:
                    robot = new PulseRobotEntity(_nextInstanceId, config, side, level, maxLevel);
                    break;
                default:
                    throw new ArgumentException("未注册对应机器人实体类型：" + config.Id, nameof(config));
            }
            _entities.Add(robot.InstanceId, robot);
            _nextInstanceId = nextId;
            return robot;
        }
    }
}
