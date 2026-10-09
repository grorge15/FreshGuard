using System;
using System.Collections.Generic;
using TEngine;

namespace GameLogic
{
    /// <summary>双方战场共用的单局实体集合。实例 ID 必须与原管理器一起使用。</summary>
    public sealed partial class BattleEntityRegistry : IDisposable
    {
        private readonly Dictionary<int, Entity> _entities = new Dictionary<int, Entity>();
        private int _nextInstanceId = 1;
        private bool _disposed;

        public RobotEntity CreateRobot(int configId, BattleSide side, int shapeId, int skillId,
            int level = 1, int maxLevel = 5)
        {
            ThrowIfDisposed();
            EntityValidation.RequirePositive(configId, nameof(configId));
            EntityValidation.RequireSide(side);
            EntityValidation.RequirePositive(shapeId, nameof(shapeId));
            EntityValidation.RequirePositive(skillId, nameof(skillId));
            EntityValidation.RequirePositive(maxLevel, nameof(maxLevel));
            if (level <= 0 || level > maxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            var nextId = checked(_nextInstanceId + 1);
            var robot = new RobotEntity(_nextInstanceId, configId, side, shapeId, skillId, level, maxLevel);
            _entities.Add(robot.InstanceId, robot);
            _nextInstanceId = nextId;
            return robot;
        }

        public int CreateEnemy(int configId, BattleSide side, int maxHp, float baseSpeed)
        {
            ThrowIfDisposed();
            EntityValidation.RequirePositive(configId, nameof(configId));
            EntityValidation.RequireSide(side);
            EntityValidation.RequirePositive(maxHp, nameof(maxHp));
            EntityValidation.RequireFiniteNonNegative(baseSpeed, nameof(baseSpeed));
            var nextId = checked(_nextInstanceId + 1);
            var instanceId = _nextInstanceId;
            var enemy = MemoryPool.Acquire<EnemyEntity>();
            try
            {
                enemy.Initialize(instanceId, configId, side, maxHp, baseSpeed);
                _entities.Add(instanceId, enemy);
            }
            catch
            {
                MemoryPool.Release(enemy);
                throw;
            }
            _nextInstanceId = nextId;
            return instanceId;
        }

        /// <summary>怪物引用仅供同步读取，禁止保存到计时器、异步回调或投射物中。</summary>
        public bool TryGet(int instanceId, out Entity entity)
        {
            entity = null;
            return !_disposed && _entities.TryGetValue(instanceId, out entity);
        }

        public DamageResult ApplyDamage(int enemyInstanceId, int damage, out EntityRemovalSnapshot removal)
        {
            removal = default(EntityRemovalSnapshot);
            Entity entity;
            if (damage <= 0 || !TryGet(enemyInstanceId, out entity)) return DamageResult.Ignored;
            var enemy = entity as EnemyEntity;
            if (enemy == null || !enemy.IsAlive) return DamageResult.Ignored;
            enemy.ApplyDamage(damage);
            if (enemy.CurrentHp > 0) return DamageResult.Damaged;
            Remove(enemyInstanceId, EntityRemovalReason.Killed, out removal);
            return DamageResult.Killed;
        }

        public bool Remove(int instanceId, EntityRemovalReason reason, out EntityRemovalSnapshot removal)
        {
            removal = default(EntityRemovalSnapshot);
            Entity entity;
            if (!TryGet(instanceId, out entity) || entity.IsRemoved || !CanRemove(entity, reason)) return false;
            removal = new EntityRemovalSnapshot(entity, reason);
            _entities.Remove(instanceId);
            var glass = entity as GlassEntity;
            if (glass != null) _glassByCell.Remove((glass.Side, glass.Coordinate));
            entity.MarkRemoved(reason);
            var enemy = entity as EnemyEntity;
            if (enemy != null) MemoryPool.Release(enemy);
            if (glass != null) MemoryPool.Release(glass);
            return true;
        }

        public void AdvanceTime(float deltaTime)
        {
            ThrowIfDisposed();
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            foreach (var entity in _entities.Values)
            {
                (entity as RobotEntity)?.AdvanceTime(deltaTime);
                (entity as GlassEntity)?.AdvanceTime(deltaTime);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            foreach (var id in new List<int>(_entities.Keys))
            {
                EntityRemovalSnapshot removal;
                Remove(id, EntityRemovalReason.BattleEnded, out removal);
            }
            _disposed = true;
        }

        private static bool CanRemove(Entity entity, EntityRemovalReason reason)
        {
            switch (reason)
            {
                case EntityRemovalReason.Consumed:
                    return entity is RobotEntity;
                case EntityRemovalReason.Killed:
                    return entity is EnemyEntity enemy && enemy.CurrentHp == 0;
                case EntityRemovalReason.Leaked:
                    return entity is EnemyEntity;
                case EntityRemovalReason.Broken:
                    return entity is GlassEntity glass && glass.CurrentDurability == 0;
                case EntityRemovalReason.Manual:
                case EntityRemovalReason.BattleEnded:
                    return true;
                default:
                    return false;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BattleEntityRegistry));
        }
    }
}
