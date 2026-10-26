using System;
using System.Collections.Generic;

namespace GameLogic
{
    public sealed class BuffBehaviorRegistry
    {
        private readonly Dictionary<string, Func<IBuffBehavior>> _factories = new Dictionary<string, Func<IBuffBehavior>>();
        public BuffBehaviorRegistry() { Register("slow", () => new SlowBuffBehavior()); Register("noop", () => new BuffBehavior()); }
        public void Register(string key, Func<IBuffBehavior> factory) => _factories.Add(key, factory ?? throw new ArgumentNullException(nameof(factory)));
        public IBuffBehavior Create(string key)
        {
            if (!_factories.TryGetValue(key, out var factory)) throw new InvalidOperationException("未注册Buff行为：" + key);
            return factory() ?? throw new InvalidOperationException("Buff行为工厂返回空：" + key);
        }
    }
    /// <summary>减速行为由EnemyEntity读取有效值，不修改基础速度，移除后自然恢复。</summary>
    public sealed class SlowBuffBehavior : BuffBehavior { }
}
