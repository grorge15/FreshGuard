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
        public float RouteProgress { get; private set; }
        public Enemy Configuration { get; private set; }
        public int Cycle { get; private set; }
        public bool IsAlive => !IsRemoved && CurrentHp > 0;
        public override bool CanCast => IsAlive;
        public float EffectiveSpeed
        {
            get
            {
                var slow = 0f;
                // 配置语义是“实际承受控制的比例”，1表示完整承受，0表示免疫。
                var receivedControl = Math.Max(0f, Math.Min(1f, Configuration?.ControlResist ?? 1f));
                foreach (var buff in Buffs.Items)
                    if (buff.Model.BehaviorKey == "slow" && buff.Remaining > 0f)
                        slow = Math.Max(slow, buff.Get("slow") * receivedControl);
                return BaseSpeed * Math.Max(0.5f, 1f - slow);
            }
        }

        // MemoryPool.Acquire<T>() requires a public parameterless constructor.
        public EnemyEntity() { }

        internal void Initialize(int instanceId, int configId, BattleSide side, int maxHp, float baseSpeed)
        {
            Configuration = null;
            Cycle = 0;
            InitializeIdentity(instanceId, configId, side);
            MaxHp = maxHp;
            CurrentHp = maxHp;
            BaseSpeed = baseSpeed;
            RouteProgress = 0f;
        }

        internal void InitializeFromConfig(int instanceId, Enemy config, BattleSide side, int cycle)
        {
            Initialize(instanceId, config.Id, side, cycle == 1 ? config.HpC1 : config.HpC2, config.EnemySpeed);
            Configuration = config;
            Cycle = cycle;
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
            Configuration = null;
            Cycle = 0;
        }
    }
}
