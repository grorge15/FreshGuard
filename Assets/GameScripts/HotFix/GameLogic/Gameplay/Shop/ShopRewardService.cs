using System;
using System.Globalization;

namespace GameLogic
{
    public enum ShopRewardKind { EnemyKill, GlassBreak }
    public enum ShopEnemyKind { Normal, Elite, Boss }
    public enum ShopGlassKind { Normal, Rainbow }
    public enum ShopRewardSource { NormalEnemy, EliteEnemy, Boss, NormalGlass, ColoredGlass }

    /// <summary>只接收上层已确认的奖励事实，不生产敌人、玻璃或AI行为。</summary>
    public sealed class ShopRewardService
    {
        private readonly BattleShopContext _context;
        internal ShopRewardService(BattleShopContext context) { _context = context; }

        public ShopOperationResult TryGrant(long battleId, BattleSide recipient, ShopRewardSource source, int sourceInstanceId)
        {
            if (_context.IsEnded) return ShopOperationResult.BattleEnded;
            if (sourceInstanceId <= 0) return ShopOperationResult.InvalidArgument;
            switch (source)
            {
                case ShopRewardSource.NormalEnemy:
                    return TryGrantEnemyKill(battleId, sourceInstanceId, recipient, ShopEnemyKind.Normal);
                case ShopRewardSource.EliteEnemy:
                    return TryGrantEnemyKill(battleId, sourceInstanceId, recipient, ShopEnemyKind.Elite);
                case ShopRewardSource.Boss:
                    return TryGrantEnemyKill(battleId, sourceInstanceId, recipient, ShopEnemyKind.Boss);
                case ShopRewardSource.NormalGlass:
                    return TryGrantGlassBreak(battleId, sourceInstanceId.ToString(CultureInfo.InvariantCulture), recipient, ShopGlassKind.Normal);
                case ShopRewardSource.ColoredGlass:
                    return TryGrantGlassBreak(battleId, sourceInstanceId.ToString(CultureInfo.InvariantCulture), recipient, ShopGlassKind.Rainbow);
                default:
                    return ShopOperationResult.InvalidArgument;
            }
        }

        public ShopOperationResult TryGrantEnemyKill(long battleId, int enemyInstanceId, BattleSide recipient, int coins)
        {
            return _context.TryGrantReward(battleId, ShopRewardKind.EnemyKill,
                enemyInstanceId.ToString(CultureInfo.InvariantCulture), recipient, coins);
        }

        public ShopOperationResult TryGrantGlassBreak(long battleId, string glassId, BattleSide recipient, int coins)
        {
            return _context.TryGrantReward(battleId, ShopRewardKind.GlassBreak, glassId, recipient, coins);
        }

        public ShopOperationResult TryGrantEnemyKill(long battleId, int enemyInstanceId, BattleSide recipient, ShopEnemyKind kind)
        {
            int coins;
            if (!_context.Rules.EnemyRewards.TryGetValue(kind, out coins)) return ShopOperationResult.InvalidArgument;
            return TryGrantEnemyKill(battleId, enemyInstanceId, recipient, coins);
        }

        public ShopOperationResult TryGrantGlassBreak(long battleId, string glassId, BattleSide recipient, ShopGlassKind kind)
        {
            int coins;
            if (!_context.Rules.GlassRewards.TryGetValue(kind, out coins)) return ShopOperationResult.InvalidArgument;
            return TryGrantGlassBreak(battleId, glassId, recipient, coins);
        }
    }
}
