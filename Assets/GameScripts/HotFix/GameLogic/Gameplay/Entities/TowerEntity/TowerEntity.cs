using System;

namespace GameLogic
{
    public sealed class TowerEntity : Entity
    {
        public override EntityKind Kind => EntityKind.Tower;
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public bool IsAlive => !IsRemoved && CurrentHp > 0;
        public event Action<TowerHealthSnapshot> HealthChanged;
        public event Action<TowerHealthSnapshot> Died;
        private bool _notifying;

        // 每局仅两座塔，使用普通对象，避免通知调用栈中的池复用。
        internal TowerEntity(int instanceId, int configId, BattleSide side, int maxHp)
        {
            InitializeIdentity(instanceId, configId, side);
            MaxHp = CurrentHp = maxHp;
        }

        internal DamageResult ApplyDamage(int damage)
        {
            if (!IsAlive || damage <= 0 || _notifying) return DamageResult.Ignored;
            var previous = CurrentHp;
            CurrentHp = Math.Max(0, CurrentHp - damage);
            var snapshot = new TowerHealthSnapshot(this, previous);
            var killed = CurrentHp == 0;
            // 通知前捕获快照与订阅；监听方移除实体也不会破坏本次通知。
            var healthChanged = HealthChanged;
            var died = Died;
            _notifying = true;
            try
            {
                // 致命伤先广播死亡，让对局锁定终局后再通知表现，阻止回调再伤害另一座塔。
                if (killed) TowerNotifications.Publish(died, snapshot);
                TowerNotifications.Publish(healthChanged, snapshot);
            }
            finally { _notifying = false; }
            return killed ? DamageResult.Killed : DamageResult.Damaged;
        }

        protected override void OnRemoved()
        {
            HealthChanged = null;
            Died = null;
        }
    }
}
