using System;
using System.Linq;
using GameConfig;

namespace GameLogic
{
    /// <summary>开战前冻结的杯数和金币规则；重启恢复不依赖之后修改的配置。</summary>
    [Serializable]
    public sealed class BattleRewardRules
    {
        public const int CoinItemId = 2101;
        public int StartingTrophies;
        public int WinCup;
        public int LoseCup;
        public int WinCoins;
        public int LoseCoins;
        public string ItemName;

        public static BattleRewardRules FromTables(Tables tables, int trophies)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (trophies < 0) throw new ArgumentOutOfRangeException(nameof(trophies));
            var rows = tables.TbResultAward.DataList.OrderBy(row => row.CupRange.FirstOrDefault()).ToArray();
            if (rows.Length == 0) throw new InvalidOperationException("ResultAward 缺少结算配置。");
            long next = 0;
            BattleRewardRules selected = null;
            foreach (var row in rows)
            {
                if (row.CupRange.Length != 2 || row.CupRange[0] != next || row.CupRange[1] < row.CupRange[0] ||
                    row.WinCup < 0 || row.LoseCup > 0)
                    throw new InvalidOperationException("ResultAward 杯数区间必须从0连续且不重叠，胜利加杯、失败扣杯：" + row.Id);
                var rule = new BattleRewardRules
                {
                    StartingTrophies = trophies, WinCup = row.WinCup, LoseCup = row.LoseCup,
                    WinCoins = ReadCoins(row.WinAward, row.Id), LoseCoins = ReadCoins(row.LoseAward, row.Id),
                    ItemName = tables.TbItemBase.Get(CoinItemId).Name
                };
                // 超出最高档上限时，继续沿用最后一档；不设置玩法杯数上限。
                if (trophies >= row.CupRange[0]) selected = rule;
                next = (long)row.CupRange[1] + 1;
            }
            return selected ?? throw new InvalidOperationException("ResultAward 缺少当前杯数的结算配置：" + trophies);
        }

        private static int ReadCoins(int[][] awards, int rowId)
        {
            var amount = 0;
            foreach (var award in awards)
            {
                if (award.Length != 2 || award[0] != CoinItemId || award[1] < 0)
                    throw new InvalidOperationException("当前结算仅支持助力币奖励，请检查 ResultAward：" + rowId);
                amount = checked(amount + award[1]);
            }
            return amount;
        }

        public int GetTrophiesAfter(BattleSettlementResult result) =>
            (int)Math.Max(0L, Math.Min(int.MaxValue, (long)StartingTrophies +
                (result == BattleSettlementResult.Victory ? WinCup : LoseCup)));
    }

    /// <summary>与玩家余额一起原子保存的本局账目，领取倍数也随存档保留。</summary>
    [Serializable]
    public sealed class BattleSettlementRecord
    {
        public string EntryId;
        public BattleSettlementResult Result;
        public bool Abandoned;
        public int TrophiesBefore;
        public int TrophiesAfter;
        public bool HasOpponentTrophies;
        public int OpponentTrophiesBefore;
        public int OpponentTrophiesAfter;
        public int BaseCoins;
        public string ItemName;
        public int ClaimedMultiplier;
        public int TrophyDelta => TrophiesAfter - TrophiesBefore;
        public int OpponentTrophyDelta => OpponentTrophiesAfter - OpponentTrophiesBefore;

        internal void EnsureOpponentTrophies(BattleRewardRules rules)
        {
            if (HasOpponentTrophies || rules == null) return;
            // 当前匹配双方使用相同的入场杯数，敌方按相反胜负独立结算并保底为0。
            OpponentTrophiesBefore = rules.StartingTrophies;
            OpponentTrophiesAfter = rules.GetTrophiesAfter(Result == BattleSettlementResult.Victory
                ? BattleSettlementResult.Defeat : BattleSettlementResult.Victory);
            HasOpponentTrophies = true;
        }
    }
}
