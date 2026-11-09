using System;

namespace GameLogic
{
    public enum BuffApplyMode { Reject, Add, Refresh, Replace }

    /// <summary>行为只决定应用规则，容器负责原子提交及生命周期通知。</summary>
    public readonly struct BuffApplyDecision
    {
        public BuffApplyMode Mode { get; }
        public BuffObj Existing { get; }
        private BuffApplyDecision(BuffApplyMode mode, BuffObj existing = null)
        { Mode = mode; Existing = existing; }
        public static BuffApplyDecision Add => new BuffApplyDecision(BuffApplyMode.Add);
        public static BuffApplyDecision Reject => default(BuffApplyDecision);
        public static BuffApplyDecision Refresh(BuffObj existing) =>
            new BuffApplyDecision(BuffApplyMode.Refresh, existing ?? throw new ArgumentNullException(nameof(existing)));
        public static BuffApplyDecision Replace(BuffObj existing) =>
            new BuffApplyDecision(BuffApplyMode.Replace, existing ?? throw new ArgumentNullException(nameof(existing)));
    }
}
