using System;
using Cysharp.Threading.Tasks;
using GameConfig;

namespace GameLogic
{
    /// <summary>本局助力币领取状态；保存失败重试时只补存档，不重复增加余额。</summary>
    public sealed class BattleSettlementReward
    {
        private const int CoinItemId = 2101;
        private readonly MainMenuPlayerState _player;
        private readonly Func<UniTask> _save;
        private int _claimedMultiplier;
        private bool _saving;

        public int BaseAmount { get; }
        public string ItemName { get; }
        public int DisplayAmount => checked(BaseAmount * Math.Max(1, _claimedMultiplier));
        public bool IsDoubled => _claimedMultiplier == 2;
        public bool IsSaved { get; private set; }

        public BattleSettlementReward(Tables tables, MainMenuPlayerState player,
            BattleSettlementResult result, Func<UniTask> save)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            var amount = 0;
            var found = false;
            foreach (var row in tables.TbResultAward.DataList)
            {
                if (row.CupRange.Length != 2 || player.Trophies < row.CupRange[0] || player.Trophies > row.CupRange[1]) continue;
                found = true;
                var awards = result == BattleSettlementResult.Victory ? row.WinAward : row.LoseAward;
                foreach (var award in awards)
                {
                    if (award.Length != 2 || award[0] != CoinItemId || award[1] < 0)
                        throw new InvalidOperationException("当前结算仅支持助力币奖励，请检查 ResultAward：" + row.Id);
                    amount = checked(amount + award[1]);
                }
                break;
            }
            if (!found) throw new InvalidOperationException("ResultAward 缺少当前杯数的结算配置：" + player.Trophies);
            BaseAmount = amount;
            ItemName = tables.TbItemBase.Get(CoinItemId).Name;
        }

        public async UniTask ClaimAsync(bool doubleReward)
        {
            if (_saving) return;
            var multiplier = Math.Max(_claimedMultiplier, doubleReward ? 2 : 1);
            if (IsSaved && multiplier == _claimedMultiplier) return;
            _saving = true;
            try
            {
                var additional = checked(BaseAmount * (multiplier - _claimedMultiplier));
                _player.AddCoins(additional);
                _claimedMultiplier = multiplier;
                IsSaved = false;
                await _save();
                IsSaved = true;
            }
            finally { _saving = false; }
        }
    }
}
