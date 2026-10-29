using UnityEngine;

namespace GameLogic
{
    internal sealed class EnemyBulletWorld : IBulletWorld
    {
        private readonly BattleShopSceneController _host;
        private readonly BattleShopContext _context;
        private readonly EnemyBattleController _enemies;
        public EnemyBulletWorld(BattleShopSceneController host, BattleShopContext context, EnemyBattleController enemies)
        { _host = host; _context = context; _enemies = enemies; }
        public bool IsValid(long battleId) => _host != null && _context != null && _host.Context == _context &&
            _context.BattleId == battleId && !_context.IsEnded && !_context.IsEndRequested &&
            _enemies != null && _enemies.OwnsContext(_context);
        public bool IsRunning => IsValid(_context.BattleId) && _host.IsCombatRunning;
        public bool TryGetPosition(BattleSide side, int targetId, out Vector3 position) =>
            _enemies.TryGetTargetPosition(side, targetId, out position);
        public bool TryGetDeathPosition(BattleSide side, int targetId, out Vector3 position) =>
            _enemies.TryGetDeathPosition(side, targetId, out position);
    }
}
