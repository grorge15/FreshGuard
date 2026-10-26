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
            RobotEntity.ValidateAttackConfig(config, maxLevel);
            if (level <= 0 || level > maxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            // 0 是尚未配置技能；现有显式传参接口继续要求有效技能 ID。
            if (config.SkillId < 0) throw new ArgumentOutOfRangeException(nameof(config.SkillId));
            if (config.ShapeId_Ref == null || config.ShapeId_Ref.Id != config.ShapeId)
                throw new ArgumentException("机器人形状引用未解析或与形状 ID 不一致。", nameof(config));
            if (string.IsNullOrWhiteSpace(config.RobotPrefab) || string.IsNullOrWhiteSpace(config.RobotIcon))
                throw new ArgumentException("机器人预制体与图标资源名不能为空。", nameof(config));

            if (config.Id != CannonRobotEntity.CONFIG_ID && config.Id != ScatterRobotEntity.CONFIG_ID &&
                config.Id != ImpactRobotEntity.CONFIG_ID && config.Id != PiercingRobotEntity.CONFIG_ID &&
                config.Id != RapidFireRobotEntity.CONFIG_ID && config.Id != TractorRobotEntity.CONFIG_ID &&
                config.Id != FrostRobotEntity.CONFIG_ID && config.Id != PoisonRobotEntity.CONFIG_ID)
                throw new ArgumentException("未注册对应机器人实体类型：" + config.Id, nameof(config));
            var instanceId = ReserveInstanceId();
            RobotEntity robot;
            switch (config.Id)
            {
                case CannonRobotEntity.CONFIG_ID:
                    robot = new CannonRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case ScatterRobotEntity.CONFIG_ID:
                    robot = new ScatterRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case ImpactRobotEntity.CONFIG_ID:
                    robot = new ImpactRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case PiercingRobotEntity.CONFIG_ID:
                    robot = new PiercingRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case RapidFireRobotEntity.CONFIG_ID:
                    robot = new RapidFireRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case TractorRobotEntity.CONFIG_ID:
                    robot = new TractorRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case FrostRobotEntity.CONFIG_ID:
                    robot = new FrostRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                case PoisonRobotEntity.CONFIG_ID:
                    robot = new PoisonRobotEntity(instanceId, config, side, level, maxLevel);
                    break;
                default:
                    throw new ArgumentException("未注册对应机器人实体类型：" + config.Id, nameof(config));
            }
            AddEntity(robot.InstanceId, robot);
            return robot;
        }
    }
}
