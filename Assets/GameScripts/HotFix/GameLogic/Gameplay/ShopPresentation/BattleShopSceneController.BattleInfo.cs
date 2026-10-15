using System;
using TEngine;

namespace GameLogic
{
    public sealed partial class BattleShopSceneController
    {
        private BattleInfoUI _battleInfoWindow;
        private long _lastProgressBattleId;
        private int _lastProgressWave;
        private double _lastProgressSecond = -1;
        public BattleInfoUI InfoWindow => _battleInfoWindow;

        private BattleProgressSnapshot Progress => new BattleProgressSnapshot(Context.BattleId,
            _enemies.CurrentWave, _enemies.ElapsedSeconds);

        private void PublishBattleProgress(bool force = false)
        {
            if (Context == null || _enemies == null) return;
            var progress = Progress;
            var second = Math.Floor(progress.ElapsedSeconds);
            if (!force && _lastProgressBattleId == progress.BattleId && _lastProgressWave == progress.CurrentWave &&
                _lastProgressSecond == second) return;
            _lastProgressBattleId = progress.BattleId;
            _lastProgressWave = progress.CurrentWave;
            _lastProgressSecond = second;
            GameEvent.Send(BattleProgressEvents.Changed, progress);
        }

        private void LateUpdate()
        {
            if (CombatWindow == null || CombatWindow.IsDestroyed || _battleInfoWindow == null || Context == null ||
                _battleInfoWindow.BattleId != Context.BattleId) return;
            PublishBattleProgress();
        }
    }
}
