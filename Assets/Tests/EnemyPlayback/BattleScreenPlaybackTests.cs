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
        public IEnumerator RealBattleFitsSplitViewsAndCancelsReentrantDragSafely()
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
            var originalRect = camera.rect;
            var originalColor = camera.backgroundColor;
            var originalDepth = camera.depth;
            var originalMask = camera.cullingMask;
            var resolutions = new[] { new Vector2Int(1080, 1920), new Vector2Int(1080, 2160),
                new Vector2Int(1080, 2380), new Vector2Int(540, 1190), new Vector2Int(1080, 3000) };
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
                Vector2 playerReference = default;
                Vector2 longPlayer = default, longOpponent = default;
                Vector2 previousOpponent = default;
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
                    Assert.That(host.CombatWindow.Info.rectTransform.localScale.x, Is.EqualTo(1f).Within(0.00001f),
                        "宽度充足时信息栏应保持Prefab的1倍缩放。");
                    AssertProjected(camera, layout.PlayerBounds, layout.PlayerScreenRect);
                    AssertProjected(layout.OpponentCamera, layout.OpponentBounds, layout.OpponentScreenRect);
                    var playerPixels = layout.Layout.PlayerTarget.size;
                    var opponentPixels = layout.Layout.OpponentTarget.size;
                    if (index == 0) playerReference = playerPixels;
                    if (resolution.x == 1080)
                    {
                        Assert.That(Vector2.Distance(playerReference, playerPixels), Is.LessThan(0.02f), "同宽长屏我方显示大小不变。");
                        Assert.That(opponentPixels.x + 0.02f, Is.GreaterThanOrEqualTo(previousOpponent.x));
                        Assert.That(opponentPixels.y + 0.02f, Is.GreaterThanOrEqualTo(previousOpponent.y));
                        previousOpponent = opponentPixels;
                    }
                    Assert.That(opponentPixels.x, Is.LessThanOrEqualTo(playerPixels.x + 0.02f));
                    Assert.That(opponentPixels.y, Is.LessThanOrEqualTo(playerPixels.y + 0.02f));
                    Assert.That(layout.Layout.PlayerTarget.yMin - host.CombatWindow.GetShopScreenRect().yMax,
                        Is.EqualTo(16 * host.CombatWindow.ScreenLayoutScale).Within(0.05f));
                    if (index == 2) { longPlayer = playerPixels; longOpponent = opponentPixels; }
                    if (index == 3)
                    {
                        Assert.That(Vector2.Distance(longPlayer * 0.5f, playerPixels), Is.LessThan(0.02f));
                        Assert.That(Vector2.Distance(longOpponent * 0.5f, opponentPixels), Is.LessThan(0.02f));
                    }
                    if (index == 4)
                    {
                        Assert.That(Mathf.Min(playerPixels.x / opponentPixels.x, playerPixels.y / opponentPixels.y), Is.EqualTo(1).Within(0.001f));
                        Assert.That(layout.Layout.OpponentTarget.yMax, Is.LessThan(layout.OpponentScreenRect.yMax - 1));
                    }
                    var canvas = host.CombatWindow.Canvas.rootCanvas;
                    var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    var corners = new Vector3[4];
                    host.CombatWindow.Info.rectTransform.GetWorldCorners(corners);
                    var infoBottom = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]).y;
                    var infoTop = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]).y;
                    Assert.That(infoBottom, Is.GreaterThan(camera.WorldToScreenPoint(layout.PlayerBounds.max).y));
                    Assert.That(infoTop, Is.LessThan(layout.OpponentCamera.WorldToScreenPoint(layout.OpponentBounds.min).y));
                    for (var b = 0; b < boards.Length; b++)
                    {
                        Assert.AreEqual(positions[b], boards[b].transform.position);
                        Assert.AreEqual(scales[b], boards[b].transform.localScale);
                        var cell = new BoardCoordinate(4, 3);
                        PlacementBoardView hitBoard;
                        BoardCoordinate hit;
                        var boardCamera = host.Placement.GetSceneCamera(host.Placement.GetBoardSide(boards[b]));
                        Assert.IsTrue(host.Placement.TryGetPointerCell(boardCamera.WorldToScreenPoint(boards[b].GetCellWorldPosition(cell)), out hitBoard, out hit));
                        Assert.AreSame(boards[b], hitBoard);
                        Assert.AreEqual(cell, hit);
                    }
                    var adaptedPosition = camera.transform.position;
                    var adaptedSize = camera.orthographicSize;
                    Assert.IsTrue(layout.RefreshLayout(), "稳定分辨率下应能重复刷新布局。");
                    Assert.AreEqual(adaptedPosition, camera.transform.position, "重复刷新相机位置发生变化：" + resolution);
                    Assert.AreEqual(adaptedSize, camera.orthographicSize, "重复刷新相机尺寸发生变化：" + resolution);
                    var screenshot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BattleScreenV2-" + resolution.x + "x" + resolution.y + ".png"));
                    host.CombatWindow.Notices.Clear();
                    ScreenCapture.CaptureScreenshot(screenshot);
                    for (var frame = 0; frame < 4; frame++) yield return null;
                    host.CombatWindow.CurrencyFlight.Clear();
                    var playerBoard = Array.Find(boards, board => host.Placement.GetBoardSide(board) == BattleSide.Player);
                    var worldOrigin = playerBoard.GetCellWorldPosition(new BoardCoordinate(4, 3));
                    host.CombatWindow.CurrencyFlight.Show(worldOrigin, 1);
                    var coin = host.CombatWindow.CurrencyFlight.transform.Find("FlyingEnergyCoin");
                    Assert.IsNotNull(coin);
                    Assert.That(Vector2.Distance(camera.WorldToScreenPoint(worldOrigin),
                        RectTransformUtility.WorldToScreenPoint(uiCamera, coin.position)), Is.LessThan(0.15f), "飞币应从我方相机投影位置出现。");
                    host.CombatWindow.CurrencyFlight.Clear();
                    reports.Add(resolution + ": player=" + layout.Layout.PlayerTarget + ", opponent=" + layout.Layout.OpponentTarget + ", info=" + layout.Layout.InfoTarget);
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
                var restartData = DescribeLayout(layout, host.CombatWindow, camera);
                Assert.IsTrue(host.ShopWindow.Drag.Press(source, -1, Vector2.zero, Time.unscaledTime, new BoardCoordinate(0, 0)), "调整完成后应能再次预留商店手势。");
                Action<int> endOnCancel = side => { if (host.IsReady && !host.Context.IsBusy) host.EndBattle(); };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, endOnCancel);
                try { Assert.IsFalse(layout.RefreshLayout(), "取消通知中终局后，旧刷新必须停止。"); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, endOnCancel); }
                Assert.IsFalse(layout.IsBound);
                Assert.AreEqual(original, camera.transform.position);
                Assert.AreEqual(originalSize, camera.orthographicSize);
                Assert.AreEqual(originalRect, camera.rect);
                Assert.AreEqual(originalColor, camera.backgroundColor);
                Assert.AreEqual(originalDepth, camera.depth);
                Assert.AreEqual(originalMask, camera.cullingMask);
                Assert.IsNull(layout.OpponentCamera);
                yield return host.InitializeAsync().ToCoroutine();
                Assert.IsTrue(host.IsReady, host.InitializationError?.ToString());
                File.WriteAllText(Path.Combine(Application.dataPath, "../Library/BattleScreenV2Restart.txt"),
                    restartData + "\nAfter restart:\n" + DescribeLayout(layout, host.CombatWindow, camera));
                // 地图还原/重新应用会使Bounds产生约2e-7世界单位的浮点差，按小于0.001像素比较重开结果。
                Assert.That(Vector3.Distance(restartPosition, camera.transform.position) * layout.Layout.PlayerPixelsPerUnit,
                    Is.LessThan(0.001f), "重新开局相机位置应在亚像素精度内一致。");
                Assert.That(camera.orthographicSize, Is.EqualTo(restartSize).Within(0.000001f), "重新开局缩放应一致。");
                host.EndBattle();
                Assert.AreEqual(original, camera.transform.position);
                Assert.AreEqual(originalSize, camera.orthographicSize);
                reports.Add("尺寸变化取消预留手势、取消通知内终局、相同输入重开和二次退场恢复：通过。");
                File.WriteAllLines(Path.Combine(Application.dataPath, "../Library/BattleScreenV2PlaybackVerification.txt"), reports);
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

        private static string DescribeLayout(BattleScreenLayoutController layout, MainCombatUI window, Camera camera)
        {
            return "camera=" + camera.transform.position.ToString("R") + ", size=" + camera.orthographicSize.ToString("R") +
                ", playerBounds=" + layout.PlayerBounds.ToString("R") + ", opponentBounds=" + layout.OpponentBounds.ToString("R") +
                ", playerTarget=" + layout.Layout.PlayerTarget.ToString("R") + ", ppu=" + layout.Layout.PlayerPixelsPerUnit.ToString("R") +
                ", shopDesign=" + window.ShopDesignSize.ToString("R") + ", shop=" + window.GetShopScreenRect().ToString("R") +
                ", scale=" + window.ScreenLayoutScale.ToString("R");
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
