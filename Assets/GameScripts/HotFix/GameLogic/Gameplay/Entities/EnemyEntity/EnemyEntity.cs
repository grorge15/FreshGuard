using System;
using TEngine;
using GameConfig.enemy;

namespace GameLogic
{
    /// <summary>
    /// 框架内存池对象。只允许管理器获取、初始化和归还；查询引用仅供当前同步操作使用。
    /// 延迟操作保存实例 ID，归还后不得继续访问对象引用。
    /// </summary>
    public class EnemyEntity : Entity, IMemory
    {
        public override EntityKind Kind => EntityKind.Enemy;
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public float BaseSpeed { get; private set; }
        public float MoveSpeed { get; private set; }
        public float RouteProgress { get; private set; }
        public Enemy Configuration { get; private set; }
        public int Cycle { get; private set; }
        public int Wave { get; private set; }
        public bool IsAlive => !IsRemoved && CurrentHp > 0;
        public bool IsIsolated { get; private set; }
        public override bool CanCast => IsAlive;

        // MemoryPool.Acquire<T>() requires a public parameterless constructor.
        public EnemyEntity() { }

        internal void Initialize(int instanceId, int configId, BattleSide side, int maxHp, float baseSpeed)
        {
            Configuration = null;
            Cycle = 0;
            Wave = 0;
            InitializeIdentity(instanceId, configId, side);
            MaxHp = maxHp;
            CurrentHp = maxHp;
            BaseSpeed = baseSpeed;
            MoveSpeed = baseSpeed;
            RouteProgress = 0f;
            IsIsolated = false;
        }

        // 行为负责属性变化规则；实体只校验并保存当前值。
        internal void SetMoveSpeed(float speed)
        {
            EntityValidation.RequireFiniteNonNegative(speed, nameof(speed));
            MoveSpeed = speed;
        }

        internal void SetIsolated(bool isolated) => IsIsolated = isolated;

        internal void InitializeFromConfig(int instanceId, Enemy config, BattleSide side,
            int cycle, int wave, int maxHp)
        {
            Initialize(instanceId, config.Id, side, maxHp, config.EnemySpeed);
            Configuration = config;
            Cycle = cycle;
            Wave = wave;
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
            MoveSpeed = 0f;
            RouteProgress = 0f;
            Configuration = null;
            Cycle = 0;
            Wave = 0;
            IsIsolated = false;
        }
    }
}
