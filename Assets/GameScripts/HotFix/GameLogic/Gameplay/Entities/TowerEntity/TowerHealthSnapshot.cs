using System;

namespace GameLogic
{
    public readonly struct TowerHealthSnapshot
    {
        public int InstanceId { get; }
        public BattleSide Side { get; }
        public int PreviousHp { get; }
        public int CurrentHp { get; }
        public int MaxHp { get; }

        internal TowerHealthSnapshot(TowerEntity tower, int previousHp)
        {
            InstanceId = tower.InstanceId;
            Side = tower.Side;
            PreviousHp = previousHp;
            CurrentHp = tower.CurrentHp;
            MaxHp = tower.MaxHp;
        }
    }

    internal static class TowerNotifications
    {
        // 一个表现监听者异常不能阻断死亡事实或其他订阅者。
        internal static void Publish<T>(Action<T> handlers, T snapshot)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(snapshot); }
                catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            }
        }
    }
}
