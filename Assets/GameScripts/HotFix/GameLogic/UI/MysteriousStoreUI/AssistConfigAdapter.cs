using System;
using System.Collections.Generic;
using System.Linq;
using GameConfig;

namespace GameLogic
{
    /// <summary>商品身份与效果身份分开处理；体力补给由商品分支立即发放。</summary>
    public static class AssistConfigAdapter
    {
        private static readonly HashSet<string> Behaviors = new HashSet<string>
        {
            "OpeningBallSpeedBonus", "ComboBallSpeedBonus", "ComboRobotDamageBonus",
            "GlassDurabilityOverride", "ExtraBerserkBall"
        };

        public static IReadOnlyList<MysteriousStoreOfferData> FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var offers = new List<MysteriousStoreOfferData>();
            foreach (var goods in tables.TbShopGoods.DataList.Where(row => row.Id >= 1801 && row.Id <= 1806).OrderBy(row => row.Id))
            {
                if (goods.HasLevel != 0 && goods.HasLevel != 1)
                    throw new InvalidOperationException("商品hasLevel必须为0或1：" + goods.Id);
                var quality = Quality(goods.Quality);
                if (goods.Id == 1806)
                {
                    offers.Add(new MysteriousStoreOfferData(goods.Id, 0, goods.Name, goods.Icon, goods.Descriptions,
                        quality, goods.Price, false, 1, true));
                    continue;
                }
                var buff = tables.TbBuff.GetOrDefault(goods.BuffId);
                if (buff == null || !Behaviors.Contains(buff.BehaviorKey) || buff.MaxLevel < 1)
                    throw new InvalidOperationException("商品引用了尚未实现或无效的助力：" + goods.Id + "/" + goods.BuffId);
                var name = string.IsNullOrWhiteSpace(goods.Name) ? buff.Name : goods.Name;
                var icon = string.IsNullOrWhiteSpace(goods.Icon) ? buff.Icon : goods.Icon;
                var descriptions = goods.Descriptions.Length == 0 ? buff.Descriptions : goods.Descriptions;
                offers.Add(new MysteriousStoreOfferData(goods.Id, goods.BuffId, name, icon, descriptions,
                    quality, goods.Price, goods.HasLevel == 1, goods.HasLevel == 1 ? buff.MaxLevel : 1));
            }
            return offers.AsReadOnly();
        }

        public static IReadOnlyList<MatchBuff> ToMatchBuffs(MainMenuPlayerState state, Tables tables, DateTimeOffset now)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var catalog = FromTables(tables).Where(offer => !offer.IsInstantReward).GroupBy(offer => offer.BuffId)
                .ToDictionary(group => group.Key, group => group.First());
            var result = new List<MatchBuff>();
            foreach (var held in state.HeldAssists)
            {
                if (held.IsExpired(now) || !catalog.TryGetValue(held.BuffId, out var offer)) continue;
                if (held.Level > offer.MaxLevel)
                    throw new InvalidOperationException("持有助力超过配置最高等级：" + held.BuffId);
                result.Add(new MatchBuff(held.BuffId, offer.Name, offer.IconLocation, held.Level, held.ExpiresUtcTicks));
            }
            return result.AsReadOnly();
        }

        public static IReadOnlyList<MatchBuff> UnlockedMatchBuffs(Tables tables) => FromTables(tables)
            .Where(offer => !offer.IsInstantReward).GroupBy(offer => offer.BuffId)
            .Select(group => group.First()).Select(offer => new MatchBuff(offer.BuffId, offer.Name, offer.IconLocation, 1))
            .ToList().AsReadOnly();

        private static MysteriousStoreQuality Quality(int value)
        {
            switch (value)
            {
                case 2: return MysteriousStoreQuality.Green;
                case 3: return MysteriousStoreQuality.Blue;
                case 4: return MysteriousStoreQuality.Purple;
                case 5: return MysteriousStoreQuality.Orange;
                default: throw new InvalidOperationException("未知神秘商店商品品质：" + value);
            }
        }
    }
}
