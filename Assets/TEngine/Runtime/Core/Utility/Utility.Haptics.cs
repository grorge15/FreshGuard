using UnityEngine;

namespace TEngine
{
    public static partial class Utility
    {
        /// <summary>原生触觉调用保留在 AOT 程序集中，供热更层调用。</summary>
        public static class Haptics
        {
            [UnityEngine.Scripting.Preserve]
            public static void PlayLightImpact(float intensity)
            {
#if UNITY_IOS && !UNITY_EDITOR
                FreshGuardPlayLightImpact(Mathf.Clamp01(intensity));
#endif
            }

#if UNITY_IOS && !UNITY_EDITOR
            [System.Runtime.InteropServices.DllImport("__Internal")]
            private static extern void FreshGuardPlayLightImpact(float intensity);
#endif
        }
    }
}
