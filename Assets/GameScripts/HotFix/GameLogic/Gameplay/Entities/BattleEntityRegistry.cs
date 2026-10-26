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
        private bool _disposing;
        private BattleRuntime _battle;
        internal void BindBattle(BattleRuntime battle)
        {
            if (_battle != null && !ReferenceEquals(_battle, battle)) throw new InvalidOperationException("注册表已属于另一对局。");
            _battle = battle;
            foreach (var entity in new List<Entity>(_entities.Values)) _battle.Attach(entity);
        }
        internal int[] CopyIds() => new List<int>(_entities.Keys).ToArray();
        private int ReserveInstanceId()
        {
            var id = _nextInstanceId;
            _nextInstanceId = checked(id + 1); // 任何被动回调之前预留，重入创建不能复用身份。
            return id;
        }
        private void AddEntity(int id, Entity entity)
        {
            _entities.Add(id, entity);
            try { _battle?.Attach(entity); }
            catch (Exception registrationFailure)
            {
                // 回调可能已创建其他实体或移除此实体，仅回滚本次仍登记的实例。
                if (_entities.TryGetValue(id, out var registered) && ReferenceEquals(registered, entity))
                {
                    _entities.Remove(id);
                    _battle?.Timelines.Cancel(entity.OwnerRef);
                    try { entity.MarkRemoved(EntityRemovalReason.Manual); }
                    catch (Exception cleanupFailure)
                    { throw new AggregateException("实体注册失败，回滚回调也失败。", registrationFailure, cleanupFailure); }
                }
                throw;
            }
        }

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
            var instanceId = ReserveInstanceId();
            var robot = new RobotEntity(instanceId, configId, side, shapeId, skillId, level, maxLevel);
            AddEntity(robot.InstanceId, robot);
            return robot;
        }

        public int CreateEnemy(int configId, BattleSide side, int maxHp, float baseSpeed)
        {
            ThrowIfDisposed();
            EntityValidation.RequirePositive(configId, nameof(configId));
            EntityValidation.RequireSide(side);
            EntityValidation.RequirePositive(maxHp, nameof(maxHp));
            EntityValidation.RequireFiniteNonNegative(baseSpeed, nameof(baseSpeed));
            var instanceId = ReserveInstanceId();
            var enemy = MemoryPool.Acquire<EnemyEntity>();
            try
            {
                enemy.Initialize(instanceId, configId, side, maxHp, baseSpeed);
                AddEntity(instanceId, enemy);
            }
            catch
            {
                MemoryPool.Release(enemy);
                throw;
            }
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
            _battle?.Timelines.Cancel(entity.OwnerRef);
            var glass = entity as GlassEntity;
            if (glass != null) _glassByCell.Remove((glass.Side, glass.Coordinate));
            var tower = entity as TowerEntity;
            if (tower != null) _towerBySide.Remove(tower.Side);
            var enemy = entity as EnemyEntity;
            try { entity.MarkRemoved(reason); }
            finally
            {
                if (enemy != null) MemoryPool.Release(enemy);
                if (glass != null) MemoryPool.Release(glass);
            }
            return true;
        }

        public void AdvanceTime(float deltaTime)
            => AdvanceTime(deltaTime, true);

        internal void AdvanceTime(float deltaTime, bool advanceEnemyBuffs)
        {
            ThrowIfDisposed();
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            foreach (var id in CopyIds())
            {
                if (!TryGet(id, out var entity)) continue;
                (entity as RobotEntity)?.AdvanceTime(deltaTime);
                (entity as GlassEntity)?.AdvanceTime(deltaTime);
                entity.Skills.Advance(deltaTime);
                if (advanceEnemyBuffs || !(entity is EnemyEntity)) entity.Buffs.Advance(deltaTime);
            }
        }

        public void Dispose()
        {
            if (_disposed || _disposing) return;
            _disposing = true;
            Exception failure = null;
            try
            {
                foreach (var id in CopyIds())
                    try { Remove(id, EntityRemovalReason.BattleEnded, out _); }
                    catch (Exception exception) { if (failure == null) failure = exception; }
            }
            finally
            {
                _entities.Clear(); _glassByCell.Clear(); _towerBySide.Clear();
                _disposed = true; _disposing = false;
            }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static bool CanRemove(Entity entity, EntityRemovalReason reason)
        {
            switch (reason)
            {
                case EntityRemovalReason.Consumed:
                    return entity is RobotEntity;
                case EntityRemovalReason.Killed:
                    return (entity is EnemyEntity enemy && enemy.CurrentHp == 0) ||
                        (entity is TowerEntity tower && tower.CurrentHp == 0);
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
            if (_disposed || _disposing) throw new ObjectDisposedException(nameof(BattleEntityRegistry));
        }
    }
}
