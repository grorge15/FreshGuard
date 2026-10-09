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

    /// <summary>槽ID为0..2；Offer和InstanceId互斥，已购实体离槽后两者都为空。</summary>
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
        public BattleSide Side { get; }
        public int EnergyCoins { get; internal set; }
        public IReadOnlyList<ShopSlot> Slots { get; }
        public int? StorageInstanceId { get; internal set; }
        public IReadOnlyList<int> LastBatchRobotIds { get; internal set; }

        internal BattleShopState(BattleSide side, ShopRules rules)
        {
            Side = side;
            EnergyCoins = rules.InitialCoins;
            var slots = new List<ShopSlot>();
            for (var index = 0; index < rules.SlotCount; index++) slots.Add(new ShopSlot(index));
            Slots = slots.AsReadOnly();
            LastBatchRobotIds = new List<int>().AsReadOnly();
        }
    }
}
