using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>只启动已有MCP本地桥接；不改场景、Prefab或用户的传输偏好。</summary>
    [InitializeOnLoad]
    public static class MapEditorVerification
    {
        private const string SESSION_KEY = "FreshGuard.MapVerification.LocalBridge";
        private static double _nextAttempt;
        private static string _lastFailure;
        static MapEditorVerification()
        {
            // 本地自动验证可在Library写入一次性请求；不改变MCP全局传输偏好。
            var request = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/FreshGuard.StartLocalVerificationBridge"));
            var requested = File.Exists(request);
            if (requested)
            {
                SessionState.SetBool(SESSION_KEY, true);
                File.Delete(request);
            }
            if (SessionState.GetBool(SESSION_KEY, false)) RegisterRecovery();
        }

        [MenuItem("FreshGuard/Map/Start Local Verification Bridge")]
        public static void StartBridge()
        {
            SessionState.SetBool(SESSION_KEY, true);
            RegisterRecovery();
            _nextAttempt = 0;
            MaintainBridge();
            var bridge = FindType("MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost");
            var running = bridge != null && (bool)bridge.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var message = running
                ? "MCP本地验证桥已运行，端口：" + bridge.GetMethod("GetCurrentPort", BindingFlags.Public | BindingFlags.Static).Invoke(null, null)
                : "MCP本地验证桥启动请求已登记，编辑器空闲后自动重试；详情见Console。";
            Debug.Log("[MapEditor] " + message);
            if (EditorWindow.focusedWindow != null) EditorWindow.focusedWindow.ShowNotification(new GUIContent(message));
        }

        private static void RegisterRecovery()
        {
            EditorApplication.update -= MaintainBridge;
            EditorApplication.update += MaintainBridge;
        }

        private static void MaintainBridge()
        {
            if (!SessionState.GetBool(SESSION_KEY, false))
            { EditorApplication.update -= MaintainBridge; return; }
            if (EditorApplication.timeSinceStartup < _nextAttempt) return;
            _nextAttempt = EditorApplication.timeSinceStartup + 1;
            try
            {
                var bridge = FindType("MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost");
                if (bridge == null) throw new InvalidOperationException("未安装MCP For Unity。");
                if ((bool)bridge.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static).GetValue(null)) return;
                // 包内检查会排除延迟重载时原始isCompiling标记的假阳性。
                var actualCompile = FindType("MCPForUnity.Editor.Services.EditorStateCache")?.GetMethod(
                    "GetActualIsCompiling", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                var compiling = actualCompile != null ? (bool)actualCompile.Invoke(null, null) : EditorApplication.isCompiling;
                if (compiling || EditorApplication.isUpdating) return;
                bridge.GetMethod("StartAutoConnect", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                if ((bool)bridge.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static).GetValue(null))
                { _lastFailure = null; Debug.Log("[MapEditor] Local verification bridge started."); }
            }
            catch (Exception exception)
            {
                if (_lastFailure != exception.Message) Debug.LogWarning("[MapEditor] Local bridge retry: " + exception.Message);
                _lastFailure = exception.Message;
            }
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false)).FirstOrDefault(type => type != null);
    }
}
