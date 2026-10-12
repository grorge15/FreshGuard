using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>每侧共享一个锁定目标；只同步读取存活实体和路线进度。</summary>
    public sealed class SharedEnemyTargetSelector
    {
        private readonly BattleShopContext _context;
        private readonly EnemyBattleController _enemies;
        private readonly int?[] _targets = new int?[2];
        private readonly List<int> _candidates = new List<int>();

        public SharedEnemyTargetSelector(BattleShopContext context, EnemyBattleController enemies)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _enemies = enemies != null ? enemies : throw new ArgumentNullException(nameof(enemies));
        }

        public int? GetTarget(BattleSide side)
        {
            EntityValidation.RequireSide(side);
            var index = (int)side;
            if (!_enemies.OwnsContext(_context)) { Clear(); return null; }
            var locked = _targets[index];
            if (locked.HasValue && _enemies.TryGetTargetPosition(side, locked.Value, out _)) return locked;
            _targets[index] = null;
            _enemies.CopyTargetCandidates(side, _candidates);
            var best = -1f;
            foreach (var id in _candidates)
            {
                if (!_context.Registry.TryGet(id, out var entity) || !(entity is EnemyEntity enemy) || !enemy.IsAlive) continue;
                if (enemy.RouteProgress > best || enemy.RouteProgress == best &&
                    (!_targets[index].HasValue || id < _targets[index].Value))
                {
                    best = enemy.RouteProgress;
                    _targets[index] = id;
                }
            }
            return _targets[index];
        }

        public void Clear() { _targets[0] = _targets[1] = null; _candidates.Clear(); }
    }
}
