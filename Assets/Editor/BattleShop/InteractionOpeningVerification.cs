using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>从TEngine Launcher观察加载阶段，再验证双方开放时机；不保存场景或配置。</summary>
    public static class InteractionOpeningVerification
    {
        private const string Pending = "FreshGuard.InteractionOpening.LaunchPending";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool _running;
        [Serializable]
        private sealed class Report { public bool passed; public string details; public string[] checks; public int coveredFrames; }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("FreshGuard/Battle/Verify Interaction Opening via TEngine Launcher")]
        public static void RunViaLauncher()
        {
            Require(!EditorApplication.isPlaying && !EditorApplication.isCompiling, "请先停止运行并等待编译。");
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty, "场景有未保存修改，不能切换Launcher。");
            var helper = typeof(TEngine.UnityToolbarExtenderLeft).GetNestedType("SceneHelper", BindingFlags.NonPublic);
            var start = helper?.GetMethod("StartScene", BindingFlags.Public | BindingFlags.Static);
            Require(start != null, "找不到TEngine Launcher入口。");
            SessionState.SetBool(Pending, true);
            try { start.Invoke(null, new object[] { "main" }); }
            catch { SessionState.SetBool(Pending, false); throw; }
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            if (!_running) VerifyAsync().Forget();
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        private static OpponentAiController Ai(BattleShopSceneController host) =>
            (OpponentAiController)typeof(BattleShopSceneController).GetField("_opponentAi", Private).GetValue(host);
        private static float Clock(OpponentAiController ai) =>
            (float)typeof(OpponentAiController).GetField("_clock", Private).GetValue(ai);
        private static void AdvanceAi(BattleShopSceneController host, float seconds) =>
            typeof(BattleShopSceneController).GetMethod("AdvanceOpponentAi", Private).Invoke(host, new object[] { seconds });
        private static PointerEventData Pointer(Vector2 point) => new PointerEventData(EventSystem.current)
        { pointerId = -1, position = point, button = PointerEventData.InputButton.Left };
        private static GameObject Slot(BattleShopUI ui, int id) =>
            ui.transform.Find("m_rect_ShopPanel/m_tf_Offers/Slot_" + id).gameObject;
        private static Vector2 Point(BattleShopUI ui, Transform node)
        {
            var canvas = ui.OwnerWindow.Canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null :
                canvas.worldCamera != null ? canvas.worldCamera : GameModule.UI.UICamera;
            return RectTransformUtility.WorldToScreenPoint(camera, node.position);
        }
        private static void Pick(BattleShopUI ui, int slot)
        {
            var node = Slot(ui, slot);
            ExecuteEvents.Execute(node, Pointer(Point(ui, node.transform)), ExecuteEvents.pointerDownHandler);
            ui.Drag.Advance(Time.unscaledTime + 0.21f);
            Require(ui.Drag.Phase == RobotDragPhase.Dragging, "交互开放后无法长按拿起商品。");
        }
        private static async UniTask Wait(Func<bool> condition, float seconds = 15f)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                Require(Time.realtimeSinceStartup < deadline, "功能验证等待超时。");
                await UniTask.Yield();
            }
        }
        private static void OpenGlass(BattleShopSceneController host, GlassBoardController glass, PhysicalCollisionBall ball)
        {
            var board = glass.GetComponentInParent<PlacementBoardView>();
            for (var column = 1; column < board.Model.Columns - 1; column++)
                for (var row = 1; row < board.Model.Rows - 1; row++)
                {
                    GlassView view;
                    while (glass.TryGetView(new BoardCoordinate(column, row), out view))
                    {
                        host.Context.Registry.AdvanceTime(1f);
                        Require(glass.ApplyCollision(view, ball) != GlassCollisionResult.Ignored, "无法通过玻璃碰撞打开验证区域。");
                    }
                }
        }

        private static async UniTaskVoid VerifyAsync()
        {
            _running = true;
            var report = new Report();
            var checks = new List<string>();
            var oldTimeScale = Time.timeScale;
            BattleShopSceneController host = null;
            try
            {
                await Wait(() => UIModule.IsValid && GameModule.UI.GetUI<MainMenuUI>() != null, 60f);
                var entry = GameApp.BeginBattleFromMainMenu(GameModule.UI.GetUI<MainMenuUI>());
                ShopOffer[] originalOffers = null;
                while (entry.Status == UniTaskStatus.Pending)
                {
                    host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                    var loading = GameModule.UI.GetUI<BattleLoadingUI>();
                    if (host != null && host.IsReady && loading != null && loading.Visible)
                    {
                        report.coveredFrames++;
                        Require(!host.CanInteractWithRobots, "加载遮罩仍可见时机器人交互已经开放。");
                        Require(!host.TryOpenRobotInteraction(host.Context.BattleId), "加载遮罩未关闭仍能显式开放交互。");
                        var state = host.Context.GetState(BattleSide.Opponent);
                        if (originalOffers == null) originalOffers = state.Slots.Select(s => s.Offer).ToArray();
                        Require(state.Slots.Select(s => s.Offer).SequenceEqual(originalOffers), "加载期间AI消费或刷新了商品。");
                        Require(host.Context.CreateOpponentAiSnapshot(host.OpponentGlass.GetComponentInParent<PlacementBoardView>().Model)
                            .Robots.Count == 0, "玩家被遮罩挡住时AI已经部署。");
                        Require(Mathf.Approximately(Clock(Ai(host)), 0f), "加载阶段提前消耗了AI首次行动延迟。");
                        var ui = host.ShopWindow;
                        var balance = host.Context.GetState(BattleSide.Player).EnergyCoins;
                        ui.PressSlot(0, -1, Point(ui, Slot(ui, 0).transform), Time.unscaledTime - 1f);
                        ui.Drag.Advance(Time.unscaledTime);
                        Slot(ui, 0).transform.parent.parent.Find("m_btn_Refresh").GetComponent<Button>().onClick.Invoke();
                        Require(ui.Drag.Phase == RobotDragPhase.Idle && !host.Context.IsBusy &&
                            host.Context.GetState(BattleSide.Player).EnergyCoins == balance,
                            "遮罩期间的玩家拿起或刷新未被拦截。");
                    }
                    await UniTask.Yield();
                }
                await entry;
                Require(report.coveredFrames > 0 && host != null && host.CanInteractWithRobots,
                    "没有观察到遮罩准备阶段或进入战斗后未开放交互。");
                Require(GameModule.UI.GetUI<BattleLoadingUI>() == null && GameModule.UI.GetUI<MainMenuUI>() == null,
                    "交互开放时加载界面或主界面仍存在。");
                checks.Add("完整Launcher加载流程：遮罩期间双方禁止操作，AI时钟保持0，关闭后统一开放");

                // 测试夹具暂停Unity时间以避免真实对局自行结束；不使用业务暂停来绕开交互校验。
                Time.timeScale = 0f;
                var ai = Ai(host);
                Require(host.TryOpenRobotInteraction(host.Context.BattleId), "重复开放不是幂等操作。");
                Require(!host.TryOpenRobotInteraction(host.Context.BattleId + 1), "错误BattleId可以开放交互。");
                var uiWindow = host.ShopWindow;
                var context = host.Context;
                var source = context.GetState(BattleSide.Player).Slots[0].Offer;
                Pick(uiWindow, 0);
                host.SetFrozen(true);
                Require(!host.CanInteractWithRobots && uiWindow.Drag.Phase == RobotDragPhase.Idle && !context.IsBusy &&
                    context.GetState(BattleSide.Player).Slots[0].Offer == source, "暂停未取消玩家手势并保留商品。");
                host.SetFrozen(false);
                Require(host.CanInteractWithRobots, "恢复暂停后交互未恢复。");
                checks.Add("正确对局幂等开放、错误对局拒绝、暂停取消手势与恢复");

                OpenGlass(host, host.PlayerGlass, host.Bootstrap.PlayerBall);
                OpenGlass(host, host.OpponentGlass, host.Bootstrap.OpponentBall);
                var board = host.PlayerGlass.GetComponentInParent<PlacementBoardView>();
                GameConfig.robot.Robot config;
                int level;
                PlaceableDefinition definition;
                Require(context.GetDragContent(BattleSide.Player, RobotDragSource.Offer(0, source.OfferId),
                    out config, out level, out definition) == ShopOperationResult.Success, "无法读取部署商品。");
                BoardCoordinate? anchor = null;
                for (var row = 1; row < board.Model.Rows - 1 && !anchor.HasValue; row++)
                    for (var column = 1; column < board.Model.Columns - 1; column++)
                    {
                        var candidate = new BoardCoordinate(column, row);
                        if (!board.Model.Evaluate(candidate, definition).IsValid) continue;
                        anchor = candidate;
                        break;
                    }
                Require(anchor.HasValue, "找不到真实地图上的合法部署位置。");
                var center = new Vector2((definition.Cells.Min(c => c.Column) + definition.Cells.Max(c => c.Column)) * 0.5f,
                    (definition.Cells.Min(c => c.Row) + definition.Cells.Max(c => c.Row)) * 0.5f);
                var point = (Vector2)host.Placement.SceneCamera.WorldToScreenPoint(board.transform.TransformPoint(
                    board.GetCellLocalPosition(anchor.Value) + (Vector3)(center * board.CellSize)));
                var coins = context.GetState(BattleSide.Player).EnergyCoins;
                Pick(uiWindow, 0);
                uiWindow.Drag.Move(-1, point);
                BoardPlacementTarget preview;
                Require(host.Placement.TryGetDragTarget(out preview) && preview.Anchor.Equals(anchor.Value), "中心吸附预览偏移。");
                uiWindow.Drag.Release(-1, point);
                await Wait(() => uiWindow.Drag.Phase == RobotDragPhase.Idle);
                var id = int.Parse(board.Model.GetOccupant(anchor.Value));
                Entity entity;
                Require(context.Registry.TryGet(id, out entity), "实际部署实体不存在。");
                var robot = (RobotEntity)entity;
                Require(robot.Anchor.Equals(anchor) && context.GetState(BattleSide.Player).EnergyCoins == coins - source.Price,
                    "预览与最终落点或购买扣费不一致。");
                var view = UnityEngine.Object.FindObjectsOfType<RobotView>().First(v => v.InstanceId == id);
                var hitPoint = (Vector2)host.Placement.SceneCamera.WorldToScreenPoint(board.GetCellWorldPosition(anchor.Value));
                ExecuteEvents.Execute(view.gameObject, Pointer(hitPoint), ExecuteEvents.pointerDownHandler);
                uiWindow.Drag.Advance(Time.unscaledTime + 0.21f);
                Require(uiWindow.Drag.Phase == RobotDragPhase.Dragging, "开放后场上机器人不能拿起。");
                uiWindow.Drag.Release(-1, new Vector2(-1000, -1000));
                await Wait(() => uiWindow.Drag.Phase == RobotDragPhase.Idle);
                Require(robot.Anchor.Equals(anchor) && board.Model.GetOccupant(anchor.Value) == robot.Definition.Id,
                    "非法落点没有回到原位置。");
                checks.Add("真实玩家长按、完整形状中心预览、按价购买部署、非法回位");

                // 用实际场景调度入口确定性推进AI时间，检验开放和暂停都不会提前耗尽初始延迟。
                var aiBefore = context.CreateOpponentAiSnapshot(host.OpponentGlass.GetComponentInParent<PlacementBoardView>().Model);
                Require(aiBefore.Robots.Count == 0 && Mathf.Approximately(Clock(ai), 0f), "开放瞬间AI已有提前动作。");
                AdvanceAi(host, 0.5f);
                Require(!ai.IsExecuting && Mathf.Approximately(Clock(ai), 0.5f), "AI没有保留首次行动延迟。");
                Require(host.TryOpenRobotInteraction(context.BattleId), "重复开放失败。");
                Require(Mathf.Approximately(Clock(ai), 0.5f), "重复开放重置了AI计时。");
                host.SetFrozen(true);
                AdvanceAi(host, 100f);
                Require(Mathf.Approximately(Clock(ai), 0.5f), "暂停期间仍累计AI行动时间。");
                host.SetFrozen(false);
                AdvanceAi(host, 0.29f);
                Require(!ai.IsExecuting && Mathf.Approximately(Clock(ai), 0.79f), "恢复后AI提前行动。");
                AdvanceAi(host, 0.02f);
                await Wait(() => !ai.IsExecuting);
                var aiAfter = context.CreateOpponentAiSnapshot(host.OpponentGlass.GetComponentInParent<PlacementBoardView>().Model);
                Require(aiAfter.Robots.Count == 1 && aiAfter.EnergyCoins < aiBefore.EnergyCoins, "延迟结束后AI未按正常事务部署。");
                AdvanceAi(host, 0f);
                Require(context.CreateOpponentAiSnapshot(host.OpponentGlass.GetComponentInParent<PlacementBoardView>().Model)
                    .Robots.Count == 1, "AI补跑了加载期间的动作。");
                checks.Add("AI首次延迟从开放起累计、暂停冻结、恢复无补跑、正常购买部署");

                Time.timeScale = oldTimeScale;
                var oldBattleId = context.BattleId;
                host.EndBattle();
                Require(!host.CanInteractWithRobots && context.IsEnded, "结束对局没有关闭交互。");
                await host.InitializeAsync();
                Require(host.IsReady && !host.CanInteractWithRobots && host.Context.BattleId != oldBattleId &&
                    !host.TryOpenRobotInteraction(oldBattleId), "新局继承旧开放状态或接受旧回调。");
                host.EndBattle();
                checks.Add("终局关闭、新局默认关闭及旧BattleId隔离");
                report.passed = true;
                report.details = "TEngine Launcher交互开放时序、玩家操作和AI调度验证通过。";
            }
            catch (Exception e)
            {
                report.details = e.ToString();
                Debug.LogError("[Interaction Opening Verification] " + e);
            }
            finally
            {
                Time.timeScale = oldTimeScale;
                _running = false;
                report.checks = checks.ToArray();
                File.WriteAllText(Path.Combine(Application.dataPath, "../Library/InteractionOpeningVerification.json"), JsonUtility.ToJson(report, true));
                if (report.passed) Debug.Log("[Interaction Opening Verification] PASS " + string.Join("；", checks));
            }
        }
    }
}
