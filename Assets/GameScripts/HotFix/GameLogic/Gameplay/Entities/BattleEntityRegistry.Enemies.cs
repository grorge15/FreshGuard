using System;
using GameConfig.enemy;
using TEngine;

namespace GameLogic
{
    public sealed partial class BattleEntityRegistry
    {
        public int CreateEnemyFromConfig(Enemy config, BattleSide side, int cycle = 1)
        {
            ThrowIfDisposed();
            ValidateEnemyConfig(config);
            EntityValidation.RequireSide(side);
            if (cycle != 1 && cycle != 2) throw new ArgumentOutOfRangeException(nameof(cycle));
            var instanceId = ReserveInstanceId();
            EnemyEntity enemy;
            switch (config.Id)
            {
                case SkeletonEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<SkeletonEnemy>(); break;
                case EliteSkeletonEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<EliteSkeletonEnemy>(); break;
                case SkeletonBossEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<SkeletonBossEnemy>(); break;
                default: throw new ArgumentException("未注册对应敌人实体类型：" + config.Id, nameof(config));
            }
            try
            {
                enemy.InitializeFromConfig(instanceId, config, side, cycle);
                AddEntity(instanceId, enemy);
            }
            catch
            {
                MemoryPool.Release(enemy);
                throw;
            }
            return instanceId;
        }

        internal static void ValidateEnemyConfig(Enemy config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            EntityValidation.RequirePositive(config.Id, nameof(config.Id));
            EntityValidation.RequirePositive(config.HpC1, nameof(config.HpC1));
            EntityValidation.RequirePositive(config.HpC2, nameof(config.HpC2));
            EntityValidation.RequireFiniteNonNegative(config.EnemySpeed, nameof(config.EnemySpeed));
            if (config.EnemyType < 1 || config.EnemyType > 3) throw new ArgumentOutOfRangeException(nameof(config.EnemyType));
            if (config.EnemyKillSP < 0) throw new ArgumentOutOfRangeException(nameof(config.EnemyKillSP));
            if (config.LeakDamage < 0) throw new ArgumentOutOfRangeException(nameof(config.LeakDamage));
            RequireRatio(config.ControlResist, nameof(config.ControlResist));
            RequireRatio(config.KnockbackResist, nameof(config.KnockbackResist));
            if (string.IsNullOrWhiteSpace(config.EnemyPrefab)) throw new ArgumentException("敌人预制体资源名不能为空。", nameof(config));
            if (config.Id != SkeletonEnemy.CONFIG_ID && config.Id != EliteSkeletonEnemy.CONFIG_ID &&
                config.Id != SkeletonBossEnemy.CONFIG_ID)
                throw new ArgumentException("未注册对应敌人实体类型：" + config.Id, nameof(config));
        }

        private static void RequireRatio(float value, string name)
        {
            EntityValidation.RequireFiniteNonNegative(value, name);
            if (value > 1f) throw new ArgumentOutOfRangeException(name, "受控比例必须在 0~1 范围内。");
        }
    }
}
