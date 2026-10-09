using System;
using System.Collections.Generic;
using GameConfig.glass;
using TEngine;

namespace GameLogic
{
    public sealed partial class BattleEntityRegistry
    {
        private readonly Dictionary<(BattleSide, BoardCoordinate), int> _glassByCell =
            new Dictionary<(BattleSide, BoardCoordinate), int>();

        public GlassEntity CreateGlass(int configId, BattleSide side, BoardCoordinate coordinate,
            GlassKind kind, int maxDurability, float hitInterval)
        {
            return CreateGlassCore(configId, side, coordinate, kind, maxDurability, 1, hitInterval);
        }

        public GlassEntity CreateGlassFromConfig(Glass config, BattleSide side,
            BoardCoordinate coordinate, float hitInterval)
        {
            ThrowIfDisposed();
            if (config == null) throw new ArgumentNullException(nameof(config));
            return CreateGlassCore(config.Id, side, coordinate, (GlassKind)config.Kind,
                config.MaxDurability, config.HitDurabilityLoss, hitInterval);
        }

        /// <summary>仅返回仍存活的玻璃；查询引用不得保留到异步回调中。</summary>
        public bool TryGetGlass(BattleSide side, BoardCoordinate coordinate, out GlassEntity glass)
        {
            glass = null;
            int instanceId;
            Entity entity;
            if (_disposed || !_glassByCell.TryGetValue((side, coordinate), out instanceId) ||
                !TryGet(instanceId, out entity)) return false;
            var candidate = entity as GlassEntity;
            if (candidate == null || !candidate.IsAlive) return false;
            glass = candidate;
            return true;
        }

        public GlassCollisionResult ApplyGlassCollision(int id, bool isBerserk, out GlassBreakSnapshot snapshot)
        {
            snapshot = default(GlassBreakSnapshot);
            Entity entity;
            if (!TryGet(id, out entity)) return GlassCollisionResult.Ignored;
            var glass = entity as GlassEntity;
            if (glass == null) return GlassCollisionResult.Ignored;
            var result = glass.ApplyCollision(isBerserk);
            if (result != GlassCollisionResult.Broken) return result;

            // Remove 会立即清空并归还池对象，碎裂身份必须先复制。
            var broken = new GlassBreakSnapshot(glass);
            EntityRemovalSnapshot removal;
            if (!Remove(id, EntityRemovalReason.Broken, out removal)) return GlassCollisionResult.Ignored;
            snapshot = broken;
            return GlassCollisionResult.Broken;
        }

        private GlassEntity CreateGlassCore(int configId, BattleSide side, BoardCoordinate coordinate,
            GlassKind kind, int maxDurability, int hitDurabilityLoss, float hitInterval)
        {
            ThrowIfDisposed();
            EntityValidation.RequirePositive(configId, nameof(configId));
            EntityValidation.RequireSide(side);
            if (coordinate.Column < 0 || coordinate.Row < 0)
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            if (kind != GlassKind.Normal && kind != GlassKind.Colored)
                throw new ArgumentOutOfRangeException(nameof(kind));
            EntityValidation.RequirePositive(maxDurability, nameof(maxDurability));
            EntityValidation.RequirePositive(hitDurabilityLoss, nameof(hitDurabilityLoss));
            EntityValidation.RequireFiniteNonNegative(hitInterval, nameof(hitInterval));
            var cell = (side, coordinate);
            if (_glassByCell.ContainsKey(cell))
                throw new InvalidOperationException("该阵营格子已有存活玻璃。");

            var nextId = checked(_nextInstanceId + 1);
            var instanceId = _nextInstanceId;
            var glass = MemoryPool.Acquire<GlassEntity>();
            try
            {
                glass.Initialize(instanceId, configId, side, coordinate, kind,
                    maxDurability, hitDurabilityLoss, hitInterval);
                _entities.Add(instanceId, glass);
                _glassByCell.Add(cell, instanceId);
            }
            catch
            {
                _entities.Remove(instanceId);
                MemoryPool.Release(glass);
                throw;
            }
            _nextInstanceId = nextId;
            return glass;
        }
    }
}
