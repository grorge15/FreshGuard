using System;

namespace GameLogic
{
    /// <summary>单次选择状态；与 Unity 时间、配置表和强化效果均无依赖。</summary>
    public sealed class BuffSelectSession
    {
        public bool IsActive { get; private set; } = true;
        public float RemainingSeconds { get; private set; }
        private readonly BuffSelectRequest _request;
        public BuffSelectSession(BuffSelectRequest request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            RemainingSeconds = request.DurationSeconds;
        }
        public bool TrySelect(int index, out BuffSelectResult result) => Complete(index, false, out result);
        public bool Advance(float deltaSeconds, out BuffSelectResult result)
        {
            result = null;
            if (!IsActive) return false;
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            RemainingSeconds = Math.Max(0, RemainingSeconds - deltaSeconds);
            return RemainingSeconds <= 0 && Complete(0, true, out result);
        }
        private bool Complete(int index, bool automatic, out BuffSelectResult result)
        {
            result = null;
            if (!IsActive || index < 0 || index >= _request.Options.Count) return false;
            // 先结束会话，再向外返回结果，避免点击和超时同帧重复完成。
            IsActive = false;
            result = new BuffSelectResult(index, _request.Options[index], automatic);
            return true;
        }
        public void Cancel() { IsActive = false; }
    }
}
