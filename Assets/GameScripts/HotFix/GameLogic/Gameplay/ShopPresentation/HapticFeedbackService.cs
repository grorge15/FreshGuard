using UnityEngine;

namespace GameLogic
{
    /// <summary>Provides a short mobile haptic pulse. Unsupported platforms silently skip it.</summary>
    public static class HapticFeedbackService
    {
        private const string EnabledKey = "HapticFeedbackEnabled";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void PlayMergePulse()
        {
            if (!Enabled || !Application.isMobilePlatform || !SystemInfo.supportsVibration) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") >= 26)
                    {
                        using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", 35L, 90))
                            vibrator.Call("vibrate", effect);
                    }
                    else vibrator.Call("vibrate", 35L);
                }
                return;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Merge haptic failed; using the system vibration fallback: " + exception.Message);
            }
#endif
            Handheld.Vibrate();
        }
    }
}
