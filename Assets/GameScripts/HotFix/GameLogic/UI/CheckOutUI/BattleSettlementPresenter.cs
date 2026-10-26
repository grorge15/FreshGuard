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
        private int _generation;
        private CancellationTokenSource _cancellation;
        private bool _released;

        protected override void OnInit()
        {
            GameEvent.AddEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, OnRequested);
        }

        public void BeginBattle(long battleId)
        {
            if (battleId <= 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            if (_released) return;
            var previousId = _activeBattleId;
            _activeBattleId = battleId;
            _handledBattleId = 0;
            _generation++;
            CancelPending();
            CloseOwnedWindow(previousId);
            _cancellation = new CancellationTokenSource();
        }

        private void OnRequested(BattleSettlementSnapshot snapshot)
        {
            if (_released || snapshot.BattleId <= 0 || snapshot.BattleId != _activeBattleId ||
                snapshot.BattleId == _handledBattleId) return;
            _handledBattleId = snapshot.BattleId;
            OpenAsync(snapshot, _generation, _cancellation.Token).Forget();
        }

        private async UniTaskVoid OpenAsync(BattleSettlementSnapshot snapshot, int generation, CancellationToken token)
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
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                CloseOwnedWindow(snapshot.BattleId);
                Debug.LogError("结算窗口加载失败，对局保持结束状态：" + exception.Message);
            }
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
        }
    }
}
