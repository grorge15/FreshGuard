using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TEngine;

namespace FreshGuard.Editor
{
    /// <summary>真实运行窗口的可重复验收，不向生产组件增加测试专用接口。</summary>
    public static class BattleShopPlayVerification
    {
        [Serializable]
        public sealed class Report
        {
            public bool passed;
            public string details;
            public int initialCoins;
            public int afterPurchase;
            public bool shortPressStayed;
            public bool longPressIconOnly;
            public bool invalidDropReturned;
            public bool deployedOriginalEntity;
            public bool storageSwap;
            public bool refreshDiscardedOwned;
            public bool opponentWithoutUi;
            public bool cancelledLoadStayed;
            public bool singleEventSystem;
            public bool stableSlotLayout;
        }

        [MenuItem("FreshGuard/Shop/Verify Running Shop")]
        public static void Verify() { VerifyAsync().Forget(); }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static async UniTaskVoid VerifyAsync()
        {
            var report = new Report();
            try
            {
                Require(Application.isPlaying, "需要从启动场景进入 PlayMode。");
                var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                Require(host != null && host.IsReady, "商店未初始化。");
                host.EndBattle();
                await host.InitializeAsync();
                host.SetFrozen(true);
                var context = host.Context;
                var ui = await GameModule.UI.ShowUIAsyncAwait<BattleShopUI>(context, host);
                Require(ui != null && ui.IsPrepare, "TEngine 窗口未就绪。");
                Canvas.ForceUpdateCanvases();
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                report.singleEventSystem = UnityEngine.Object.FindObjectsOfType<EventSystem>().Length == 1;
                Require(report.singleEventSystem, "存在重复的活动 EventSystem。");
                var state = context.GetState(BattleSide.Player);
                report.initialCoins = state.EnergyCoins;
                Require(state.EnergyCoins == 20, "验收需新的一局，开局余额不是20。");
                var source = ui.transform.Find("m_rect_ShopPanel/m_tf_Offers/Slot_0").gameObject;
                var icon = source.transform.Find("m_img_RobotIcon").GetComponent<Image>();
                var point = RectTransformUtility.WorldToScreenPoint(GameModule.UI.UICamera, source.transform.position);
                var pointer = new PointerEventData(EventSystem.current) { pointerId = -1, position = point, button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Require(hits.Count > 0 && hits[0].gameObject == source, "商品槽被其他UI阻挡或无法接收射线。");
                var price = state.Slots[0].Offer.Price;
                ExecuteEvents.Execute(source, pointer, ExecuteEvents.pointerDownHandler);
                var instanceId = state.Slots[0].InstanceId.Value;
                report.afterPurchase = state.EnergyCoins;
                Require(report.afterPurchase == 20 - price, "按下未立即购买扣币。");
                ExecuteEvents.Execute(source, pointer, ExecuteEvents.pointerUpHandler);
                report.shortPressStayed = state.Slots[0].InstanceId == instanceId && ui.Drag.Phase == RobotDragPhase.Idle;
                Require(report.shortPressStayed, "短按未保留已购槽。");
                var now = Time.unscaledTime;
                ui.PressSlot(0, -1, point, now);
                ui.Drag.Advance(now + 0.199f);
                Require(ui.Drag.Phase == RobotDragPhase.Pressed, "0.2秒前进入拖拽。");
                ui.Drag.Advance(now + 0.201f);
                var ghost = ui.transform.Find("m_item_DragGhost").gameObject;
                report.longPressIconOnly = ui.Drag.Phase == RobotDragPhase.Dragging && !icon.enabled &&
                    ghost.activeSelf && ghost.GetComponentsInChildren<SpriteRenderer>(true).Length == 0 &&
                    !ghost.GetComponent<Image>().raycastTarget;
                Require(report.longPressIconOnly, "拖拽图标或来源隐藏不正确。");
                var beforeRefresh = state.EnergyCoins;
                Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Busy, "拖拽时刷新未拦截。");
                ui.Drag.Release(-1, new Vector2(-1000, -1000));
                await UniTask.Yield();
                report.invalidDropReturned = state.Slots[0].InstanceId == instanceId && icon.enabled &&
                    state.EnergyCoins == beforeRefresh && !ghost.activeSelf;
                Require(report.invalidDropReturned, "非法落点未返回原槽或发生退款。");
                var player = GameObject.Find("PlayerBoardWorldRoot").GetComponentInChildren<PlacementBoardView>();
                var camera = Camera.main;
                var dropPoint = (Vector2)camera.WorldToScreenPoint(player.GetCellWorldPosition(new BoardCoordinate(4, 3)));
                Entity cachedEntity;
                context.Registry.TryGet(instanceId, out cachedEntity);
                // Warm the normal asset cache, whose Instantiate path yields one frame.
                var warm = new GameObject("VerificationWarmup");
                warm.SetActive(false);
                var cached = await GameModule.Resource.LoadGameObjectAsync(((RobotEntity)cachedEntity).Configuration.RobotPrefab, warm.transform);
                UnityEngine.Object.Destroy(warm);
                Require(cached != null, "验收预加载失败。");
                ui.PressSlot(0, -1, point, Time.unscaledTime);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                ui.Drag.Release(-1, dropPoint);
                Require(ui.Drag.Phase == RobotDragPhase.Committing, "验收未进入异步部署。");
                ui.Drag.Cancel();
                // Re-press before the old load returns: IsDragging is true again.
                ui.PressSlot(0, -1, point, Time.unscaledTime);
                await UniTask.Yield();
                await UniTask.Yield();
                report.cancelledLoadStayed = state.Slots[0].InstanceId == instanceId &&
                    player.Model.GetOccupant(new BoardCoordinate(4, 3)) == null;
                Require(report.cancelledLoadStayed, "取消的旧加载在再次按下时仍部署了机器人。");
                ui.Drag.Cancel();
                ui.PressSlot(0, -1, point, Time.unscaledTime);
                ui.Drag.Advance(Time.unscaledTime + 0.21f);
                ui.Drag.Move(-1, dropPoint);
                ui.Drag.Release(-1, dropPoint);
                await UniTask.WaitUntil(() => ui.Drag.Phase == RobotDragPhase.Idle,
                    cancellationToken: host.GetCancellationTokenOnDestroy()).Timeout(TimeSpan.FromSeconds(15));
                Entity entity;
                var robot = context.Registry.TryGet(instanceId, out entity) ? entity as RobotEntity : null;
                report.deployedOriginalEntity = robot != null && robot.Location == RobotLocation.Board &&
                    player.Model.GetOccupant(new BoardCoordinate(4, 3)) == instanceId.ToString() &&
                    state.EnergyCoins == beforeRefresh && state.Slots[0].InstanceId == null;
                Require(report.deployedOriginalEntity, "部署未复用已购实体或再次扣费。");
                report.stableSlotLayout = source.activeSelf &&
                    ui.transform.Find("m_rect_ShopPanel/m_tf_Offers").childCount == 3 && !icon.enabled;
                Require(report.stableSlotLayout, "空商品槽消失或布局移动。");
                // Use the same public logic API without any opponent window.
                var opponent = context.GetState(BattleSide.Opponent);
                RobotEntity first, second;
                Require(context.TryPurchase(BattleSide.Opponent, 0, opponent.Slots[0].Offer.OfferId, out first) == ShopOperationResult.Success,
                    "敌方购买失败。");
                Require(context.TryStoreOrSwap(BattleSide.Opponent, first.InstanceId) == ShopOperationResult.Success, "敌方暂存失败。");
                context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.Boss, 991);
                Require(context.TryPurchase(BattleSide.Opponent, 1, opponent.Slots[1].Offer.OfferId, out second) == ShopOperationResult.Success,
                    "敌方第二次购买失败。");
                Require(context.TryStoreOrSwap(BattleSide.Opponent, second.InstanceId) == ShopOperationResult.Success, "暂存交换失败。");
                report.storageSwap = opponent.StorageInstanceId == second.InstanceId && opponent.Slots[1].InstanceId == first.InstanceId;
                Require(report.storageSwap, "已购机器人未交换回来源商品槽。");
                Require(context.TryRefresh(BattleSide.Opponent) == ShopOperationResult.Success, "敌方刷新失败。");
                report.refreshDiscardedOwned = !context.Registry.TryGet(first.InstanceId, out entity) &&
                    context.Registry.TryGet(second.InstanceId, out entity) && opponent.StorageInstanceId == second.InstanceId;
                Require(report.refreshDiscardedOwned, "刷新未清除已购槽或误删暂存。");
                report.opponentWithoutUi = GameObject.FindObjectsOfType<RobotDragController>().Length == 1;
                Require(report.opponentWithoutUi, "敌方创建了额外UI。");
                report.passed = true;
                report.details = "真实TEngine窗口：按下扣币、短按保留、0.2秒拖拽、非法落点返回、原实体部署、双方独立、暂存交换及刷新清除通过。";
                // Leave the tested board and shop visible for a screenshot; caller may end the battle afterwards.
            }
            catch (Exception e) { report.details = e.ToString(); Debug.LogError("[BattleShop Verification] " + report.details); }
            finally
            {
                File.WriteAllText(Path.Combine(Application.dataPath, "../Library/BattleShopVerification.json"), JsonUtility.ToJson(report, true));
                if (report.passed) Debug.Log("[BattleShop Verification] PASS " + report.details);
            }
        }

        [MenuItem("FreshGuard/Shop/Verify Lifecycle")]
        public static void VerifyLifecycle() { VerifyLifecycleAsync().Forget(); }

        private static async UniTaskVoid VerifyLifecycleAsync()
        {
            var passed = false;
            string details;
            try
            {
                Require(Application.isPlaying, "需要 PlayMode。");
                var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                Require(host != null && host.IsReady, "商店未就绪。");
                host.EndBattle();
                await host.InitializeAsync();
                host.SetFrozen(true);
                var context = host.Context;
                var state = context.GetState(BattleSide.Player);
                // Verify the real event adapter, including duplicate facts.
                var reward = new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.Boss, 70001);
                var beforeReward = state.EnergyCoins;
                GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, reward);
                GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, reward);
                Require(state.EnergyCoins == beforeReward + 50, "真实奖励适配器漏发或重复发币。");
                while (state.EnergyCoins >= context.RefreshPrice)
                    Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Success, "刷新消耗失败。");
                var ui = GameModule.UI.GetUI<BattleShopUI>();
                var refresh = ui.transform.Find("m_rect_ShopPanel/m_btn_Refresh").gameObject;
                var coins = ui.transform.Find("m_rect_ShopPanel/m_text_EnergyCoins").GetComponent<Text>();
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                var beforeFailure = state.EnergyCoins;
                ExecuteEvents.Execute(refresh, pointer, ExecuteEvents.pointerClickHandler);
                var message = ui.transform.Find("m_rect_ShopPanel/m_text_Message").GetComponent<Text>();
                Require(message.text == "能源币不足" && state.EnergyCoins == beforeFailure, "不足时仍扣币或未反馈。");
                var sawFlash = false;
                var flashStarted = Time.unscaledTime;
                while (Time.unscaledTime - flashStarted < 0.5f)
                {
                    sawFlash |= coins.color != Color.white;
                    await UniTask.Yield();
                }
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                Require(sawFlash && coins.color == Color.white, "0.5秒不足闪烁未结束。");
                GameEvent.Send(BattleRewardAdapter.REWARD_EVENT,
                    new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.Boss, 70002));
                RobotEntity robot;
                Require(context.TryPurchase(BattleSide.Player, 0, state.Slots[0].Offer.OfferId, out robot) == ShopOperationResult.Success,
                    "终局验收购买失败。");
                Action<int> cancelOnBegin = side => { if (robot.IsDragging) ui.Drag.Cancel(); };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, cancelOnBegin);
                bool began;
                try { began = ui.Drag.Press(robot.InstanceId, -1, Vector2.zero, Time.unscaledTime); }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, cancelOnBegin); }
                Require(!began && !context.IsBusy && ui.Drag.Phase == RobotDragPhase.Idle,
                    "交互通知内取消被Press覆盖。");
                Require(context.TryBeginInteraction(BattleSide.Player, robot.InstanceId), "终局验收交互失败。");
                var player = GameObject.Find("PlayerBoardWorldRoot").GetComponentInChildren<PlacementBoardView>();
                Action<int> endOnDeploy = side =>
                {
                    if (robot.Location == RobotLocation.Board) host.EndBattle();
                };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, endOnDeploy);
                ShopOperationResult result;
                try
                {
                    result = await host.DeployRobotAsync(robot.InstanceId,
                        new BoardPlacementTarget(player, new BoardCoordinate(4, 3)), host.GetCancellationTokenOnDestroy());
                }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, endOnDeploy); }
                await UniTask.Yield();
                Require(result == ShopOperationResult.BattleEnded && context.IsEnded && !host.IsReady,
                    "部署通知内终局未阻止晚到视图。");
                Require(UnityEngine.Object.FindObjectsOfType<RobotView>().Length == 0 &&
                    player.Model.GetOccupant(new BoardCoordinate(4, 3)) == null && GameModule.UI.GetUI<BattleShopUI>() == null,
                    "终局留下视图、占格或窗口。");
                Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.BattleEnded, "终局仍可刷新。");
                host.EndBattle();
                await host.InitializeAsync();
                Require(host.IsReady && host.Context.BattleId != context.BattleId &&
                    host.Context.GetState(BattleSide.Player).EnergyCoins == 20, "同场景下一局无法重新初始化。");
                GameEvent.Send(BattleRewardAdapter.REWARD_EVENT, reward);
                Require(host.Context.GetState(BattleSide.Player).EnergyCoins == 20, "旧局奖励污染下一局。");
                var last = host.Context;
                var restarted = false;
                Action<int> restartInsideEnd = side =>
                {
                    if (!last.IsEnded || restarted) return;
                    restarted = true;
                    host.EndBattle();
                    host.InitializeAsync().Forget();
                };
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, restartInsideEnd);
                try
                {
                    host.EndBattle();
                    await UniTask.WaitUntil(() => host.IsReady && GameModule.UI.GetUI<BattleShopUI>()?.IsPrepare == true)
                        .Timeout(TimeSpan.FromSeconds(15));
                    Require(host.Context.BattleId != last.BattleId && host.transform.Find("PreparedRobotViews") != null,
                        "清理通知内启动的新局被外层Cleanup销毁。");
                }
                finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, restartInsideEnd); }
                passed = true;
                details = "奖励事实去重、实际刷新按钮余额不足及0.5秒闪烁、部署通知内终局清理、终局拒绝操作、同场景重启及旧局消息隔离通过。";
            }
            catch (Exception e) { details = e.ToString(); Debug.LogError("[BattleShop Lifecycle] " + details); }
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/BattleShopLifecycleVerification.json"),
                "{\"passed\":" + (passed ? "true" : "false") + ",\"details\":\"" +
                details.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"}");
            if (passed) Debug.Log("[BattleShop Lifecycle] PASS " + details);
        }
    }
}
