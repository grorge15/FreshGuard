#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class BattleScreenPlaybackTests
    {
        [UnityTest]
        public IEnumerator RealBattleFitsThreeResolutionsAndCancelsReentrantDragSafely()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host == null || ConfigSystem.Instance.Tables == null || UIModule.UIRoot == null)
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                var deadline = Time.realtimeSinceStartup + 60;
                while ((UIModule.UIRoot == null || GameModule.UI.GetUI<MainMenuUI>() == null) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsNotNull(GameModule.UI.GetUI<MainMenuUI>(), "Launcher 未完成。");
                yield return GameModule.Scene.LoadSceneAsync("Main").ToCoroutine();
                host = Object.FindObjectOfType<BattleShopSceneController>();
            }
            Assert.IsNotNull(host);
            GameModule.UI.CloseUI<MainMenuUI>();
            host.EndBattle();
            var camera = host.Placement.SceneCamera;
            var original = camera.transform.position;
            var originalSize = camera.orthographicSize;
            var resolutions = new[] { new Vector2Int(1080, 1920), new Vector2Int(1080, 2380), new Vector2Int(540, 1190) };
            using (var gameView = new GameViewResolutionScope())
            {
                yield return host.InitializeAsync().ToCoroutine();
                Assert.IsTrue(host.IsReady, host.InitializationError?.ToString());
                var deadline = Time.realtimeSinceStartup + 10;
                while (!host.CanInteractWithRobots && Time.realtimeSinceStartup < deadline)
                {
                    host.TryOpenRobotInteraction(host.Context.BattleId);
                    yield return null;
                }
                Assert.IsTrue(host.CanInteractWithRobots, "真实战斗开始后应开放交互。");
                host.SetFrozen(true);
                var layout = host.GetComponent<BattleScreenLayoutController>();
                var boards = Object.FindObjectsOfType<PlacementBoardView>();
                var positions = Array.ConvertAll(boards, board => board.transform.position);
                var scales = Array.ConvertAll(boards, board => board.transform.localScale);
                var reports = new List<string>();
                Vector3 longPosition = default;
                float longSize = 0;
                for (var index = 0; index < resolutions.Length; index++)
                {
                    var resolution = resolutions[index];
                    gameView.Select(resolution.x, resolution.y);
                    deadline = Time.realtimeSinceStartup + 10;
                    while ((Screen.width != resolution.x || Screen.height != resolution.y) && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.AreEqual(resolution.x, Screen.width);
                    Assert.AreEqual(resolution.y, Screen.height);
                    yield return null;
                    yield return null;
                    AssertProjected(camera, layout.PlayerBounds, layout.AvailableScreenRect);
                    AssertProjected(camera, layout.OpponentBounds, layout.AvailableScreenRect);
                    Assert.That(layout.AvailableScreenRect.yMin, Is.GreaterThan(host.CombatWindow.GetShopScreenRect().yMax));
                    var canvas = host.CombatWindow.Canvas.rootCanvas;
                    var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    var corners = new Vector3[4];
                    host.CombatWindow.Info.rectTransform.GetWorldCorners(corners);
                    var infoBottom = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]).y;
                    var infoTop = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]).y;
                    Assert.That(infoBottom, Is.GreaterThan(camera.WorldToScreenPoint(layout.PlayerBounds.max).y));
                    Assert.That(infoTop, Is.LessThan(camera.WorldToScreenPoint(layout.OpponentBounds.min).y));
                    for (var b = 0; b < boards.Length; b++)
                    {
                        Assert.AreEqual(positions[b], boards[b].transform.position);
                        Assert.AreEqual(scales[b], boards[b].transform.localScale);
                        var cell = new BoardCoordinate(4, 3);
                        PlacementBoardView hitBoard;
                        BoardCoordinate hit;
                        Assert.IsTrue(host.Placement.TryGetPointerCell(camera.WorldToScreenPoint(boards[b].GetCellWorldPosition(cell)), out hitBoard, out hit));
                        Assert.AreSame(boards[b], hitBoard);
                        Assert.AreEqual(cell, hit);
                    }
                    var adaptedPosition = camera.transform.position;
                    var adaptedSize = camera.orthographicSize;
                    Assert.IsTrue(layout.RefreshLayout(), "稳定分辨率下应能重复刷新布局。");
                    Assert.AreEqual(adaptedPosition, camera.transform.position);
                    Assert.AreEqual(adaptedSize, camera.orthographicSize);
                    if (index == 1) { longPosition = adaptedPosition; longSize = adaptedSize; }
                    if (index == 2)
                    {
                        Assert.That(Vector3.Distance(longPosition, adaptedPosition), Is.LessThan(0.0001f));
                        Assert.That(adaptedSize, Is.EqualTo(longSize).Within(0.0001f));
                    }
                    var screenshot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BattleScreen-" + resolution.x + "x" + resolution.y + ".png"));
                    ScreenCapture.CaptureScreenshot(screenshot);
                    for (var frame = 0; frame < 4; frame++) yield return null;
                    reports.Add(resolution + ": size=" + adaptedSize + ", camera=" + adaptedPosition + ", available=" + layout.AvailableScreenRect);
                }
                // 运行中的屏幕变化必须撤销真实商店手势与事务。
                host.SetFrozen(false);
                var state = host.Context.GetState(BattleSide.Player);
                var source = RobotDragSource.Offer(state.Slots[0].SlotId, state.Slots[0].Offer.OfferId);
                Assert.IsTrue(host.ShopWindow.Drag.Press(source, -1, Vector2.zero, Time.unscaledTime, new BoardCoordinate(0, 0)), "调整前应能预留商店手势。");
                gameView.Select(1080, 1920);
                deadline = Time.realtimeSinceStartup + 10;
                while ((Screen.width != 1080 || host.ShopWindow.Drag.Phase != RobotDragPhase.Idle || !host.CanInteractWithRobots) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(RobotDragPhase.Idle, host.ShopWindow.Drag.Phase);
                Assert.IsFalse(host.Context.IsBusy);
                // GameView 的尺寸与 CanvasScaler 可在相邻帧更新；比较稳定后的相同输入。
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.IsTrue(layout.RefreshLayout());
                var restartPosition = camera.transform.position;
                var restartSize = camera.orthographicSize;
                Assert.IsTrue(host.ShopWindow.Drag.Press(source, -1, Vector2.zero, Time.unscaledTime, new BoardCoordinate(0, 0)), "调整完成后应能再次预留商店手势。");
                Action<int> endOnCancel = side => { if (host.IsReady && !host.Context.IsBusy) host.EndBattle(); };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, endOnCancel);
                try { Assert.IsFalse(layout.RefreshLayout(), "取消通知中终局后，旧刷新必须停止。"); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, endOnCancel); }
                Assert.IsFalse(layout.IsBound);
                Assert.AreEqual(original, camera.transform.position);
                Assert.AreEqual(originalSize, camera.orthographicSize);
                yield return host.InitializeAsync().ToCoroutine();
                Assert.IsTrue(host.IsReady, host.InitializationError?.ToString());
                Assert.AreEqual(restartPosition, camera.transform.position, "相同输入重新开局应得到相同相机位置。");
                Assert.AreEqual(restartSize, camera.orthographicSize, "相同输入重新开局应得到相同缩放。");
                host.EndBattle();
                Assert.AreEqual(original, camera.transform.position);
                Assert.AreEqual(originalSize, camera.orthographicSize);
                reports.Add("尺寸变化取消预留手势、取消通知内终局、相同输入重开和二次退场恢复：通过。");
                File.WriteAllLines(Path.Combine(Application.dataPath, "../Library/BattleScreenPlaybackVerification.txt"), reports);
            }
        }

        private static void AssertProjected(Camera camera, Bounds bounds, Rect area)
        {
            for (var i = 0; i < 4; i++)
            {
                var screen = camera.WorldToScreenPoint(new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y, 0));
                Assert.That(screen.x, Is.InRange(area.xMin - 0.05f, area.xMax + 0.05f));
                Assert.That(screen.y, Is.InRange(area.yMin - 0.05f, area.yMax + 0.05f));
            }
        }

        /// <summary>只在测试期间添加固定像素预设，退出时恢复原选项并移除所加预设。</summary>
        private sealed class GameViewResolutionScope : IDisposable
        {
            private readonly Type _viewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            private readonly EditorWindow _view;
            private readonly object _group;
            private readonly int _originalIndex;
            private readonly List<object> _added = new List<object>();
            private const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            public GameViewResolutionScope()
            {
                _view = EditorWindow.GetWindow(_viewType);
                _originalIndex = (int)_viewType.GetProperty("selectedSizeIndex", FLAGS).GetValue(_view);
                var sizesType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes");
                var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
                _group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { _viewType.GetProperty("currentSizeGroupType", FLAGS).GetValue(null) });
            }
            public void Select(int width, int height)
            {
                var type = _group.GetType();
                var count = (int)type.GetMethod("GetTotalCount").Invoke(_group, null);
                var sizeType = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSize");
                var index = -1;
                for (var i = 0; i < count; i++)
                {
                    var size = type.GetMethod("GetGameViewSize").Invoke(_group, new object[] { i });
                    if ((int)sizeType.GetProperty("width").GetValue(size) == width && (int)sizeType.GetProperty("height").GetValue(size) == height)
                    { index = i; break; }
                }
                if (index < 0)
                {
                    var kind = typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
                    var size = Activator.CreateInstance(sizeType, new[] { Enum.Parse(kind, "FixedResolution"), width, height, (object)"FreshGuard屏幕验收" });
                    type.GetMethod("AddCustomSize").Invoke(_group, new[] { size });
                    _added.Add(size);
                    index = count;
                }
                _viewType.GetProperty("selectedSizeIndex", FLAGS).SetValue(_view, index);
                _view.Repaint();
            }
            public void Dispose()
            {
                _viewType.GetProperty("selectedSizeIndex", FLAGS).SetValue(_view, _originalIndex);
                var type = _group.GetType();
                var builtins = (int)type.GetMethod("GetBuiltinCount").Invoke(_group, null);
                for (var i = (int)type.GetMethod("GetTotalCount").Invoke(_group, null) - 1; i >= builtins; i--)
                {
                    var size = type.GetMethod("GetGameViewSize").Invoke(_group, new object[] { i });
                    if (_added.Contains(size)) type.GetMethod("RemoveCustomSize").Invoke(_group, new object[] { i });
                }
                _view.Repaint();
            }
        }
    }
}
#endif
