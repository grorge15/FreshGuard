using System;
using GameConfig.enemy;
using TEngine;

namespace GameLogic
{
    public sealed partial class BattleEntityRegistry
    {
        public int CreateEnemyFromConfig(Enemy config, BattleSide side, int cycle = 1,
            int wave = 1, float hpGrowthPerWave = 0f)
        {
            ThrowIfDisposed();
            ValidateEnemyConfig(config);
            EntityValidation.RequireSide(side);
            if (cycle < 1) throw new ArgumentOutOfRangeException(nameof(cycle));
            var maxHp = CalculateEnemyMaxHp(config.Hp, wave, hpGrowthPerWave);
            var instanceId = ReserveInstanceId();
            EnemyEntity enemy;
            switch (config.Id)
            {
                case SkeletonEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<SkeletonEnemy>(); break;
                case EliteSkeletonEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<EliteSkeletonEnemy>(); break;
                case SkeletonBossEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<SkeletonBossEnemy>(); break;
                case AppleEnemy.CONFIG_ID: enemy = MemoryPool.Acquire<AppleEnemy>(); break;
                default: throw new ArgumentException("未注册对应敌人实体类型：" + config.Id, nameof(config));
            }
            try
            {
                enemy.InitializeFromConfig(instanceId, config, side, cycle, wave, maxHp);
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
            EntityValidation.RequirePositive(config.Hp, nameof(config.Hp));
            EntityValidation.RequireFiniteNonNegative(config.EnemySpeed, nameof(config.EnemySpeed));
            EntityValidation.RequireFiniteNonNegative(config.HitRadius, nameof(config.HitRadius));
            if (config.EnemyType < 1 || config.EnemyType > 3) throw new ArgumentOutOfRangeException(nameof(config.EnemyType));
            if (config.EnemyKillSP < 0) throw new ArgumentOutOfRangeException(nameof(config.EnemyKillSP));
            if (config.LeakDamage < 0) throw new ArgumentOutOfRangeException(nameof(config.LeakDamage));
            RequireRatio(config.ControlResist, nameof(config.ControlResist));
            RequireRatio(config.KnockbackResist, nameof(config.KnockbackResist));
            if (string.IsNullOrWhiteSpace(config.EnemyPrefab)) throw new ArgumentException("敌人预制体资源名不能为空。", nameof(config));
            if (config.Id != SkeletonEnemy.CONFIG_ID && config.Id != EliteSkeletonEnemy.CONFIG_ID &&
                config.Id != SkeletonBossEnemy.CONFIG_ID && config.Id != AppleEnemy.CONFIG_ID)
                throw new ArgumentException("未注册对应敌人实体类型：" + config.Id, nameof(config));
        }

        internal static int CalculateEnemyMaxHp(int baseHp, int wave, float hpGrowthPerWave)
        {
            EntityValidation.RequirePositive(baseHp, nameof(baseHp));
            EntityValidation.RequirePositive(wave, nameof(wave));
            EntityValidation.RequireFiniteNonNegative(hpGrowthPerWave, nameof(hpGrowthPerWave));
            if (wave == 1 || hpGrowthPerWave == 0f) return baseHp;
            if (baseHp * (1d + (wave - 1d) * hpGrowthPerWave) > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(hpGrowthPerWave), "敌人生命值超出整数范围。");
            // 使用十进制计算，避免浮点误差改变 .5 的四舍五入结果。
            var hp = baseHp * (1m + (wave - 1m) * (decimal)hpGrowthPerWave);
            return checked((int)Math.Round(hp, 0, MidpointRounding.AwayFromZero));
        }

        private static void RequireRatio(float value, string name)
        {
            EntityValidation.RequireFiniteNonNegative(value, name);
            if (value > 1f) throw new ArgumentOutOfRangeException(name, "受控比例必须在 0~1 范围内。");
        }
    }
}
