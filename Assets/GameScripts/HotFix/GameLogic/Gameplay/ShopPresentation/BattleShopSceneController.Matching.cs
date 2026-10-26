using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleShopSceneController
    {
        private MatchEntrySnapshot _matchEntry;
        private bool _matchReleased = true;
        private float _operationalWait;
        private bool _checkingOperational;
        public bool IsWaitingForMatch => _matchEntry != null && !_matchReleased;
        public string MatchEntryId => _matchEntry?.EntryId;

        public UniTask PrepareMatchAsync(MatchEntrySnapshot entry, IProgress<BattleLoadingProgress> progress)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (Context != null || _initializing) throw new InvalidOperationException("战斗仍占用旧对局，请先清理。");
            _matchEntry = entry;
            _matchReleased = false;
            return InitializeAsync(progress);
        }
        public bool ReleaseMatch(string entryId)
        {
            if (_matchEntry?.EntryId != entryId || _matchReleased || !IsReady) return false;
            _matchReleased = true;
            _checkingOperational = true;
            _operationalWait = 0;
            BattleSettlementPresenter.Instance.BeginBattle(Context.BattleId);
            return true;
        }
        public bool CancelMatch(string entryId)
        {
            if (_matchEntry?.EntryId != entryId) return false;
            // 匹配失败/取消没有塔结算事实，不能走主动退出或奖励入口。
            _pendingTowerSettlement = null;
            CloseOwnedWindow();
            Cleanup();
            return true;
        }

        private void CheckCombatOperational(float delta)
        {
            if (!_checkingOperational || !_running) return;
            // 匹配先放行开局，运行态确认后再统一开放双方交互，AI延迟也从此时开始。
            if (IsCombatOperational && TryOpenRobotInteraction(Context.BattleId))
            {
                _checkingOperational = false;
                return;
            }
            _operationalWait += delta;
            if (_operationalWait < 30f) return;
            _checkingOperational = false;
            InitializationError = new InvalidOperationException("开战后30秒，能源球反弹、首批敌人或战斗交互界面仍未就绪。");
            Debug.LogError(InitializationError.Message, this);
            // 已开战故障冻结并留存证据，不走匹配返还或伪造胜负。
            SetFrozen(true);
        }
    }
}
