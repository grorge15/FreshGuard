using System;
using System.Collections.Generic;

namespace GameLogic
{
    public enum OpponentAiActionKind
    {
        Wait,
        PurchaseAndDeploy,
        PurchaseAndMerge,
        Merge,
        Move,
        Refresh
    }

    public readonly struct OpponentAiRobotProfile
    {
        public int RobotId { get; }
        public int QualityId { get; }
        public int Damage { get; }
        public int DamagePerLevel { get; }
        public int LevelOnePrice { get; }
        public float TriggerInterval { get; }
        public PlaceableDefinition Definition { get; }

        public OpponentAiRobotProfile(int robotId, int qualityId, int damage, float triggerInterval,
            PlaceableDefinition definition, int damagePerLevel = 0, int levelOnePrice = 6)
        {
            RobotId = robotId;
            QualityId = qualityId;
            Damage = damage;
            DamagePerLevel = damagePerLevel;
            LevelOnePrice = levelOnePrice;
            TriggerInterval = triggerInterval;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public int DamageAt(int level) => Damage + Math.Max(0, level - 1) * DamagePerLevel;
    }

    public readonly struct OpponentAiOfferSnapshot
    {
        public int SlotId { get; }
        public long OfferId { get; }
        public OpponentAiRobotProfile Profile { get; }
        public int Level { get; }
        public int Price { get; }
        public bool IsPurchased { get; }

        public OpponentAiOfferSnapshot(int slotId, long offerId, OpponentAiRobotProfile profile,
            int level, int price, bool isPurchased)
        {
            SlotId = slotId;
            OfferId = offerId;
            Profile = profile;
            Level = level;
            Price = price;
            IsPurchased = isPurchased;
        }
    }

    public readonly struct OpponentAiRobotSnapshot
    {
        public int InstanceId { get; }
        public OpponentAiRobotProfile Profile { get; }
        public int Level { get; }
        public int MaxLevel { get; }
        public int Damage { get; }
        public float TriggerInterval { get; }
        public RobotLocation Location { get; }
        public BoardCoordinate? Anchor { get; }
        public PlaceableDefinition Definition { get; }

        public OpponentAiRobotSnapshot(int instanceId, OpponentAiRobotProfile profile, int level, int maxLevel,
            int damage, float triggerInterval, RobotLocation location, BoardCoordinate? anchor,
            PlaceableDefinition definition)
        {
            InstanceId = instanceId;
            Profile = profile;
            Level = level;
            MaxLevel = maxLevel;
            Damage = damage;
            TriggerInterval = triggerInterval;
            Location = location;
            Anchor = anchor;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool IsOnBoard => Location == RobotLocation.Board && Anchor.HasValue;
    }

    public sealed class OpponentAiSnapshot
    {
        public BattleSide Side { get; }
        public int EnergyCoins { get; }
        private readonly BoardModel _board;
        // 每次返回副本，调用者和模拟器都不能更改已捕获的状态。
        public BoardModel Board => CopyBoard(_board);
        public long BattleId { get; }
        public bool HasPurchasedShopRobots { get; }
        public IReadOnlyList<OpponentAiOfferSnapshot> Offers { get; }
        public IReadOnlyList<OpponentAiRobotSnapshot> Robots { get; }
        public IReadOnlyList<OpponentAiRobotProfile> CandidateProfiles { get; }
        public int RefreshPrice { get; }
        public OpponentAiWorldSnapshot World { get; }
        public int BoardRevision => World?.BoardRevision ?? 0;
        public OpponentAiStrategyPhase StrategyPhase => World?.Phase ?? OpponentAiStrategyPhase.GlassExpansion;

        public OpponentAiSnapshot(BattleSide side, int energyCoins, BoardModel board,
            IReadOnlyList<OpponentAiOfferSnapshot> offers,
            IReadOnlyList<OpponentAiRobotSnapshot> robots,
            IReadOnlyList<OpponentAiRobotProfile> candidateProfiles,
            int refreshPrice = 5, long battleId = 0, bool hasPurchasedShopRobots = false,
            OpponentAiWorldSnapshot world = null)
        {
            if (side != BattleSide.Player && side != BattleSide.Opponent)
                throw new ArgumentOutOfRangeException(nameof(side));
            if (energyCoins < 0) throw new ArgumentOutOfRangeException(nameof(energyCoins));
            Side = side;
            EnergyCoins = energyCoins;
            _board = CopyBoard(board ?? throw new ArgumentNullException(nameof(board)));
            BattleId = battleId;
            Offers = new List<OpponentAiOfferSnapshot>(offers ?? throw new ArgumentNullException(nameof(offers))).AsReadOnly();
            Robots = new List<OpponentAiRobotSnapshot>(robots ?? throw new ArgumentNullException(nameof(robots))).AsReadOnly();
            CandidateProfiles = new List<OpponentAiRobotProfile>(candidateProfiles ?? throw new ArgumentNullException(nameof(candidateProfiles))).AsReadOnly();
            HasPurchasedShopRobots = hasPurchasedShopRobots;
            World = world;
            RefreshPrice = refreshPrice < 0 ? throw new ArgumentOutOfRangeException(nameof(refreshPrice)) : refreshPrice;
        }

        private OpponentAiSnapshot(OpponentAiSnapshot source, OpponentAiWorldSnapshot world)
        {
            // _board 仅在首次捕获时复制，之后从不修改或直接返回；只读集合也已脱离输入集合。
            // WithWorld 可跨线程共享这份私有结构，所有模拟仍通过 Board 获取独立副本。
            Side = source.Side;
            EnergyCoins = source.EnergyCoins;
            _board = source._board;
            BattleId = source.BattleId;
            Offers = source.Offers;
            Robots = source.Robots;
            CandidateProfiles = source.CandidateProfiles;
            HasPurchasedShopRobots = source.HasPurchasedShopRobots;
            RefreshPrice = source.RefreshPrice;
            World = world;
        }

        internal static BoardModel CopyBoard(BoardModel source) => source.Clone();

        public OpponentAiSnapshot WithWorld(OpponentAiWorldSnapshot world) => new OpponentAiSnapshot(
            this, world);
    }

    public readonly struct OpponentAiAction
    {
        public OpponentAiActionKind Kind { get; }
        public int SlotId { get; }
        public long OfferId { get; }
        public int SourceInstanceId { get; }
        public int TargetInstanceId { get; }
        public BoardCoordinate Anchor { get; }
        public long BattleId { get; }
        public long FreedOfferId { get; }
        public string DecisionReason { get; }
        public float ScoreBefore { get; }
        public float ScoreAfter { get; }
        public OpponentAiFollowUp? FollowUp { get; }
        public OpponentAiScoreReference? ScoreReference { get; }
        public OpponentAiEnclosureKind MinimumEnclosure { get; }

        public OpponentAiAction(OpponentAiActionKind kind, int slotId = -1, long offerId = 0,
            int sourceInstanceId = 0, int targetInstanceId = 0, BoardCoordinate anchor = default(BoardCoordinate),
            long battleId = 0, long freedOfferId = 0, string decisionReason = null,
            float scoreBefore = 0f, float scoreAfter = 0f, OpponentAiFollowUp? followUp = null,
            OpponentAiScoreReference? scoreReference = null, OpponentAiEnclosureKind minimumEnclosure = OpponentAiEnclosureKind.None)
        {
            Kind = kind;
            SlotId = slotId;
            OfferId = offerId;
            SourceInstanceId = sourceInstanceId;
            TargetInstanceId = targetInstanceId;
            Anchor = anchor;
            BattleId = battleId;
            FreedOfferId = freedOfferId;
            DecisionReason = decisionReason;
            ScoreBefore = scoreBefore;
            ScoreAfter = scoreAfter;
            FollowUp = followUp;
            ScoreReference = scoreReference;
            MinimumEnclosure = minimumEnclosure;
        }

        public static OpponentAiAction Wait() => new OpponentAiAction(OpponentAiActionKind.Wait);
    }

    public sealed class OpponentAiPlanningOptions
    {
        public bool AllowPurchase = true;
        public bool AllowMerge = true;
        public bool AllowMove = true;
        public bool AllowRefresh = true;
        public int InitialRobotTarget = 3;
        public long PreferredOfferId;
        public IReadOnlyCollection<long> BlockedOfferIds;
        public float ActiveTime;
        public IReadOnlyList<OpponentAiMoveMemory> RecentMoves;
        public bool AllowTwoStep = true;
        public OpponentAiFollowUp? PendingFollowUp;
    }
}
