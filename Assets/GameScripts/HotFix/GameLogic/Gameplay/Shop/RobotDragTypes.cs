namespace GameLogic
{
    public enum RobotDragSourceKind { OwnedRobot, ShopOffer }

    public readonly struct RobotDragSource
    {
        public RobotDragSourceKind Kind { get; }
        public int InstanceId { get; }
        public int SlotId { get; }
        public long OfferId { get; }

        private RobotDragSource(RobotDragSourceKind kind, int instanceId, int slotId, long offerId)
        {
            Kind = kind;
            InstanceId = instanceId;
            SlotId = slotId;
            OfferId = offerId;
        }

        public static RobotDragSource Owned(int id) => new RobotDragSource(RobotDragSourceKind.OwnedRobot, id, -1, 0);
        public static RobotDragSource Offer(int slotId, long offerId) => new RobotDragSource(RobotDragSourceKind.ShopOffer, 0, slotId, offerId);
    }

    /// <summary>不可变手势身份；只有创建它的Context可以提交，原占格在拿起时释放。</summary>
    public sealed class RobotDragSession
    {
        public long InteractionId { get; }
        public long BattleId { get; }
        public BattleSide Side { get; }
        public RobotDragSource Source { get; }
        public RobotLocation OriginalLocation { get; }
        public BoardModel OriginalBoard { get; }
        public BoardCoordinate? OriginalAnchor { get; }

        internal RobotDragSession(long interactionId, long battleId, BattleSide side, RobotDragSource source,
            RobotLocation originalLocation, BoardModel originalBoard, BoardCoordinate? originalAnchor)
        {
            InteractionId = interactionId;
            BattleId = battleId;
            Side = side;
            Source = source;
            OriginalLocation = originalLocation;
            OriginalBoard = originalBoard;
            OriginalAnchor = originalAnchor;
        }
    }

    public readonly struct RobotMergeSnapshot
    {
        public readonly long BattleId;
        public readonly long InteractionId;
        public readonly BattleSide Side;
        public readonly RobotDragSource Source;
        public readonly int TargetInstanceId;
        public readonly int PreviousLevel;
        public readonly int Level;

        internal RobotMergeSnapshot(long battleId, long interactionId, BattleSide side, RobotDragSource source,
            int targetInstanceId, int previousLevel, int level)
        {
            BattleId = battleId;
            InteractionId = interactionId;
            Side = side;
            Source = source;
            TargetInstanceId = targetInstanceId;
            PreviousLevel = previousLevel;
            Level = level;
        }
    }

    public readonly struct RobotActionToken
    {
        public readonly long BattleId;
        public readonly int InstanceId;
        public readonly long ActionVersion;
        public readonly int Level;

        public RobotActionToken(long battleId, int instanceId, long actionVersion, int level)
        {
            BattleId = battleId;
            InstanceId = instanceId;
            ActionVersion = actionVersion;
            Level = level;
        }
    }
}
