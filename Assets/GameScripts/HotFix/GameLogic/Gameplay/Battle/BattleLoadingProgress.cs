namespace GameLogic
{
    /// <summary>进入战斗前的加载阶段。</summary>
    public enum BattleLoadingStage
    {
        Scene = 0,
        Config = 1,
        Glass = 2,
        Enemy = 3,
        CombatPresentation = 4,
        CombatUi = 5,
        Ai = 6,
        Ready = 7,
        CombatStarted = 8,
        Failed = 9,
    }

    /// <summary>战斗加载条使用的不可变进度快照。</summary>
    public readonly struct BattleLoadingProgress
    {
        public BattleLoadingStage Stage { get; }
        public float Progress { get; }
        public string Message { get; }
        public double ElapsedSeconds { get; }

        public BattleLoadingProgress(BattleLoadingStage stage, float progress, string message,
            double elapsedSeconds = 0d)
        {
            Stage = stage;
            Progress = progress < 0f ? 0f : progress > 1f ? 1f : progress;
            Message = message ?? string.Empty;
            ElapsedSeconds = elapsedSeconds < 0d ? 0d : elapsedSeconds;
        }
    }
}
