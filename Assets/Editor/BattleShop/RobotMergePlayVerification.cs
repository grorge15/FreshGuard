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
        private static GameObject Slot(BattleShopUI ui, int id) => ui.transform.Find("m_rect_ShopPanel/m_tf_Offers/Slot_" + id).gameObject;

        private static void PickSlot(BattleShopUI ui, int id)
        {
            var node = Slot(ui, id);
            ExecuteEvents.Execute(node, Pointer(Point(node.transform)), ExecuteEvents.pointerDownHandler);
            ui.Drag.Advance(Time.unscaledTime + 0.21f);
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
            foreach (var test in new[] { "reward-cancel", "close-restart", "release-threshold", "reward-restart" })
            {
                try
                {
                    host.EndBattle();
                    await host.InitializeAsync();
                    host.SetFrozen(true);
                    var ui = host.ShopWindow;
                    var context = host.Context;
                    var state = context.GetState(BattleSide.Player);
                    if (test == "release-threshold")
                    {
                        var storage = ui.transform.Find("m_rect_ShopPanel/m_item_Storage");
                        var price = state.Slots[0].Offer.Price;
                        ui.PressSlot(0, -1, Point(storage), Time.unscaledTime - 0.21f);
                        ui.Drag.Release(-1, Point(storage));
                        Require(state.StorageInstanceId.HasValue && state.EnergyCoins == 20 - price,
                            "超过长按阈值、Update前松手被误判为短按。");
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

                var first = state.Slots[0].Offer;
                var slot = Slot(ui, 0);
                var p = Pointer(Point(slot.transform));
                ExecuteEvents.Execute(slot, p, ExecuteEvents.pointerDownHandler);
                Require(state.Slots[0].Offer == first && state.EnergyCoins == 20 && !state.Slots[0].InstanceId.HasValue,
                    "按下提前购买扣币。");
                ExecuteEvents.Execute(slot, p, ExecuteEvents.pointerUpHandler);
                Require(ui.Drag.Phase == RobotDragPhase.Idle && state.Slots[0].Offer == first, "短按未保留商品。");
                PickSlot(ui, 0);
                Require(state.EnergyCoins == 20 && context.IsBusy, "长按提前购买或没有交互锁。");
                Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Busy, "拖拽中刷新未拦截。");
                Require(Mathf.Approximately(host.Bootstrap.PlayerBall.EffectiveSpeed, host.Bootstrap.PlayerBall.Speed * 0.7f) &&
                    Mathf.Approximately(host.Bootstrap.OpponentBall.EffectiveSpeed, host.Bootstrap.OpponentBall.Speed), "未正确按方减速。");
                await Drop(ui, new Vector2(-1000, -1000));
                Require(state.EnergyCoins == 20 && state.Slots[0].Offer == first && !context.IsBusy, "非法落点扣币或丢失商品。");
                Require(Mathf.Approximately(host.Bootstrap.PlayerBall.EffectiveSpeed, host.Bootstrap.PlayerBall.Speed), "失败后未恢复球速。");
                checks.Add("真实指针短按、长按、非法回位、交互锁与己方球速");

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
                var dropPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(anchor));
                var beforeBuy = state.EnergyCoins;
                var price = state.Slots[0].Offer.Price;
                var configId = state.Slots[0].Offer.RobotId;
                PickSlot(ui, 0);
                await Drop(ui, dropPoint);
                var targetId = int.Parse(board.Model.GetOccupant(anchor));
                Entity entity;
                Require(context.Registry.TryGet(targetId, out entity), "合法购买部署没有实体。");
                var target = (RobotEntity)entity;
                Require(state.EnergyCoins == beforeBuy - price && state.Slots[0].Offer == null && !state.Slots[0].InstanceId.HasValue,
                    "部署购买未原子清槽扣费。");
                Require(View(targetId).GetComponentInChildren<TextMesh>().text == "Lv1", "盘面等级标签未显示。");
                checks.Add("合法商品购买部署与盘面等级显示");

                // 命中目标的非锚点格，以验证合成目标按指针而非拖拽锚点判定。
                var lastCell = target.Definition.Cells[target.Definition.Cells.Count - 1];
                var hitCell = new BoardCoordinate(anchor.Column + lastCell.Column, anchor.Row + lastCell.Row);
                var hitPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(hitCell));
                var offerSlot = await FindOffer(context, configId, 1);
                var originalView = View(targetId);
                var oldQuality = target.QualityId;
                var nextPrice = state.Slots[offerSlot].Offer.Price;
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

                // 场上拿起，失败回位，再移动原实例，最后进入空存储位。
                var input = originalView.gameObject;
                ExecuteEvents.Execute(input, Pointer(hitPoint), ExecuteEvents.pointerDownHandler);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                Require(target.IsDragging && board.Model.GetOccupant(anchor) == null, "场上拿起未释放占格。");
                Require(originalView.GetComponentsInChildren<Collider2D>(true).All(c => !c.enabled), "场上拿起仍参与碰撞。");
                await Drop(ui, new Vector2(-1000, -1000));
                Require(!target.IsDragging && board.Model.GetOccupant(anchor) == targetId.ToString() && View(targetId) == originalView,
                    "场上失败未原位恢复同实例。");
                ExecuteEvents.Execute(input, Pointer(hitPoint), ExecuteEvents.pointerDownHandler);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                var movedAnchor = new BoardCoordinate(2, 5);
                var movedPointer = new BoardCoordinate(movedAnchor.Column + lastCell.Column, movedAnchor.Row + lastCell.Row);
                await Drop(ui, camera.WorldToScreenPoint(board.GetCellWorldPosition(movedPointer)));
                Require(target.Anchor.Equals(movedAnchor) && target.Level == 2 && View(targetId) == originalView,
                    "重新放置改变了实例、等级或抓取偏移。");
                var movedHit = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(movedPointer));
                ExecuteEvents.Execute(input, Pointer(movedHit), ExecuteEvents.pointerDownHandler);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                var storageNode = ui.transform.Find("m_rect_ShopPanel/m_item_Storage");
                await Drop(ui, Point(storageNode));
                Require(state.StorageInstanceId == targetId && target.Location == RobotLocation.Storage && !target.CanParticipate,
                    "场上机器人未进入空存储位。");
                checks.Add("场上回位、抓取偏移重新放置及空存储位");

                // 新商品与已存机器人交换；原机器人以已购状态进入商品原槽。
                var swapSlot = await FindOffer(context, configId, 1);
                var swapPrice = state.Slots[swapSlot].Offer.Price;
                var swapCoins = state.EnergyCoins;
                PickSlot(ui, swapSlot);
                await Drop(ui, Point(storageNode));
                Require(state.Slots[swapSlot].InstanceId == targetId && state.Slots[swapSlot].IsPurchased &&
                    state.EnergyCoins == swapCoins - swapPrice, "未购商品存储交换错误。");
                Require(Slot(ui, swapSlot).transform.Find("m_text_Price").GetComponent<Text>().text == "已购买", "已购状态不可辨识。");
                // 已购槽机器人原等级为2，再部署无需收费。
                var paidCoins = state.EnergyCoins;
                PickSlot(ui, swapSlot);
                await Drop(ui, dropPoint);
                Require(state.EnergyCoins == paidCoins && target.Location == RobotLocation.Board && target.Level == 2,
                    "已购来源回场重复收费或丢失等级。");
                checks.Add("商品存储交换、已购槽标识与免费回场");

                // 临时存储机器人回场后形成另一台Lv1，再准备场上来源合成。
                var storedId = state.StorageInstanceId.Value;
                ui.PressStorage(-1, Point(storageNode), Time.unscaledTime);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                var secondAnchor = new BoardCoordinate(2, 5);
                await Drop(ui, camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor)));
                Require(!state.StorageInstanceId.HasValue && context.Registry.TryGet(storedId, out entity), "暂存来源回场失败。");
                var second = (RobotEntity)entity;
                var upgradeSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, upgradeSlot);
                await Drop(ui, camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor)));
                Require(second.Level == 2, "第二台机器人未升级到匹配等级。");
                // 两台均Lv2：来源释放所有格，目标保持原位升级Lv3。
                var secondView = View(storedId);
                ExecuteEvents.Execute(secondView.gameObject, Pointer(camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor))), ExecuteEvents.pointerDownHandler);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                var mergeCoins = state.EnergyCoins;
                await Drop(ui, dropPoint);
                Require(target.Level == 3 && !context.Registry.TryGet(storedId, out entity) && state.EnergyCoins == mergeCoins &&
                    board.Model.GetOccupant(secondAnchor) == null && View(targetId) == originalView, "场上来源合成没有完整消耗或重复收费。");
                checks.Add("场上来源合成、完整占格释放与零额外费用");

                // 独立Lv1目标覆盖存储来源与已购槽来源直接合成。
                var storageTargetSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, storageTargetSlot);
                var secondPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(secondAnchor));
                await Drop(ui, secondPoint);
                var storageTargetId = int.Parse(board.Model.GetOccupant(secondAnchor));
                Require(context.Registry.TryGet(storageTargetId, out entity), "存储直合验收目标未部署。");
                var storageTarget = (RobotEntity)entity;
                var storeSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, storeSlot);
                await Drop(ui, Point(storageNode));
                var storageSourceId = state.StorageInstanceId.Value;
                // 场上来源不得向满存储交换，必须完整回位。
                ExecuteEvents.Execute(originalView.gameObject, Pointer(dropPoint), ExecuteEvents.pointerDownHandler);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                await Drop(ui, Point(storageNode));
                Require(target.Anchor.Equals(anchor) && !target.IsDragging && board.Model.GetOccupant(anchor) == targetId.ToString() &&
                    state.StorageInstanceId == storageSourceId && !context.IsBusy, "满存储失败未恢复场上原实例。");
                var freeMergeCoins = state.EnergyCoins;
                ui.PressStorage(-1, Point(storageNode), Time.unscaledTime);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                await Drop(ui, secondPoint);
                Require(storageTarget.Level == 2 && !state.StorageInstanceId.HasValue &&
                    !context.Registry.TryGet(storageSourceId, out entity) && state.EnergyCoins == freeMergeCoins,
                    "存储来源直合未消费、清槽或重复收费。");
                checks.Add("满存储场上回位与存储来源免费直合");

                // 两次存储购买交换生成已购槽Lv1，再对另一台Lv1直接合成。
                var thirdAnchor = new BoardCoordinate(6, 2);
                var thirdPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(thirdAnchor));
                var thirdSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, thirdSlot);
                await Drop(ui, thirdPoint);
                var purchasedTargetId = int.Parse(board.Model.GetOccupant(thirdAnchor));
                Require(context.Registry.TryGet(purchasedTargetId, out entity), "已购直合验收目标未部署。");
                var purchasedTarget = (RobotEntity)entity;
                storeSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, storeSlot);
                await Drop(ui, Point(storageNode));
                var purchasedSourceId = state.StorageInstanceId.Value;
                var purchasedSlot = await FindOffer(context, configId, 1);
                PickSlot(ui, purchasedSlot);
                await Drop(ui, Point(storageNode));
                Require(state.Slots[purchasedSlot].InstanceId == purchasedSourceId, "已购来源验收交换失败。");
                freeMergeCoins = state.EnergyCoins;
                PickSlot(ui, purchasedSlot);
                await Drop(ui, thirdPoint);
                Require(purchasedTarget.Level == 2 && !state.Slots[purchasedSlot].InstanceId.HasValue &&
                    !context.Registry.TryGet(purchasedSourceId, out entity) && state.EnergyCoins == freeMergeCoins,
                    "已购槽直合未消费、清槽或重复收费。");
                checks.Add("已购商品槽来源免费直合");

                // 缓存资源也会异步Yield：取消提交后，晚到实例不得扣币或激活。
                var cancelSlot = await FindOffer(context, configId, 1);
                var cancelOffer = state.Slots[cancelSlot].Offer;
                var cancelCoins = state.EnergyCoins;
                var cancelAnchor = new BoardCoordinate(6, 5);
                var cancelPoint = (Vector2)camera.WorldToScreenPoint(board.GetCellWorldPosition(cancelAnchor));
                PickSlot(ui, cancelSlot);
                ui.Drag.Move(-1, cancelPoint);
                ui.Drag.Release(-1, cancelPoint);
                Require(ui.Drag.Phase == RobotDragPhase.Committing, "资源提交未覆盖异步等待。");
                ui.Drag.Cancel();
                await UniTask.Yield();
                await UniTask.Yield();
                Require(state.Slots[cancelSlot].Offer == cancelOffer && state.EnergyCoins == cancelCoins &&
                    board.Model.GetOccupant(cancelAnchor) == null && !context.IsBusy && ui.Drag.Phase == RobotDragPhase.Idle,
                    "取消加载后晚到回调扣币或留下实体。");
                checks.Add("实际缓存资源异步加载取消与晚到回调隔离");

                // 有效手势中终局，下一局没有来源、等级或旧手势污染。
                var endingSlot = state.Slots.FirstOrDefault(s => s.Offer != null);
                if (endingSlot == null) { context.TryRefresh(BattleSide.Player); endingSlot = state.Slots[0]; }
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
