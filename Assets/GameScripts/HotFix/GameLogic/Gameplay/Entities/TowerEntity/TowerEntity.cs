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
            var notification = CommitDamage(damage);
            PublishDamage(notification);
            return notification.Result;
        }

        // 批次先提交双方生命，再发送任何用户回调，避免遍历顺序决定胜负。
        internal TowerDamageNotification CommitDamage(int damage)
        {
            if (!IsAlive || damage <= 0 || _notifying) return default(TowerDamageNotification);
            var previous = CurrentHp;
            CurrentHp = Math.Max(0, CurrentHp - damage);
            _notifying = true;
            return new TowerDamageNotification(new TowerHealthSnapshot(this, previous), HealthChanged, Died);
        }

        internal void PublishDamage(TowerDamageNotification notification)
        {
            if (notification.Result == DamageResult.Ignored) return;
            try
            {
                // 致命伤先广播死亡，让对局锁定终局后再通知表现，阻止回调再伤害另一座塔。
                if (notification.Result == DamageResult.Killed)
                    TowerNotifications.Publish(notification.Died, notification.Snapshot);
                TowerNotifications.Publish(notification.HealthChanged, notification.Snapshot);
            }
            finally { _notifying = false; }
        }

        protected override void OnRemoved()
        {
            HealthChanged = null;
            Died = null;
        }
    }

    internal readonly struct TowerDamageNotification
    {
        internal readonly TowerHealthSnapshot Snapshot;
        internal readonly Action<TowerHealthSnapshot> HealthChanged;
        internal readonly Action<TowerHealthSnapshot> Died;
        internal readonly DamageResult Result;

        internal TowerDamageNotification(TowerHealthSnapshot snapshot, Action<TowerHealthSnapshot> healthChanged,
            Action<TowerHealthSnapshot> died)
        {
            Snapshot = snapshot;
            HealthChanged = healthChanged;
            Died = died;
            Result = snapshot.CurrentHp == 0 ? DamageResult.Killed : DamageResult.Damaged;
        }
    }
}
