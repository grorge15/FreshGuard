using System;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 框架内存池对象。只允许管理器获取、初始化和归还；查询引用仅供当前同步操作使用。
    /// 延迟操作保存实例 ID，归还后不得继续访问对象引用。
    /// </summary>
    public sealed class EnemyEntity : Entity, IMemory
    {
        public override EntityKind Kind => EntityKind.Enemy;
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public float BaseSpeed { get; private set; }
        public float RouteProgress { get; private set; }
        public bool IsAlive => !IsRemoved && CurrentHp > 0;

        // MemoryPool.Acquire<T>() requires a public parameterless constructor.
        public EnemyEntity() { }

        internal void Initialize(int instanceId, int configId, BattleSide side, int maxHp, float baseSpeed)
        {
            InitializeIdentity(instanceId, configId, side);
            MaxHp = maxHp;
            CurrentHp = maxHp;
            BaseSpeed = baseSpeed;
            RouteProgress = 0f;
        }

        internal bool TrySetRouteProgress(float progress)
        {
            if (IsRemoved || !EntityValidation.IsFiniteNonNegative(progress)) return false;
            RouteProgress = progress;
            return true;
        }

        internal void ApplyDamage(int damage)
        {
            if (IsAlive && damage > 0) CurrentHp = Math.Max(0, CurrentHp - damage);
        }

        void IMemory.Clear()
        {
            ClearIdentity();
            MaxHp = 0;
            CurrentHp = 0;
            BaseSpeed = 0f;
            RouteProgress = 0f;
        }
    }
}
