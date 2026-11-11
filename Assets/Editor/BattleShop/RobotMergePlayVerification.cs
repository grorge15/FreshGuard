using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>运行真实窗口与指针事件；验收夹具只作用于当前测试对局。</summary>
    public static class RobotMergePlayVerification
    {
        [Serializable]
        private sealed class Report { public bool passed; public string details; public string[] checks; }
        [MenuItem("FreshGuard/Robots/Verify Running Merge")]
        public static void Verify() { VerifyAsync().Forget(); }
        public static void VerifyRegressions() { VerifyRegressionsAsync().Forget(); }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static PointerEventData Pointer(Vector2 point) => new PointerEventData(EventSystem.current)
        { pointerId = -1, position = point, button = PointerEventData.InputButton.Left };
        private static Vector2 Point(Transform node) => RectTransformUtility.WorldToScreenPoint(GameModule.UI.UICamera, node.position);
        private static GameObject Slot(BattleShopUI ui, int id) => ((IEnumerable<ShopRobotItemWidget>)typeof(BattleShopUI).GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(ui)).First(item => item.SlotId == id && item.gameObject.activeInHierarchy).gameObject;

        private static Vector2 PlacementPoint(Camera camera, PlacementBoardView board, BoardCoordinate anchor, PlaceableDefinition shape)
        {
            var center = new Vector2((shape.Cells.Min(c => c.Column) + shape.Cells.Max(c => c.Column)) * 0.5f,
                (shape.Cells.Min(c => c.Row) + shape.Cells.Max(c => c.Row)) * 0.5f);
            var local = board.GetCellLocalPosition(anchor) + (Vector3)(center * board.CellSize);
            return camera.WorldToScreenPoint(board.transform.TransformPoint(local));
        }

        private static async UniTask ReleaseWithoutMove(BattleShopUI ui, BattleShopSceneController host,
            Vector2 point, BoardCoordinate expectedAnchor)
        {
            ui.Drag.Release(-1, point);
            BoardPlacementTarget target;
            // Existing board views can commit synchronously and clear the preview before Release returns.
            if (ui.Drag.Phase == RobotDragPhase.Committing)
                Require(host.Placement.TryGetDragTarget(out target) && target.Anchor.Equals(expectedAnchor),
                    "PointerUp最终坐标未同步到预览与提交目标。");
            var ghost = ui.transform.Find("m_item_DragGhost/m_img_RobotIcon").GetComponent<RectTransform>();
            var canvas = ghost.GetComponentInParent<Canvas>().rootCanvas;
            var eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var ghostCenter = RectTransformUtility.WorldToScreenPoint(eventCamera, ghost.TransformPoint(ghost.rect.center));
            Require(Vector2.Distance(ghostCenter, point) < 0.1f, "PointerUp最终图像中心未跟随指针。");
            await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
            await UniTask.Yield();
        }

        private static void PickSlot(BattleShopUI ui, int id)
        {
            var node = Slot(ui, id);
            ExecuteEvents.Execute(node, Pointer(Point(node.transform)), ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(node, Pointer(Point(node.transform) + Vector2.right), ExecuteEvents.beginDragHandler);
            Require(ui.Drag.Phase == RobotDragPhase.Dragging, "商品未进入有效拖拽。");
        }

        private static async UniTask Drop(BattleShopUI ui, Vector2 point)
        {
            ui.Drag.Move(-1, point);
            ui.Drag.Release(-1, point);
            await UniTask.WaitUntil(() => ui.gameObject == null || ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
            await UniTask.Yield();
        }

        private static async UniTask<int> FindOffer(BattleShopContext context, int robotId, int level)
        {
            var state = context.GetState(BattleSide.Player);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var match = state.Slots.FirstOrDefault(s => s.Offer != null && s.Offer.RobotId == robotId && s.Offer.Level == level);
                if (match != null) { await UniTask.Yield(); return match.SlotId; }
                Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Success, "验收商品刷新失败。");
            }
            throw new InvalidOperationException("无法生成验收匹配商品。");
        }

        private static RobotView View(int id) => UnityEngine.Object.FindObjectsOfType<RobotView>().Single(v => v.InstanceId == id);

        private static async UniTask VerifyRegressionsAsync()
        {
            var checks = new List<string>();
            var failures = new List<string>();
            var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
            foreach (var test in new[] { "reward-cancel", "close-restart", "release-before-move", "reward-restart" })
            {
                try
                {
                    host.EndBattle();
                    await host.InitializeAsync();
                    host.SetFrozen(true);
                    var ui = host.ShopWindow;
                    var context = host.Context;
                    var state = context.GetState(BattleSide.Player);
                    if (test == "release-before-move")
                    {
                        var board = host.PlayerGlass.GetComponentInParent<PlacementBoardView>();
                        var offer = state.FindSlot(0).Offer;
                        GameConfig.robot.Robot config;
                        int level;
                        PlaceableDefinition shape;
                        Require(context.GetDragContent(BattleSide.Player, RobotDragSource.Offer(0, offer.OfferId),
                            out config, out level, out shape) == ShopOperationResult.Success, "读取形状失败。");
                        var anchor = new BoardCoordinate(2, 2);
                        foreach (var cell in shape.Cells)
                            board.Model.SetOpen(new BoardCoordinate(anchor.Column + cell.Column, anchor.Row + cell.Row), true);
                        var point = PlacementPoint(host.Placement.SceneCamera, board, anchor, shape);
                        ui.PressSlot(0, -1, Point(Slot(ui, 0).transform), Time.unscaledTime);
                        ui.Drag.Release(-1, point);
                        await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
                        Require(board.Model.GetOccupant(anchor) != null && state.EnergyCoins == 20 - offer.Price,
                            "快速拖动在移动事件前松手时未按最终位置提交。");
                    }
                    else
                    {
                        PickSlot(ui, 0);
                        var handled = false;
                        Action<int> onChanged = side =>
                        {
                            if (handled) return;
                            handled = true;
                            if (test == "reward-cancel") ui.Drag.Cancel();
                            else { host.EndBattle(); host.InitializeAsync().Forget(); }
                        };
                        GameEvent.AddEventListener<int>(BattleShopEvents.Changed, onChanged);
                        try
                        {
                            if (test == "reward-cancel")
                            {
                                context.Rewards.TryGrantGlassBreak(context.BattleId, "cancel-in-reward", BattleSide.Player, 1);
                                Require(ui.Drag.Phase == RobotDragPhase.Idle && !context.IsBusy,
                                    "奖励事务通知内取消留下交互锁。");
                            }
                            else
                            {
                                if (test == "reward-restart")
                                    context.Rewards.TryGrantGlassBreak(context.BattleId, "restart-in-reward", BattleSide.Player, 1);
                                else host.EndBattle();
                                Require(handled, "未触发窗口关闭取消通知。");
                                await UniTask.WaitUntil(() => host.IsReady && host.Context != context).Timeout(TimeSpan.FromSeconds(15));
                                Require(!host.Context.IsEnded && ui.gameObject == null, "窗口关闭通知中重启未隔离旧局。");
                            }
                        }
                        finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, onChanged); }
                    }
                    checks.Add(test);
                }
                catch (Exception e) { failures.Add(test + ": " + e.Message); }
            }
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/RobotMergeRegressionVerification.json"),
                JsonUtility.ToJson(new Report { passed = failures.Count == 0, details = string.Join("；", failures), checks = checks.ToArray() }, true));
        }

        public static async UniTask VerifyAsync()
        {
            var report = new Report();
            var checks = new List<string>();
            try
            {
                Require(Application.isPlaying, "需要从启动场景进入 PlayMode。");
                var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                Require(host != null, "场景缺少商店控制器。");
                host.EndBattle();
                await host.InitializeAsync();
                await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(15));
                var context = host.Context;
                var ui = host.ShopWindow;
                var state = context.GetState(BattleSide.Player);
                var board = host.PlayerGlass.GetComponentInParent<PlacementBoardView>();
                var camera = host.Placement.SceneCamera;
                Require(ui != null && ui.IsPrepare && state.EnergyCoins == 20, "新局窗口或开局余额错误。");
                Require(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length == 1, "重复活动 EventSystem。");
                Require(camera.GetComponent<Physics2DRaycaster>() != null, "场景相机缺少盘面指针射线。");
                await UniTask.Yield();

                var first = state.FindSlot(0).Offer;
                var slot = Slot(ui, 0);
                var p = Pointer(Point(slot.transform));
                ExecuteEvents.Execute(slot, p, ExecuteEvents.pointerDownHandler);
                Require(state.FindSlot(0).Offer == first && state.EnergyCoins == 20 && !state.FindSlot(0).InstanceId.HasValue,
                    "按下提前购买扣币。");
                ExecuteEvents.Execute(slot, p, ExecuteEvents.pointerUpHandler);
                Require(ui.Drag.Phase == RobotDragPhase.Idle && state.FindSlot(0).Offer == first, "短按未保留商品。");
                PickSlot(ui, 0);
                Require(state.EnergyCoins == 20 && context.IsBusy, "开始拖动时提前购买或没有交互锁。");
                Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Busy, "拖拽中刷新未拦截。");
                Require(Mathf.Approximately(host.Bootstrap.PlayerBall.EffectiveSpeed, host.Bootstrap.PlayerBall.Speed * 0.7f) &&
                    Mathf.Approximately(host.Bootstrap.OpponentBall.EffectiveSpeed, host.Bootstrap.OpponentBall.Speed), "未正确按方减速。");
                await Drop(ui, new Vector2(-1000, -1000));
                Require(state.EnergyCoins == 20 && state.FindSlot(0).Offer == first && !context.IsBusy, "非法落点扣币或丢失商品。");
                Require(Mathf.Approximately(host.Bootstrap.PlayerBall.EffectiveSpeed, host.Bootstrap.PlayerBall.Speed), "失败后未恢复球速。");
                checks.Add("真实指针轻点、即时拖动、非法回位、交互锁与己方球速");

                // 用玻璃系统真实碰撞打开内部测试区域，外围仍由模型禁止部署。
                for (var column = 1; column < board.Model.Columns - 1; column++)
                    for (var row = 1; row < board.Model.Rows - 1; row++)
                    {
                        var cell = new BoardCoordinate(column, row);
                        GlassView glass;
                        while (host.PlayerGlass.TryGetView(cell, out glass))
                        {
                            context.Registry.AdvanceTime(1f);
                            var result = host.PlayerGlass.ApplyCollision(glass, host.Bootstrap.PlayerBall);
                            Require(result != GlassCollisionResult.Ignored, "玻璃验收夹具碰撞未生效。");
                        }
                    }
                host.SetFrozen(true);
                context.Rewards.TryGrantGlassBreak(context.BattleId, "merge-verification-funds", BattleSide.Player, 2000);
                await UniTask.Yield();
                var anchor = new BoardCoordinate(2, 2);
                GameConfig.robot.Robot config;
                int level;
                PlaceableDefinition shape;
                Require(context.GetDragContent(BattleSide.Player, RobotDragSource.Offer(0, state.FindSlot(0).Offer.OfferId),
                    out config, out level, out shape) == ShopOperationResult.Success, "读取形状失败。");
                var dropPoint = PlacementPoint(camera, board, anchor, shape);
                var anchorPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(anchor));
                var beforeBuy = state.EnergyCoins;
                var price = state.FindSlot(0).Offer.Price;
                var configId = state.FindSlot(0).Offer.RobotId;
                PickSlot(ui, 0);
                // No Move event at the final position: PointerUp must refresh the centered target.
                await ReleaseWithoutMove(ui, host, dropPoint, anchor);
                var targetId = int.Parse(board.Model.GetOccupant(anchor));
                Entity entity;
                Require(context.Registry.TryGet(targetId, out entity), "合法购买部署没有实体。");
                var target = (RobotEntity)entity;
                Require(target.Anchor.Equals(anchor), "最终预览与实际部署锚点不一致。");
                Require(state.EnergyCoins == beforeBuy - price && state.FindSlot(0) == null,
                    "部署购买未原子清槽扣费。");
                Require(View(targetId).GetComponentInChildren<TextMesh>() == null &&
                    View(targetId).GetComponentInChildren<SpriteRenderer>().color == Color.white, "盘面应只用等级颜色标识。");
                checks.Add("形状中心部署、无Move的PointerUp最终预览/落地一致与盘面等级显示");

                // 命中目标的非锚点格，以验证合成目标按指针而非拖拽锚点判定。
                var lastCell = target.Definition.Cells[target.Definition.Cells.Count - 1];
                var hitCell = new BoardCoordinate(anchor.Column + lastCell.Column, anchor.Row + lastCell.Row);
                var hitPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(hitCell));
                var offerSlot = await FindOffer(context, configId, 1);
                var originalView = View(targetId);
                var oldQuality = target.QualityId;
                var nextPrice = state.FindSlot(offerSlot).Offer.Price;
                var beforeMerge = state.EnergyCoins;
                PickSlot(ui, offerSlot);
                await Drop(ui, hitPoint);
                Require(target.Level == 2 && target.Anchor.Equals(anchor) && View(targetId) == originalView && target.QualityId == oldQuality,
                    "商品直合未保留目标实例、锚点或品质。");
                Require(state.EnergyCoins == beforeMerge - nextPrice && originalView.MergeFeedbackCount == 1,
                    "商品直合扣费或一次成功反馈错误。");
                ui.RefreshView();
                Require(originalView.MergeFeedbackCount == 1, "普通刷新重复播放成功反馈。");
                checks.Add("未购商品直合、非锚点命中、原实例升级与一次反馈");

                // 场上拿起，失败回位，再移动原实例。
                var input = originalView.gameObject;
                ExecuteEvents.Execute(input, Pointer(hitPoint), ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(input, Pointer(hitPoint + Vector2.right), ExecuteEvents.beginDragHandler);
                Require(target.IsDragging && board.Model.GetOccupant(anchor) == null, "场上拿起未释放占格。");
                Require(originalView.GetComponentsInChildren<Collider2D>(true).All(c => !c.enabled), "场上拿起仍参与碰撞。");
                ui.Drag.Move(-1, PlacementPoint(camera, board, new BoardCoordinate(2, 5), target.Definition));
                // A prior valid preview must not be used after release outside the board without a Move.
                ui.Drag.Release(-1, new Vector2(-1000, -1000));
                await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle).Timeout(TimeSpan.FromSeconds(15));
                Require(!target.IsDragging && board.Model.GetOccupant(anchor) == targetId.ToString() && View(targetId) == originalView,
                    "最终非法位置误用之前有效预览，或未原位恢复同实例。");
                ExecuteEvents.Execute(input, Pointer(hitPoint), ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(input, Pointer(hitPoint + Vector2.right), ExecuteEvents.beginDragHandler);
                var movedAnchor = new BoardCoordinate(2, 5);
                var movedPointer = new BoardCoordinate(movedAnchor.Column + lastCell.Column, movedAnchor.Row + lastCell.Row);
                await ReleaseWithoutMove(ui, host, PlacementPoint(camera, board, movedAnchor, target.Definition), movedAnchor);
                Require(target.Anchor.Equals(movedAnchor) && target.Level == 2 && View(targetId) == originalView,
                    "中心重新放置改变了实例、等级或目标锚点。");
                checks.Add("场上回位、边缘抓取后中心重新放置");

                // 新商品直接部署，再验证两台场上机器人的免费合成。
                var secondAnchor = anchor;
                var secondSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, secondSlot);
                await Drop(ui, PlacementPoint(camera, board, secondAnchor, target.Definition));
                var secondId = int.Parse(board.Model.GetOccupant(secondAnchor));
                Require(context.Registry.TryGet(secondId, out entity), "第二台机器人未部署。");
                var second = (RobotEntity)entity;
                var upgradeSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, upgradeSlot);
                await Drop(ui, camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor)));
                Require(second.Level == 2, "第二台机器人未升级到匹配等级。");
                var secondView = View(secondId);
                ExecuteEvents.Execute(secondView.gameObject, Pointer(camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor))), ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(secondView.gameObject, Pointer((Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor)) + Vector2.right), ExecuteEvents.beginDragHandler);
                var mergeCoins = state.EnergyCoins;
                await Drop(ui, camera.WorldToScreenPoint(board.GetCellWorldPosition(movedAnchor)));
                Require(target.Level == 3 && target.Anchor.Equals(movedAnchor) && second.IsRemoved &&
                    state.EnergyCoins == mergeCoins && board.Model.GetOccupant(secondAnchor) == null,
                    "场上合成未完整释放来源、保留目标或出现额外扣费。");
                checks.Add("新商品直接部署、商品直合与场上来源免费合成");

                // 缓存资源也会异步Yield：取消提交后，晚到实例不得扣币或激活。
                var cancelSlot = await FindOffer(context, configId, 1);
                var cancelOffer = state.FindSlot(cancelSlot).Offer;
                var cancelCoins = state.EnergyCoins;
                var cancelAnchor = new BoardCoordinate(6, 5);
                var cancelPoint = PlacementPoint(camera, board, cancelAnchor, target.Definition);
                PickSlot(ui, cancelSlot);
                ui.Drag.Move(-1, cancelPoint);
                ui.Drag.Release(-1, cancelPoint);
                Require(ui.Drag.Phase == RobotDragPhase.Committing, "资源提交未覆盖异步等待。");
                ui.Drag.Cancel();
                await UniTask.Yield();
                await UniTask.Yield();
                Require(state.FindSlot(cancelSlot).Offer == cancelOffer && state.EnergyCoins == cancelCoins &&
                    board.Model.GetOccupant(cancelAnchor) == null && !context.IsBusy && ui.Drag.Phase == RobotDragPhase.Idle,
                    "取消加载后晚到回调扣币或留下实体。");
                checks.Add("实际缓存资源异步加载取消与晚到回调隔离");

                // 有效手势中终局，下一局没有来源、等级或旧手势污染。
                var endingSlot = state.Slots.FirstOrDefault(s => s.Offer != null);
                if (endingSlot == null) { context.TryRefresh(BattleSide.Player); endingSlot = state.FindSlot(0); }
                PickSlot(ui, endingSlot.SlotId);
                var endedContext = context;
                host.EndBattle();
                await UniTask.Yield();
                Require(endedContext.IsEnded && !endedContext.IsBusy && !host.IsReady &&
                    UnityEngine.Object.FindObjectsOfType<RobotView>().Length == 0, "终局未清理手势或视图。");
                await host.InitializeAsync();
                Require(host.IsReady && host.Context.BattleId != endedContext.BattleId &&
                    host.Context.GetState(BattleSide.Player).EnergyCoins == 20, "下一局没有重新初始化。");
                checks.Add("拖拽中终局清理与新局隔离");
                report.passed = true;
                report.details = "真实商店与盘面拖拽验收通过。";
            }
            catch (Exception e) { report.details = e.ToString(); Debug.LogError("[Robot Merge Verification] " + e); }
            report.checks = checks.ToArray();
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/RobotMergeVerification.json"), json);
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/BattleShopPlayVerification.json"), json);
            if (report.passed) Debug.Log("[Robot Merge Verification] PASS " + string.Join("；", checks));
        }
    }
}
