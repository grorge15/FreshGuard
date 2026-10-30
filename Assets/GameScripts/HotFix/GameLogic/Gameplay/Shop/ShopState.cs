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
        StorageFull,
        IncompatibleMerge,
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

    /// <summary>SlotId为局内稳定身份，不是列表下标；Offer和InstanceId互斥，空项移除。</summary>
    public sealed class ShopSlot
    {
        public int SlotId { get; }
        public ShopOffer Offer { get; internal set; }
        public int? InstanceId { get; internal set; }
        public bool IsPurchased => InstanceId.HasValue;

        internal ShopSlot(int slotId) { SlotId = slotId; }
    }

    public sealed class BattleShopState
    {
        private readonly List<ShopSlot> _slots = new List<ShopSlot>();
        private int _nextSlotId;
        public BattleSide Side { get; }
        public int EnergyCoins { get; internal set; }
        public IReadOnlyList<ShopSlot> Slots { get; }
        public int? StorageInstanceId { get; internal set; }
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

        internal ShopSlot AddSlot()
        {
            var slot = new ShopSlot(_nextSlotId++);
            _slots.Add(slot);
            return slot;
        }
        internal void RemoveSlot(ShopSlot slot) => _slots.Remove(slot);
        internal void ClearSlots() => _slots.Clear();
    }
}
