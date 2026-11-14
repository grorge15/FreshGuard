using System;
using Cysharp.Threading.Tasks;
using GameConfig;

namespace GameLogic
{
    /// <summary>窗口只操作已结算的账目；金币保存失败重试不重复加钱，也不再次改杯数。</summary>
    public sealed class BattleSettlementReward
    {
        private readonly BattleSettlementRecord _settlement;
        private readonly Func<int, UniTask> _claim;
        private bool _saving;

        public int BaseAmount => _settlement.BaseCoins;
        public string ItemName => _settlement.ItemName;
        public int DisplayAmount => checked(BaseAmount * Math.Max(1, _settlement.ClaimedMultiplier));
        public bool IsDoubled => _settlement.ClaimedMultiplier == 2;
        public bool IsSaved { get; private set; }
        public int TrophiesBefore => _settlement.TrophiesBefore;
        public int TrophiesAfter => _settlement.TrophiesAfter;
        public int TrophyDelta => _settlement.TrophyDelta;
        public int OpponentTrophiesBefore => _settlement.OpponentTrophiesBefore;
        public int OpponentTrophiesAfter => _settlement.OpponentTrophiesAfter;
        public int OpponentTrophyDelta => _settlement.OpponentTrophyDelta;

        public BattleSettlementReward(BattleSettlementRecord settlement, Func<int, UniTask> claim)
        {
            _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
            _claim = claim ?? throw new ArgumentNullException(nameof(claim));
        }

        /// <summary>保留独立奖励预览的入口；正式战斗使用持久化账目构造。</summary>
        public BattleSettlementReward(Tables tables, MainMenuPlayerState player,
            BattleSettlementResult result, Func<UniTask> save)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (save == null) throw new ArgumentNullException(nameof(save));
            var rules = BattleRewardRules.FromTables(tables, player.Trophies);
            _settlement = new BattleSettlementRecord
            {
                BaseCoins = result == BattleSettlementResult.Victory ? rules.WinCoins : rules.LoseCoins,
                ItemName = rules.ItemName, Result = result,
                TrophiesBefore = player.Trophies, TrophiesAfter = player.Trophies
            };
            _settlement.EnsureOpponentTrophies(rules);
            _claim = async multiplier =>
            {
                player.AddCoins(checked(BaseAmount * (multiplier - _settlement.ClaimedMultiplier)));
                _settlement.ClaimedMultiplier = multiplier;
                await save();
            };
        }

        public async UniTask ClaimAsync(bool doubleReward)
        {
            if (_saving) return;
            var multiplier = Math.Max(_settlement.ClaimedMultiplier, doubleReward ? 2 : 1);
            if (IsSaved && multiplier == _settlement.ClaimedMultiplier) return;
            _saving = true;
            IsSaved = false;
            try
            {
                await _claim(multiplier);
                IsSaved = true;
            }
            finally { _saving = false; }
        }
    }
}
