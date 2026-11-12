using System.Collections.Generic;

namespace GameLogic
{
    public enum ShopOperationResult
    {
        Success,
        BattleEnded,
        Busy,
        InvalidArgument,
        WrongSide,
        InvalidSlot,
        StaleOffer,
        AlreadyPurchased,
        InsufficientCoins,
        EntityNotFound,
        InvalidLocation,
        InvalidPlacement,
        IncompatibleMerge = 13,
        MaxLevelReached,
        InvalidConfiguration,
        WrongBattle,
        DuplicateReward,
        CoinOverflow,
        ResourceFailed,
        ShopFull,
        InvalidTarget = InvalidPlacement,
        InvalidSource = InvalidLocation,
        InsufficientFunds = InsufficientCoins
    }

    public static class BattleShopEvents
    {
        public const int Changed = 0x53484F50;
        public const int Merged = 0x53484F51;
        public const int MergeSucceeded = 0x53484F52;
    }

    public sealed class ShopOffer
    {
        public long OfferId { get; }
        public int RobotId { get; }
        public int Level { get; }
        public int Price { get; }

        internal ShopOffer(long offerId, int robotId, int level, int price)
        {
            OfferId = offerId;
            RobotId = robotId;
            Level = level;
            Price = price;
        }
    }

    /// <summary>每批固定三个槽位；SlotId为稳定身份，Offer和InstanceId互斥，空槽保留原位。</summary>
    public sealed class ShopSlot
    {
        public int SlotId { get; }
        public ShopOffer Offer { get; internal set; }
        public int? InstanceId { get; internal set; }
        public bool IsPurchased => InstanceId.HasValue;
        public bool IsEmpty => Offer == null && !InstanceId.HasValue;

        internal ShopSlot(int slotId) { SlotId = slotId; }
    }

    public sealed class BattleShopState
    {
        private readonly List<ShopSlot> _slots = new List<ShopSlot>();
        private int _nextSlotId;
        public BattleSide Side { get; }
        public int EnergyCoins { get; internal set; }
        public IReadOnlyList<ShopSlot> Slots { get; }
        public IReadOnlyList<int> LastBatchRobotIds { get; internal set; }

        internal BattleShopState(BattleSide side, ShopRules rules)
        {
            Side = side;
            EnergyCoins = rules.InitialCoins;
            Slots = _slots.AsReadOnly();
            LastBatchRobotIds = new List<int>().AsReadOnly();
        }
        public ShopSlot FindSlot(int slotId)
        {
            foreach (var slot in _slots) if (slot.SlotId == slotId) return slot;
            return null;
        }

        internal bool HasIssuedSlot(int slotId) => slotId >= 0 && slotId < _nextSlotId;

        internal void ReplaceOffers(ShopOffer[] offers)
        {
            if (offers == null || offers.Length != ShopRules.ShopCapacity)
                throw new System.ArgumentException("每批必须提供三个商品。", nameof(offers));
            _slots.Clear();
            foreach (var offer in offers)
                _slots.Add(new ShopSlot(_nextSlotId++) { Offer = offer });
        }
        internal void ClearSlots() => _slots.Clear();
    }
}
