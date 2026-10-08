using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TEngine.Editor
{
    public static class LubanTools
    {
        private static bool _running;

        [MenuItem("TEngine/Luban/转表 &X", priority = -100)]
        private static async void ZhuanXiaoYi()
        {
            if (_running) return;
            _running = true;
            // Keep generated scripts from reloading this callback before it refreshes assets.
            EditorApplication.LockReloadAssemblies();
            try
            {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                string path = Application.dataPath + "/../../Configs/GameConfig/gen_code_bin_to_project_lazyload.sh";
                var start = new ProcessStartInfo("/bin/bash", "\"" + path + "\"");
#elif UNITY_EDITOR_WIN
                string path = Application.dataPath + "/../../Configs/GameConfig/gen_code_bin_to_project_lazyload.bat";
                var start = new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + path + "\"\"");
#endif
                Debug.Log($"执行转表：{path}");
                start.WorkingDirectory = Path.GetDirectoryName(path);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = Encoding.UTF8;
                start.StandardErrorEncoding = Encoding.UTF8;
                start.EnvironmentVariables["AI_MODE"] = "1";
                using (var process = new Process { StartInfo = start })
                {
                    process.Start();
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    string stdout = await output;
                    string stderr = await errors;
                    if (process.ExitCode != 0)
                    {
                        Debug.LogError($"Luban export failed ({process.ExitCode}).\n{stdout}\n{stderr}");
                        return;
                    }
                    if (!string.IsNullOrWhiteSpace(stderr)) Debug.LogWarning(stderr);
                    Debug.Log($"Luban export succeeded.\n{stdout}");
                }
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                _running = false;
            }
        }

        [MenuItem("TEngine/Luban/转表 &X", true)]
        private static bool CanExport() => !_running && !EditorApplication.isCompiling &&
                                            !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
