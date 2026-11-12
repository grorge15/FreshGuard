using UnityEngine;

namespace GameLogic
{
    /// <summary>手机触觉反馈共用设置开关；不支持的平台跳过。</summary>
    public static class HapticFeedbackService
    {
        private const string EnabledKey = "HapticFeedbackEnabled";
        private static SlotRefreshHaptic _slotRefresh;

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) _slotRefresh?.StopPulse();
            }
        }

        public static void PlayPurchasePulse() => PlayLightPulse(18, 48, 0.36f);

        public static void PlayBerserkStartPulse() => PlayLightPulse(25, 60, 0.48f);

        public static void PlayMergePulse() => PlayLightPulse(35, 90, 0.55f);

        public static SlotRefreshHaptic BeginSlotRefresh()
        {
            _slotRefresh?.Dispose();
            _slotRefresh = new SlotRefreshHaptic();
            return _slotRefresh;
        }

        /// <summary>由转轮动画逐帧推进；短震重叠形成轻微持续感，释放后立即停止。</summary>
        public sealed class SlotRefreshHaptic : System.IDisposable
        {
            private float _nextPulseAt;
            private bool _ownsPulse;
            private bool _disposed;

            public void Update()
            {
                if (_disposed) return;
                if (!Enabled) { StopPulse(); return; }
                var now = Time.unscaledTime;
                if (now < _nextPulseAt) return;
                _nextPulseAt = now + 0.08f;
                PlayLightPulse(100, 18, 0.12f, priority: false);
                _ownsPulse = true;
            }

            internal void PauseForPulse(int milliseconds)
            {
                // 暴走等单次反馈接管马达；刷新不能覆盖它，也不能在取消时截断它。
                _ownsPulse = false;
                _nextPulseAt = Time.unscaledTime + milliseconds / 1000f;
            }

            internal void StopPulse()
            {
                if (!_ownsPulse) return;
                _ownsPulse = false;
                StopRefreshPulse();
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                StopPulse();
                if (ReferenceEquals(_slotRefresh, this)) _slotRefresh = null;
            }
        }

#if UNITY_EDITOR
        // 编辑器验证真实购买、合并与暴走事件，不调用设备马达。
        internal static System.Action<int, int> LightPulseOverride { get; set; }
        internal static System.Action RefreshStopOverride { get; set; }
#endif

        private static void PlayLightPulse(int milliseconds, int amplitude, float intensity, bool priority = true)
        {
            if (!Enabled) return;
            if (priority) _slotRefresh?.PauseForPulse(milliseconds);
#if UNITY_EDITOR
            LightPulseOverride?.Invoke(milliseconds, amplitude);
#elif UNITY_ANDROID
            if (!SystemInfo.supportsVibration) return;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") >= 26 && vibrator != null &&
                        vibrator.Call<bool>("hasAmplitudeControl"))
                    {
                        using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", (long)milliseconds, amplitude))
                            vibrator.Call("vibrate", effect);
                    }
                    else
                    {
                        // 不支持幅度控制的机型使用系统轻触反馈，避免退化成长震。
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                        using (var view = window.Call<AndroidJavaObject>("getDecorView"))
                            view.Call<bool>("performHapticFeedback", 4); // HapticFeedbackConstants.CLOCK_TICK
                    }
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("轻震反馈未能播放：" + exception.Message);
            }
#elif UNITY_IOS
            if (SystemInfo.supportsVibration) TEngine.Utility.Haptics.PlayLightImpact(intensity);
#endif
        }

        private static void StopRefreshPulse()
        {
#if UNITY_EDITOR
            RefreshStopOverride?.Invoke();
#elif UNITY_ANDROID
            if (!SystemInfo.supportsVibration) return;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                    vibrator?.Call("cancel");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("停止刷新轻震时发生异常：" + exception.Message);
            }
#endif
        }

    }
}
