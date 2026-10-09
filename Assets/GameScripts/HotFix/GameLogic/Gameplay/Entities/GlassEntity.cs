using System;
using TEngine;

namespace GameLogic
{
    /// <summary>玻璃耐久及所有球共享的受伤冷却；引用仅供当前同步操作读取。</summary>
    public sealed class GlassEntity : Entity, IMemory
    {
        public override EntityKind Kind => EntityKind.Glass;
        public BoardCoordinate Coordinate { get; private set; }
        public GlassKind GlassKind { get; private set; }
        public int MaxDurability { get; private set; }
        public int CurrentDurability { get; private set; }
        public int HitDurabilityLoss { get; private set; }
        public float CooldownInterval { get; private set; }
        public float DamageCooldownRemaining { get; private set; }
        public bool IsAlive => !IsRemoved && CurrentDurability > 0;

        // MemoryPool.Acquire<T>() 要求公开无参构造；初始化与归还由注册表完成。
        public GlassEntity() { }

        internal void Initialize(int instanceId, int configId, BattleSide side, BoardCoordinate coordinate,
            GlassKind kind, int maxDurability, int hitDurabilityLoss, float hitInterval)
        {
            InitializeIdentity(instanceId, configId, side);
            Coordinate = coordinate;
            GlassKind = kind;
            MaxDurability = maxDurability;
            CurrentDurability = maxDurability;
            HitDurabilityLoss = hitDurabilityLoss;
            CooldownInterval = hitInterval;
            DamageCooldownRemaining = 0f;
        }

        internal GlassCollisionResult ApplyCollision(bool isBerserk)
        {
            if (!IsAlive || isBerserk || DamageCooldownRemaining > 0f) return GlassCollisionResult.Ignored;
            CurrentDurability = Math.Max(0, CurrentDurability - HitDurabilityLoss);
            DamageCooldownRemaining = CooldownInterval;
            return CurrentDurability == 0 ? GlassCollisionResult.Broken : GlassCollisionResult.Damaged;
        }

        internal void AdvanceTime(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsAlive) DamageCooldownRemaining = Math.Max(0f, DamageCooldownRemaining - deltaTime);
        }

        void IMemory.Clear()
        {
            ClearIdentity();
            Coordinate = default(BoardCoordinate);
            GlassKind = default(GlassKind);
            MaxDurability = 0;
            CurrentDurability = 0;
            HitDurabilityLoss = 0;
            CooldownInterval = 0f;
            DamageCooldownRemaining = 0f;
        }
    }
}
