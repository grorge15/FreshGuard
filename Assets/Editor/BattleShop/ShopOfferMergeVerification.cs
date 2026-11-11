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

        private static Camera EventCamera(BattleShopUI ui)
        {
            var canvas = ui.OwnerWindow.Canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null :
                canvas.worldCamera != null ? canvas.worldCamera : GameModule.UI.UICamera;
        }

        private static GameObject Slot(BattleShopUI ui, int id) =>
            ((IEnumerable<ShopRobotItemWidget>)typeof(BattleShopUI).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ui)).First(item => item.SlotId == id && item.gameObject.activeInHierarchy).gameObject;

        private static Vector2 Point(BattleShopUI ui, Transform node) =>
            RectTransformUtility.WorldToScreenPoint(EventCamera(ui), node.TransformPoint(((RectTransform)node).rect.center));

        private static PointerEventData Pointer(Vector2 point) => new PointerEventData(EventSystem.current)
        { pointerId = -1, position = point, button = PointerEventData.InputButton.Left };

        private static void Pick(BattleShopUI ui, int slot)
        {
            var node = Slot(ui, slot);
            ExecuteEvents.Execute(node, Pointer(Point(ui, node.transform)), ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(node, Pointer(Point(ui, node.transform) + Vector2.right), ExecuteEvents.beginDragHandler);
            Require(ui.Drag.Phase == RobotDragPhase.Dragging, "移动商品没有立即进入拖拽。");
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
            var slot = context.GetState(BattleSide.Player).FindSlot(slotId);
            Require(slot.Offer != null && !slot.IsPurchased, "夹具需要现有未购买商品。");
            var config = ConfigSystem.Instance.Tables.TbRobot.Get(robotId);
            var offer = (ShopOffer)Activator.CreateInstance(typeof(ShopOffer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { slot.Offer.OfferId, robotId, level, context.Rules.GetPrice(config.QualityId, level) }, null);
            typeof(ShopSlot).GetProperty("Offer").SetValue(slot, offer);
        }

        private static ShopSlot[] ResetOffers(BattleShopContext context, BattleShopUI ui, int robotId, int level = 1)
        {
            ui.Drag.Cancel();
            Coins(context, 10000);
            Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Success, "验证刷新失败。");
            var slots = context.GetState(BattleSide.Player).Slots.ToArray();
            Offer(context, slots[0].SlotId, robotId, level);
            Offer(context, slots[1].SlotId, robotId, level);
            ui.RefreshView();
            return slots;
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
                await UniTask.WaitUntil(() => host != null && host.CanInteractWithRobots)
                    .Timeout(TimeSpan.FromSeconds(30));
                Require(host != null && host.CanInteractWithRobots, "Launcher没有进入真实战斗。");
                oldHostEnabled = host.enabled;
                host.enabled = false;
                Time.timeScale = 0f;
                var context = host.Context;
                ui = host.ShopWindow;
                var state = context.GetState(BattleSide.Player);
                Require(ui.transform.Find("m_rect_ShopPanel/m_item_Storage") == null, "运行界面仍存在暂存区。");
                var robotId = state.Slots[0].Offer.RobotId;
                await UniTask.Yield();
                checks.Add("真实Launcher进入战斗，暂存区已移除");

                // SlotId是稳定身份；商品消耗后槽位会移除，不能将SlotId当作数组下标。
                ShopSlot[] slots = null;
                ShopOffer merged = null;
                for (var mergeLevel = 1; mergeLevel <= 5; mergeLevel++)
                {
                    slots = ResetOffers(context, ui, robotId, mergeLevel);
                    var source = slots[0].Offer;
                    var target = slots[1].Offer;
                    var third = slots[2].Offer;
                    Coins(context, 0);
                    ui.RefreshView();
                    var targetPoint = Point(ui, Slot(ui, slots[1].SlotId).transform);
                    Pick(ui, slots[0].SlotId);
                    Require(context.IsBusy && state.EnergyCoins == 0, "拿起商品提前扣费。");
                    Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Busy, "拖拽期间可以刷新。");
                    if (mergeLevel == 1)
                    {
                        ui.Drag.Move(-1, targetPoint);
                        var hits = new List<RaycastResult>();
                        EventSystem.current.RaycastAll(Pointer(targetPoint), hits);
                        Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<ShopPointerInput>() != null,
                            "拖拽图像遮挡商品射线。");
                        ui.RefreshView();
                        int hitSlot;
                        Require(ui.TryGetShopSlot(targetPoint, out hitSlot) && hitSlot == slots[1].SlotId, "动态槽位命中错误。");
                    }
                    await Drop(ui, slots[0].SlotId, targetPoint, false);
                    Require(state.EnergyCoins == 0 && slots[2].Offer == third, "商品合成扣费或影响其他商品。");
                    if (mergeLevel == 5)
                        Require(slots[0].Offer == source && slots[1].Offer == target, "Lv5仍被合成。");
                    else
                    {
                        merged = slots[1].Offer;
                        Require(state.FindSlot(slots[0].SlotId) == null && merged.Level == mergeLevel + 1 &&
                            merged.RobotId == robotId && merged.OfferId != source.OfferId && merged.OfferId != target.OfferId &&
                            merged.Price == context.Rules.GetPrice(ConfigSystem.Instance.Tables.TbRobot.Get(robotId).QualityId, mergeLevel + 1),
                            "免费合成没有移除来源、升级目标或更新价格。");
                        ui.Drag.Release(-1, targetPoint);
                        Require(slots[1].Offer == merged, "重复松手重复合成。");
                    }
                }
                checks.Add("动态商品身份、Lv1～Lv4零余额免费合成、Lv5上限、刷新互斥和重复松手");

                slots = ResetOffers(context, ui, robotId);
                var original = slots[0].Offer;
                Coins(context, 0);
                var differentId = ConfigSystem.Instance.Tables.TbRobot.DataList.First(r => r.Id != robotId).Id;
                Offer(context, slots[1].SlotId, differentId, 1);
                ui.RefreshView();
                Pick(ui, slots[0].SlotId);
                await Drop(ui, slots[0].SlotId, Point(ui, Slot(ui, slots[1].SlotId).transform));
                Require(slots[0].Offer == original && state.EnergyCoins == 0, "不兼容商品消耗了来源。");
                Pick(ui, slots[0].SlotId);
                await Drop(ui, slots[0].SlotId, new Vector2(-1000, -1000));
                Require(slots[0].Offer == original && !context.IsBusy, "非法落点丢失商品或交互锁。");
                checks.Add("不兼容商品及界外落点原位恢复、不扣币");

                slots = ResetOffers(context, ui, robotId);
                Pick(ui, slots[0].SlotId);
                await Drop(ui, slots[0].SlotId, Point(ui, Slot(ui, slots[1].SlotId).transform));
                var mergedSlot = slots[1];
                merged = mergedSlot.Offer;
                GameConfig.robot.Robot deploymentConfig;
                int deploymentLevel;
                PlaceableDefinition definition;
                Require(context.GetDragContent(BattleSide.Player, RobotDragSource.Offer(mergedSlot.SlotId, merged.OfferId),
                    out deploymentConfig, out deploymentLevel, out definition) == ShopOperationResult.Success, "合成商品形状不存在。");
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
                        if (!board.Model.Evaluate(candidate, definition).IsValid) continue;
                        anchor = candidate;
                        break;
                    }
                Require(anchor.HasValue, "找不到验证部署位置。");
                var center = new Vector2((definition.Cells.Min(c => c.Column) + definition.Cells.Max(c => c.Column)) * 0.5f,
                    (definition.Cells.Min(c => c.Row) + definition.Cells.Max(c => c.Row)) * 0.5f);
                var deployPoint = (Vector2)host.Placement.SceneCamera.WorldToScreenPoint(board.transform.TransformPoint(
                    board.GetCellLocalPosition(anchor.Value) + (Vector3)(center * board.CellSize)));
                Coins(context, 0);
                ui.RefreshView();
                Pick(ui, mergedSlot.SlotId);
                await Drop(ui, mergedSlot.SlotId, deployPoint);
                Require(mergedSlot.Offer == merged && state.EnergyCoins == 0 && board.Model.GetOccupant(anchor.Value) == null,
                    "余额不足仍购买部署。");
                Coins(context, merged.Price);
                ui.RefreshView();
                Pick(ui, mergedSlot.SlotId);
                await Drop(ui, mergedSlot.SlotId, deployPoint);
                Require(state.FindSlot(mergedSlot.SlotId) == null && state.EnergyCoins == 0, "部署没有仅购买一次。");
                Entity entity;
                Require(context.Registry.TryGet(int.Parse(board.Model.GetOccupant(anchor.Value)), out entity), "已购机器人不存在。");
                var robot = (RobotEntity)entity;
                Require(robot.Level == 2 && robot.Location == RobotLocation.Board && robot.Anchor.Equals(anchor), "直接部署丢失等级或锚点。");
                checks.Add("余额复核、按合成价格仅购买一次与中心对齐部署");

                slots = ResetOffers(context, ui, robotId, 2);
                var boardHit = (Vector2)host.Placement.SceneCamera.WorldToScreenPoint(board.GetCellWorldPosition(anchor.Value));
                var beforeMerge = state.EnergyCoins;
                var mergePrice = slots[0].Offer.Price;
                Pick(ui, slots[0].SlotId);
                await Drop(ui, slots[0].SlotId, boardHit);
                Require(robot.Level == 3 && state.EnergyCoins == beforeMerge - mergePrice && state.FindSlot(slots[0].SlotId) == null,
                    "商品向场上合成行为改变。");
                var view = UnityEngine.Object.FindObjectsOfType<RobotView>().First(v => v.InstanceId == robot.InstanceId);
                ExecuteEvents.Execute(view.gameObject, Pointer(boardHit), ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(view.gameObject, Pointer(boardHit + Vector2.right), ExecuteEvents.beginDragHandler);
                Require(ui.Drag.Phase == RobotDragPhase.Dragging, "场上机器人无法拿起。");
                ui.Drag.Release(-1, new Vector2(-1000, -1000));
                await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
                Require(robot.Location == RobotLocation.Board && robot.Anchor.Equals(anchor) &&
                    board.Model.GetOccupant(anchor.Value) == robot.Definition.Id, "非法落点没有回到原位置。");
                checks.Add("商品向场上合成与场上机器人非法回位");

                slots = ResetOffers(context, ui, robotId);
                original = slots[0].Offer;
                Pick(ui, slots[0].SlotId);
                ui.Visible = false;
                Require(ui.Drag.Phase == RobotDragPhase.Idle && !context.IsBusy && slots[0].Offer == original,
                    "隐藏窗口没有取消手势并恢复商品。");
                ui.Visible = true;
                Pick(ui, slots[0].SlotId);
                var oldInteraction = context.ActiveInteraction.InteractionId;
                host.EndBattle();
                await host.InitializeAsync();
                Require(context.IsEnded && !context.IsBusy && host.Context.BattleId != context.BattleId &&
                    host.Context.TryCommitOfferMerge(oldInteraction, slots[1].SlotId, slots[1].Offer.OfferId) == ShopOperationResult.InvalidSource,
                    "旧对局回调污染新对局。");
                checks.Add("隐藏窗口取消、终局清理与新局隔离");
                report.passed = true;
                report.details = "TEngine Launcher真实商店回归通过，暂存区移除且购买、部署、合成、取消正常。";
            }
            catch (Exception e) { report.details = e.ToString(); Debug.LogError("[Shop Offer Merge Verification] " + e); }
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
