using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic
{
    public enum MysteriousStoreQuality { Green, Blue, Purple, Orange }
    public enum MysteriousStorePurchaseResult { Success, SoldOut, InsufficientBalance, SlotsFull, Unavailable }

    public sealed class MysteriousStoreOfferData
    {
        private readonly int[] _prices;
        public int BuffId { get; }
        public string Name { get; }
        public string Description { get; }
        public MysteriousStoreQuality Quality { get; }
        public int MaxLevel => _prices.Length;

        public MysteriousStoreOfferData(int buffId, string name, string description,
            MysteriousStoreQuality quality, params int[] prices)
        {
            if (buffId <= 0 || prices == null || prices.Length == 0 || prices.Any(price => price < 0))
                throw new ArgumentException("助力和层数价格必须有效。");
            BuffId = buffId;
            Name = name;
            Description = description;
            Quality = quality;
            _prices = (int[])prices.Clone();
        }

        public int PriceAt(int level) => level > 0 && level <= MaxLevel ? _prices[level - 1] : 0;
    }

    public sealed class MysteriousStoreHeldBuffData
    {
        public MysteriousStoreOfferData Buff { get; }
        public int BuffId => Buff.BuffId;
        public int Level { get; internal set; }
        public int Slot { get; }

        public MysteriousStoreHeldBuffData(MysteriousStoreOfferData buff, int level, int slot)
        {
            Buff = buff ?? throw new ArgumentNullException(nameof(buff));
            if (level < 1 || level > buff.MaxLevel || slot < 0 || slot >= MysteriousStorePreviewSession.SlotCount)
                throw new ArgumentOutOfRangeException(nameof(level));
            Level = level;
            Slot = slot;
        }
    }

    /// <summary>商品和助力仍为内存演示数据；助力币直接使用主菜单玩家状态，由宿主负责保存。</summary>
    public sealed class MysteriousStorePreviewSession
    {
        public const int SlotCount = 6;
        private readonly List<MysteriousStoreHeldBuffData> _held;
        public MainMenuPlayerState PlayerState { get; }
        public IReadOnlyList<MysteriousStoreOfferData> Offers { get; }
        public IReadOnlyList<MysteriousStoreHeldBuffData> HeldBuffs { get; }
        public bool HasPurchased { get; private set; }

        public MysteriousStorePreviewSession(MainMenuPlayerState playerState, IEnumerable<MysteriousStoreOfferData> offers,
            IEnumerable<MysteriousStoreHeldBuffData> heldBuffs = null)
        {
            PlayerState = playerState ?? throw new ArgumentNullException(nameof(playerState));
            var offerList = new List<MysteriousStoreOfferData>(offers ?? throw new ArgumentNullException(nameof(offers)));
            if (offerList.Count > 3 || offerList.Any(item => item == null) ||
                offerList.Select(item => item.BuffId).Distinct().Count() != offerList.Count)
                throw new ArgumentException("商品必须是最多三种不同助力。", nameof(offers));
            _held = heldBuffs == null ? new List<MysteriousStoreHeldBuffData>() :
                heldBuffs.Select(item => new MysteriousStoreHeldBuffData(item.Buff, item.Level, item.Slot)).ToList();
            if (_held.Count > SlotCount || _held.Select(item => item.Slot).Distinct().Count() != _held.Count ||
                _held.Select(item => item.BuffId).Distinct().Count() != _held.Count)
                throw new ArgumentException("持有助力的 ID 和槽位不能重复。", nameof(heldBuffs));
            Offers = offerList.AsReadOnly();
            HeldBuffs = _held.AsReadOnly();
        }

        public static MysteriousStorePreviewSession CreateDefault(MainMenuPlayerState playerState)
        {
            var purple = new MysteriousStoreOfferData(1800, "紫色示例助力", "提升守护力量，\n为下一场战斗提供\n额外助力。", MysteriousStoreQuality.Purple, 40, 60, 80);
            var orange = new MysteriousStoreOfferData(1801, "橙色示例助力", "强化战斗表现，\n助力效果随持有\n层数逐步提升。", MysteriousStoreQuality.Orange, 40, 60, 80);
            var blue = new MysteriousStoreOfferData(1802, "蓝色示例助力", "获得全新的助力，\n购买后加入下方\n当前助力栏。", MysteriousStoreQuality.Blue, 160, 180, 200);
            return new MysteriousStorePreviewSession(playerState, new[] { purple, orange, blue });
        }

        public MysteriousStoreHeldBuffData FindHeld(int buffId) => _held.Find(item => item.BuffId == buffId);
        public int GetHeldLevel(int buffId) => FindHeld(buffId)?.Level ?? 0;
        public int GetTargetLevel(int index) => GetHeldLevel(Offers[index].BuffId) + 1;
        public int GetPrice(int index) => Offers[index].PriceAt(GetTargetLevel(index));

        public MysteriousStorePurchaseResult TryBuy(int index)
        {
            if (HasPurchased) return MysteriousStorePurchaseResult.SoldOut;
            if (index < 0 || index >= Offers.Count) return MysteriousStorePurchaseResult.Unavailable;
            var offer = Offers[index];
            var level = GetTargetLevel(index);
            if (level > offer.MaxLevel) return MysteriousStorePurchaseResult.Unavailable;
            var price = GetPrice(index);
            if (PlayerState.Coins < price) return MysteriousStorePurchaseResult.InsufficientBalance;
            var held = FindHeld(offer.BuffId);
            if (held == null && _held.Count == SlotCount) return MysteriousStorePurchaseResult.SlotsFull;
            if (!PlayerState.TrySpendCoins(price)) return MysteriousStorePurchaseResult.InsufficientBalance;
            if (held == null)
            {
                var slot = Enumerable.Range(0, SlotCount).First(value => _held.All(item => item.Slot != value));
                _held.Add(new MysteriousStoreHeldBuffData(offer, 1, slot));
            }
            else held.Level = level;
            HasPurchased = true;
            return MysteriousStorePurchaseResult.Success;
        }

        public bool Remove(int buffId) => _held.RemoveAll(item => item.BuffId == buffId) != 0;
    }
}
