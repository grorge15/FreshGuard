using System;
using UnityEngine;

namespace GameLogic
{
    public enum SkillOwnerKind { Battlefield, Entity }
    public readonly struct SkillOwnerRef : IEquatable<SkillOwnerRef>
    {
        public long BattleId { get; }
        public SkillOwnerKind Kind { get; }
        public BattleSide Side { get; }
        public int? EntityId { get; }
        private SkillOwnerRef(long battleId, SkillOwnerKind kind, BattleSide side, int? entityId)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            EntityValidation.RequireSide(side);
            BattleId = battleId; Kind = kind; Side = side; EntityId = entityId;
        }
        public static SkillOwnerRef Battlefield(long battleId, BattleSide side) =>
            new SkillOwnerRef(battleId, SkillOwnerKind.Battlefield, side, null);
        public static SkillOwnerRef Entity(long battleId, BattleSide side, int entityId)
        {
            EntityValidation.RequirePositive(entityId, nameof(entityId));
            return new SkillOwnerRef(battleId, SkillOwnerKind.Entity, side, entityId);
        }
        public bool Equals(SkillOwnerRef other) => BattleId == other.BattleId && Kind == other.Kind &&
            Side == other.Side && EntityId == other.EntityId;
        public override bool Equals(object obj) => obj is SkillOwnerRef other && Equals(other);
        public override int GetHashCode() => ((BattleId.GetHashCode() * 397 ^ (int)Kind) * 397 ^ (int)Side) * 397 ^ EntityId.GetHashCode();
    }
    public interface ISkillOwner
    {
        SkillOwnerRef OwnerRef { get; }
        SkillContainer Skills { get; }
        BuffContainer Buffs { get; }
        bool CanCast { get; }
    }
    public enum SkillTargetKind { None, Battlefield, Entity, Position }
    public readonly struct SkillTarget
    {
        public SkillTargetKind Kind { get; }
        public BattleSide Side { get; }
        public int EntityId { get; }
        public Vector3 Position { get; }
        private SkillTarget(SkillTargetKind kind, BattleSide side, int entityId, Vector3 position)
        { EntityValidation.RequireSide(side); Kind = kind; Side = side; EntityId = entityId; Position = position; }
        public static SkillTarget None => default(SkillTarget);
        public static SkillTarget Battlefield(BattleSide side) => new SkillTarget(SkillTargetKind.Battlefield, side, 0, default(Vector3));
        public static SkillTarget Entity(BattleSide side, int id)
        { EntityValidation.RequirePositive(id, nameof(id)); return new SkillTarget(SkillTargetKind.Entity, side, id, default(Vector3)); }
        public static SkillTarget AtPosition(BattleSide side, Vector3 position) => new SkillTarget(SkillTargetKind.Position, side, 0, position);
    }
    public enum SkillTriggerSource { Contact, Button, AI }
    public sealed class SkillCastRequest
    {
        public SkillOwnerRef Owner { get; }
        public int SkillId { get; }
        public SkillTriggerSource Trigger { get; }
        public SkillTarget Target { get; }
        public SkillCastRequest(SkillOwnerRef owner, int skillId, SkillTriggerSource trigger, SkillTarget target)
        { Owner = owner; SkillId = skillId; Trigger = trigger; Target = target; }
    }
}
