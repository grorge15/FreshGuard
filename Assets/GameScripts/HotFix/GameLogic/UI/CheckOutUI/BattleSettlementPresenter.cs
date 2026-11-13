using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>消费已经清理完毕的终局通知；不依赖可被场景销毁的战斗控制器。</summary>
    public sealed class BattleSettlementPresenter : Singleton<BattleSettlementPresenter>
    {
        private long _activeBattleId;
        private long _handledBattleId;
        private MatchEntrySnapshot _matchEntry;
        private int _generation;
        private CancellationTokenSource _cancellation;
        private bool _released;

        protected override void OnInit()
        {
            GameEvent.AddEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, OnRequested);
        }

        public void BeginBattle(long battleId, MatchEntrySnapshot matchEntry = null)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            if (_released) return;
            var previousId = _activeBattleId;
            _activeBattleId = battleId;
            _matchEntry = matchEntry;
            _handledBattleId = 0;
            _generation++;
            CancelPending();
            CloseOwnedWindow(previousId);
            _cancellation = new CancellationTokenSource();
        }

        /// <summary>主动离开只取消本局结算归属，迟到的终局通知不能再打开结算页。</summary>
        public void AbandonBattle(long battleId)
        {
            if (_released || battleId <= 0 || _activeBattleId != battleId) return;
            _activeBattleId = _handledBattleId = 0;
            _matchEntry = null;
            _generation++;
            CancelPending();
            CloseOwnedWindow(battleId);
        }

        private void OnRequested(BattleSettlementSnapshot snapshot)
        {
            if (_released || snapshot.BattleId <= 0 || snapshot.BattleId != _activeBattleId ||
                snapshot.BattleId == _handledBattleId) return;
            _handledBattleId = snapshot.BattleId;
            OpenAsync(snapshot, _matchEntry, _generation, _cancellation.Token).Forget();
        }

        private async UniTaskVoid OpenAsync(BattleSettlementSnapshot snapshot, MatchEntrySnapshot matchEntry,
            int generation, CancellationToken token)
        {
            try
            {
                var window = await GameModule.UI.ShowUIAsyncAwait<CheckOutUI>(snapshot).AttachExternalCancellation(token);
                if (_released || generation != _generation)
                {
                    CloseOwnedWindow(snapshot.BattleId);
                    return;
                }
                if (window == null || !window.IsPrepare || window.IsDestroyed)
                    throw new InvalidOperationException("CheckOutUI 加载失败。");
                window.BindMatch(matchEntry);
                window.BindReward(new BattleSettlementReward(ConfigSystem.Instance.Tables, GameApp.PlayerState,
                    snapshot.Result, GameApp.SavePlayerStateAsync), () => ReturnMainAsync(snapshot.BattleId, generation));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                CloseOwnedWindow(snapshot.BattleId);
                Debug.LogError("结算窗口加载失败，对局保持结束状态：" + exception.Message);
            }
        }

        private async UniTask ReturnMainAsync(long battleId, int generation)
        {
            if (_released || _activeBattleId != battleId || _generation != generation) return;
            var menu = await GameModule.UI.ShowUIAsyncAwait<MainMenuUI>();
            if (_released || _activeBattleId != battleId || _generation != generation) return;
            if (menu == null || menu.IsDestroyed) throw new InvalidOperationException("主菜单加载失败，请重试。");
            _activeBattleId = 0;
            _matchEntry = null;
            _generation++;
            CancelPending();
            CloseOwnedWindow(battleId);
            // 商店失败由主菜单自行恢复，不回滚奖励或重新打开结算页。
            await menu.ShowMysteriousStorePreviewAsync(battleId);
        }

        private static void CloseOwnedWindow(long battleId)
        {
            if (battleId <= 0 || !UIModule.IsValid || UIModule.UIRoot == null) return;
            var window = GameModule.UI.GetUI<CheckOutUI>();
            if (window != null && window.UserData is BattleSettlementSnapshot data && data.BattleId == battleId)
                GameModule.UI.CloseUI<CheckOutUI>();
        }

        private void CancelPending()
        {
            var cancellation = _cancellation;
            _cancellation = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
        }

        protected override void OnRelease()
        {
            _released = true;
            _generation++;
            GameEvent.RemoveEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, OnRequested);
            CancelPending();
            CloseOwnedWindow(_activeBattleId);
            _activeBattleId = _handledBattleId = 0;
            _matchEntry = null;
        }
    }
}
