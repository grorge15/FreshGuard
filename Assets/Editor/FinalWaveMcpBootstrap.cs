using UnityEditor;

// Temporary editor-only bridge startup for this verification session.
internal static class FinalWaveMcpBootstrap
{
    [InitializeOnLoadMethod]
    private static void StartBridge()
    {
        EditorApplication.delayCall += () =>
            MCPForUnity.Editor.Services.Transport.Transports.StdioBridgeHost.StartAutoConnect();
    }
}
