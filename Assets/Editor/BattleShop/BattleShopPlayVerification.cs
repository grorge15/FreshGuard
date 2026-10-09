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
            await RobotMergePlayVerification.VerifyAsync();
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
                try { ui.Drag.Press(robot.InstanceId, -1, Vector2.zero, Time.unscaledTime); ui.Drag.Advance(Time.unscaledTime + 0.21f); began = ui.Drag.Phase != RobotDragPhase.Idle; }
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
