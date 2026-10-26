using UnityEngine;

namespace GameLogic
{
    /// <summary>设备暂停也可能发生在焦点仍为true时；共同控制前台匹配时间。</summary>
    public sealed class MatchApplicationState : MonoBehaviour
    {
        private static bool _paused;
        public static bool IsForeground => Application.isFocused && !_paused;
        private void OnApplicationPause(bool paused) { _paused = paused; }
        private void OnDestroy() { _paused = false; }
    }
}
