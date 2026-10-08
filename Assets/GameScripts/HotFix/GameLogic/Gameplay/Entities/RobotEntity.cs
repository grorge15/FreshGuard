using System;

namespace GameLogic
{
    /// <summary>只维护机器人自身状态；放置、拖拽和合成事务由业务系统完成。</summary>
    public sealed class RobotEntity : Entity
    {
        public override EntityKind Kind => EntityKind.Robot;
        public int Level { get; private set; }
        public int MaxLevel { get; }
        public int ShapeId { get; }
        public int SkillId { get; }
        public RobotLocation Location { get; private set; }
        public BoardCoordinate? Anchor { get; private set; }
        public bool IsDragging { get; private set; }
        public float TriggerCooldownRemaining { get; private set; }
        public bool CanParticipate => !IsRemoved && Location == RobotLocation.Board && !IsDragging;

        internal RobotEntity(int instanceId, int configId, BattleSide side, int shapeId, int skillId,
            int level, int maxLevel)
        {
            InitializeIdentity(instanceId, configId, side);
            ShapeId = shapeId;
            SkillId = skillId;
            Level = level;
            MaxLevel = maxLevel;
            Location = RobotLocation.Unplaced;
        }

        internal bool TrySetLocation(RobotLocation location, BoardCoordinate? anchor)
        {
            if (IsRemoved) return false;
            switch (location)
            {
                case RobotLocation.Board:
                    if (!anchor.HasValue || anchor.Value.Column < 0 || anchor.Value.Row < 0) return false;
                    break;
                case RobotLocation.Unplaced:
                case RobotLocation.Storage:
                case RobotLocation.PurchasedShopSlot:
                    if (anchor.HasValue) return false;
                    break;
                default:
                    return false;
            }
            Location = location;
            Anchor = anchor;
            IsDragging = false;
            return true;
        }

        internal bool TrySetDragging(bool dragging)
        {
            if (IsRemoved) return false;
            IsDragging = dragging;
            return true;
        }

        internal bool TryUpgrade()
        {
            if (IsRemoved || Level >= MaxLevel) return false;
            Level++;
            TriggerCooldownRemaining = 0f;
            return true;
        }

        internal bool TryConsumeAttackTrigger(float protectionSeconds)
        {
            EntityValidation.RequireFiniteNonNegative(protectionSeconds, nameof(protectionSeconds));
            if (!CanParticipate || TriggerCooldownRemaining > 0f) return false;
            TriggerCooldownRemaining = protectionSeconds;
            return true;
        }

        internal void AdvanceTime(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRemoved) TriggerCooldownRemaining = Math.Max(0f, TriggerCooldownRemaining - deltaTime);
        }

        protected override void OnRemoved()
        {
            Location = RobotLocation.Unplaced;
            Anchor = null;
            IsDragging = false;
            TriggerCooldownRemaining = 0f;
        }
    }
}
