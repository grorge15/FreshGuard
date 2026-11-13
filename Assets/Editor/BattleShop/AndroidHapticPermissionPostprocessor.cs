#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace FreshGuard.Editor
{
    /// <summary>震动通过 Android 原生接口调用，显式声明权限，避免依赖 Unity 的 API 扫描。</summary>
    public sealed class AndroidHapticPermissionPostprocessor : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        private const string VibrationPermission = "android.permission.VIBRATE";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            var manifest = document.DocumentElement;
            if (manifest == null) throw new InvalidDataException("Android manifest has no root element.");
            foreach (XmlNode node in manifest.ChildNodes)
            {
                var permission = node as XmlElement;
                if (permission != null && permission.Name == "uses-permission" &&
                    permission.GetAttribute("name", AndroidNamespace) == VibrationPermission) return;
            }
            var vibration = document.CreateElement("uses-permission");
            vibration.SetAttribute("name", AndroidNamespace, VibrationPermission);
            manifest.AppendChild(vibration);
            document.Save(manifestPath);
        }
    }
}
#endif
