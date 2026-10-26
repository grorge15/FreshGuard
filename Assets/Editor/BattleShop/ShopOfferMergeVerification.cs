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
    /// <summary>从TEngine Launcher进入真实商店。确定性夹具只调整当前运行商品和余额，不保存资源。</summary>
    public static class ShopOfferMergeVerification
    {
        private const string Pending = "FreshGuard.ShopOfferMerge.LaunchPending";
        private static bool _running;

        [Serializable]
        private sealed class Report { public bool passed; public string details; public string[] checks; }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("FreshGuard/Shop/Verify Offer Merge via TEngine Launcher")]
        public static void RunViaLauncher()
        {
            Require(!EditorApplication.isPlaying && !EditorApplication.isCompiling, "请先停止运行并等待编译。");
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty, "场景有未保存修改，不能切换Launcher。");
            var helper = typeof(TEngine.UnityToolbarExtenderLeft).GetNestedType("SceneHelper", BindingFlags.NonPublic);
            var start = helper?.GetMethod("StartScene", BindingFlags.Public | BindingFlags.Static);
            Require(start != null, "找不到TEngine Launcher入口。");
            SessionState.SetBool(Pending, true);
            try { start.Invoke(null, new object[] { "Main" }); }
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

        private static Camera EventCamera(BattleShopUI ui)
        {
            var canvas = ui.OwnerWindow.Canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null :
                canvas.worldCamera != null ? canvas.worldCamera : GameModule.UI.UICamera;
        }

        private static GameObject Slot(BattleShopUI ui, int id) =>
            ui.transform.Find("m_rect_ShopPanel/m_tf_Offers/Slot_" + id).gameObject;

        private static Vector2 Point(BattleShopUI ui, Transform node) =>
            RectTransformUtility.WorldToScreenPoint(EventCamera(ui), node.TransformPoint(((RectTransform)node).rect.center));

        private static PointerEventData Pointer(Vector2 point) => new PointerEventData(EventSystem.current)
        { pointerId = -1, position = point, button = PointerEventData.InputButton.Left };

        private static void Pick(BattleShopUI ui, int slot)
        {
            var node = Slot(ui, slot);
            ExecuteEvents.Execute(node, Pointer(Point(ui, node.transform)), ExecuteEvents.pointerDownHandler);
            ui.Drag.Advance(Time.unscaledTime + 0.21f);
            Require(ui.Drag.Phase == RobotDragPhase.Dragging, "长按商品没有进入拖拽。");
        }

        private static async UniTask Drop(BattleShopUI ui, int sourceSlot, Vector2 point, bool move = true)
        {
            if (move) ui.Drag.Move(-1, point);
            // PointerUp仍发送给原按下槽位，覆盖没有最终Move事件的情况。
            ExecuteEvents.Execute(Slot(ui, sourceSlot), Pointer(point), ExecuteEvents.pointerUpHandler);
            await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
            await UniTask.Yield();
        }

        private static void Coins(BattleShopContext context, int amount) =>
            typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(context.GetState(BattleSide.Player), amount);

        private static void Offer(BattleShopContext context, int slotId, int robotId, int level)
        {
            var slot = context.GetState(BattleSide.Player).Slots[slotId];
            Require(slot.Offer != null && !slot.IsPurchased, "夹具需要现有未购买商品。");
            var config = ConfigSystem.Instance.Tables.TbRobot.Get(robotId);
            var offer = (ShopOffer)Activator.CreateInstance(typeof(ShopOffer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { slot.Offer.OfferId, robotId, level, context.Rules.GetPrice(config.QualityId, level) }, null);
            typeof(ShopSlot).GetProperty("Offer").SetValue(slot, offer);
        }

        private static void ResetOffers(BattleShopContext context, BattleShopUI ui, int robotId, int level = 1)
        {
            ui.Drag.Cancel();
            Coins(context, 10000);
            Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Success, "验证刷新失败。");
            Offer(context, 0, robotId, level);
            Offer(context, 1, robotId, level);
            ui.RefreshView();
        }

        private static async UniTaskVoid VerifyAsync()
        {
            _running = true;
            var report = new Report();
            var checks = new List<string>();
            BattleShopUI ui = null;
            BattleShopSceneController host = null;
            var oldTimeScale = Time.timeScale;
            var oldHostEnabled = true;
            try
            {
                await UniTask.WaitUntil(() => UIModule.IsValid && GameModule.UI.GetUI<MainMenuUI>() != null)
                    .Timeout(TimeSpan.FromSeconds(60));
                await GameApp.BeginBattleFromMainMenu(GameModule.UI.GetUI<MainMenuUI>());
                host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                Require(host != null && host.IsCombatOperational, "Launcher没有进入真实战斗。");
                // 业务暂停会关闭机器人交互；夹具只暂停自动推进，保留真实交互条件。
                oldHostEnabled = host.enabled;
                host.enabled = false;
                Time.timeScale = 0f;
                var context = host.Context;
                ui = host.ShopWindow;
                var state = context.GetState(BattleSide.Player);
                var robotId = state.Slots[0].Offer.RobotId;
                await UniTask.Yield();
                ResetOffers(context, ui, robotId);
                Coins(context, 0);
                ui.RefreshView();
                var source = state.Slots[0].Offer;
                var target = state.Slots[1].Offer;
                var third = state.Slots[2].Offer;
                Pick(ui, 0);
                Require(context.IsBusy && state.EnergyCoins == 0, "零余额拿起失败或提前扣费。");
                Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Busy, "拖拽期间可以刷新。");
                var targetPoint = Point(ui, Slot(ui, 1).transform);
                ui.Drag.Move(-1, targetPoint);
                Require(Slot(ui, 1).GetComponent<Image>().color == Color.cyan, "可合成悬停目标未高亮。");
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(Pointer(targetPoint), hits);
                Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<ShopPointerInput>() != null &&
                    hits[0].gameObject.transform.IsChildOf(Slot(ui, 1).transform), "拖拽图像遮挡商品射线。");
                ui.RefreshView();
                Require(Slot(ui, 1).GetComponent<Image>().color == Color.cyan, "UI刷新丢失悬停高亮。");
                var ghost = ui.transform.Find("m_item_DragGhost/m_img_RobotIcon") as RectTransform;
                Require(ghost != null && Vector2.Distance(Point(ui, ghost), targetPoint) < 0.1f, "拖拽图像中心偏移。");
                await Drop(ui, 0, targetPoint, false);
                var merged = state.Slots[1].Offer;
                Require(state.Slots[0].Offer == null && merged.Level == 2 && merged.RobotId == robotId &&
                    !state.Slots[1].IsPurchased && state.Slots[2].Offer == third && state.EnergyCoins == 0,
                    "免费合成没有原子清来源/升级目标，或影响了其他商品。");
                Require(merged.OfferId != source.OfferId && merged.OfferId != target.OfferId &&
                    Slot(ui, 1).transform.Find("m_text_Price").GetComponent<Text>().text == merged.Price.ToString() &&
                    Slot(ui, 1).GetComponent<Image>().color == RobotLevelStyle.ColorForLevel(2) &&
                    Slot(ui, 1).transform.Find("m_img_RobotIcon").GetComponent<Image>().enabled,
                    "合成后商品身份、价格、等级配色或图标未更新。");
                ui.Drag.Release(-1, targetPoint);
                Require(state.Slots[1].Offer == merged && state.EnergyCoins == 0, "重复松手重复合成。");
                checks.Add("真实Launcher、零余额免费合成、射线、高亮刷新、目标保留、价格与重复松手");

                // 空槽、来源自身、类型或等级不匹配均不穿透购买。
                Pick(ui, 1);
                await Drop(ui, 1, Point(ui, Slot(ui, 0).transform));
                Require(state.Slots[1].Offer == merged && state.EnergyCoins == 0, "空槽落点消费了商品。");
                Pick(ui, 1);
                await Drop(ui, 1, targetPoint);
                Require(state.Slots[1].Offer == merged, "来源自身落点消费了商品。");
                ResetOffers(context, ui, robotId);
                var differentId = ConfigSystem.Instance.Tables.TbRobot.DataList.First(r => r.Id != robotId).Id;
                Offer(context, 1, differentId, 1);
                ui.RefreshView();
                source = state.Slots[0].Offer;
                target = state.Slots[1].Offer;
                Pick(ui, 0);
                ui.Drag.Move(-1, targetPoint);
                Require(Slot(ui, 1).GetComponent<Image>().color != Color.cyan, "不兼容商品仍高亮。");
                await Drop(ui, 0, targetPoint);
                Require(state.Slots[0].Offer == source && state.Slots[1].Offer == target, "类型不同仍消耗商品。");
                Offer(context, 1, robotId, 2);
                target = state.Slots[1].Offer;
                ui.RefreshView();
                Pick(ui, 0);
                await Drop(ui, 0, targetPoint);
                Require(state.Slots[0].Offer == source && state.Slots[1].Offer == target, "等级不同仍消耗商品。");
                checks.Add("空槽、来源自身、类型和等级不匹配时恢复商品");

                for (var level = 2; level <= 5; level++)
                {
                    ResetOffers(context, ui, robotId, level);
                    source = state.Slots[0].Offer;
                    target = state.Slots[1].Offer;
                    var before = state.EnergyCoins;
                    Pick(ui, 0);
                    // 首次移动的坐标就是PointerUp，覆盖最终坐标刷新。
                    await Drop(ui, 0, targetPoint, false);
                    Require(state.EnergyCoins == before, "高等级商品合成扣费。");
                    if (level < 5)
                        Require(state.Slots[0].Offer == null && state.Slots[1].Offer.Level == level + 1 &&
                            state.Slots[1].Offer.Price == context.Rules.GetPrice(
                                ConfigSystem.Instance.Tables.TbRobot.Get(robotId).QualityId, level + 1), "高等级合成/定价错误。");
                    else Require(state.Slots[0].Offer == source && state.Slots[1].Offer == target, "Lv5仍被合成。");
                }
                checks.Add("Lv2～Lv4升级、Lv5上限、无最终Move的PointerUp提交");

                ResetOffers(context, ui, robotId);
                var offersTransform = Slot(ui, 0).transform.parent;
                var oldScale = offersTransform.localScale;
                try
                {
                    offersTransform.localScale = oldScale * 0.8f;
                    targetPoint = Point(ui, Slot(ui, 1).transform);
                    int hitSlot;
                    Require(ui.TryGetShopSlot(targetPoint, out hitSlot) && hitSlot == 1, "缩放后商品命中偏移。");
                    Pick(ui, 0);
                    ui.Drag.Move(-1, targetPoint);
                    Require(Slot(ui, 1).GetComponent<Image>().color == Color.cyan, "缩放后合成高亮错误。");
                    ui.Drag.Cancel();
                    Require(Slot(ui, 1).GetComponent<Image>().color == RobotLevelStyle.ColorForLevel(1), "取消未恢复高亮。");
                }
                finally { offersTransform.localScale = oldScale; }
                checks.Add("槽位缩放、坐标转换和取消高亮恢复");

                // 使用合成商品走真实暂存购买，再拖入棋盘与已有场上合成链路。
                targetPoint = Point(ui, Slot(ui, 1).transform);
                Pick(ui, 0);
                await Drop(ui, 0, targetPoint);
                merged = state.Slots[1].Offer;
                var storage = ui.transform.Find("m_rect_ShopPanel/m_item_Storage");
                Coins(context, 0);
                ui.RefreshView();
                Pick(ui, 1);
                await Drop(ui, 1, Point(ui, storage));
                Require(state.Slots[1].Offer == merged && !state.StorageInstanceId.HasValue && state.EnergyCoins == 0,
                    "余额不足仍购买进入暂存。");
                Coins(context, merged.Price);
                ui.RefreshView();
                Pick(ui, 1);
                await Drop(ui, 1, Point(ui, storage));
                Require(state.StorageInstanceId.HasValue && state.Slots[1].Offer == null && state.EnergyCoins == 0,
                    "合成后商品没有按新价仅购买一次。");
                Entity storedEntity;
                Require(context.Registry.TryGet(state.StorageInstanceId.Value, out storedEntity), "已购机器人不存在。");
                var robot = (RobotEntity)storedEntity;
                Require(robot.Level == 2, "购买丢失合成等级。");

                var board = host.PlayerGlass.GetComponentInParent<PlacementBoardView>();
                for (var column = 1; column < board.Model.Columns - 1; column++)
                    for (var row = 1; row < board.Model.Rows - 1; row++)
                    {
                        GlassView glass;
                        while (host.PlayerGlass.TryGetView(new BoardCoordinate(column, row), out glass))
                        {
                            context.Registry.AdvanceTime(1f);
                            Require(host.PlayerGlass.ApplyCollision(glass, host.Bootstrap.PlayerBall) != GlassCollisionResult.Ignored,
                                "无法通过真实玻璃碰撞打开验证区域。");
                        }
                    }
                BoardCoordinate? anchor = null;
                for (var column = 1; column < board.Model.Columns - 1 && !anchor.HasValue; column++)
                    for (var row = 1; row < board.Model.Rows - 1; row++)
                    {
                        var candidate = new BoardCoordinate(column, row);
                        if (!board.Model.Evaluate(candidate, robot.Definition).IsValid) continue;
                        anchor = candidate;
                        break;
                    }
                Require(anchor.HasValue, "找不到验证部署位置。");
                var center = new Vector2((robot.Definition.Cells.Min(c => c.Column) + robot.Definition.Cells.Max(c => c.Column)) * 0.5f,
                    (robot.Definition.Cells.Min(c => c.Row) + robot.Definition.Cells.Max(c => c.Row)) * 0.5f);
                var deployPoint = (Vector2)host.Placement.SceneCamera.WorldToScreenPoint(board.transform.TransformPoint(
                    board.GetCellLocalPosition(anchor.Value) + (Vector3)(center * board.CellSize)));
                ui.PressStorage(-1, Point(ui, storage), Time.unscaledTime);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                ui.Drag.Release(-1, deployPoint);
                await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
                Require(robot.Location == RobotLocation.Board && robot.Anchor.Equals(anchor) && !state.StorageInstanceId.HasValue,
                    "暂存部署或完整形状中心对齐发生变化。");
                checks.Add("合成商品余额复核、按新价格购买一次、暂存与中心对齐部署");

                ResetOffers(context, ui, robotId, 2);
                var boardHit = (Vector2)host.Placement.SceneCamera.WorldToScreenPoint(board.GetCellWorldPosition(anchor.Value));
                var beforeBoardMerge = state.EnergyCoins;
                var boardMergePrice = state.Slots[0].Offer.Price;
                Pick(ui, 0);
                await Drop(ui, 0, boardHit);
                Require(robot.Level == 3 && state.EnergyCoins == beforeBoardMerge - boardMergePrice &&
                    state.Slots[0].Offer == null && robot.Anchor.Equals(anchor), "原有场上合成行为改变。");
                var view = UnityEngine.Object.FindObjectsOfType<RobotView>().First(v => v.InstanceId == robot.InstanceId);
                ExecuteEvents.Execute(view.gameObject, Pointer(boardHit), ExecuteEvents.pointerDownHandler);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                Require(ui.Drag.Phase == RobotDragPhase.Dragging, "场上机器人无法拿起。");
                ui.Drag.Release(-1, new Vector2(-1000, -1000));
                await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
                Require(robot.Location == RobotLocation.Board && robot.Anchor.Equals(anchor) &&
                    board.Model.GetOccupant(anchor.Value) == robot.Definition.Id, "非法落点没有回到原位置。");
                checks.Add("原有商品向场上合成、价格扣除和非法回位");

                ResetOffers(context, ui, robotId);
                source = state.Slots[0].Offer;
                target = state.Slots[1].Offer;
                Pick(ui, 0);
                ui.Drag.Move(-1, Point(ui, Slot(ui, 1).transform));
                ui.Visible = false;
                Require(ui.Drag.Phase == RobotDragPhase.Idle && !context.IsBusy && state.Slots[0].Offer == source &&
                    state.Slots[1].Offer == target && Slot(ui, 1).GetComponent<Image>().color == RobotLevelStyle.ColorForLevel(1),
                    "隐藏UI没有取消手势、恢复商品或清理高亮。");
                ui.Visible = true;
                Pick(ui, 0);
                var oldInteraction = context.ActiveInteraction.InteractionId;
                host.EndBattle();
                await UniTask.Yield();
                await host.InitializeAsync();
                Require(context.IsEnded && !context.IsBusy && host.Context.BattleId != context.BattleId &&
                    host.Context.TryCommitOfferMerge(oldInteraction, 1, target.OfferId) == ShopOperationResult.InvalidSource,
                    "旧对局回调污染新对局。");
                checks.Add("隐藏窗口取消、终局清理和新局隔离");
                report.passed = true;
                report.details = "TEngine Launcher真实商店拖拽合成及购买/部署回归通过；商品与余额夹具只作用于运行实例。";
            }
            catch (Exception e)
            {
                report.details = e.ToString();
                Debug.LogError("[Shop Offer Merge Verification] " + e);
            }
            finally
            {
                if (ui != null && ui.gameObject != null) ui.Drag.Cancel();
                if (host != null) host.enabled = oldHostEnabled;
                Time.timeScale = oldTimeScale;
                _running = false;
                report.checks = checks.ToArray();
                File.WriteAllText(Path.Combine(Application.dataPath, "../Library/ShopOfferMergeVerification.json"), JsonUtility.ToJson(report, true));
                if (report.passed) Debug.Log("[Shop Offer Merge Verification] PASS " + string.Join("；", checks));
            }
        }
    }
}
