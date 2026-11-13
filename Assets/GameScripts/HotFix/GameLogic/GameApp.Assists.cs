using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;

public partial class GameApp
{
    private static readonly SemaphoreSlim PlayerSaveQueue = new SemaphoreSlim(1, 1);
    private static int _pendingPlayerSaves;
    public static bool IsPlayerSavePending => _pendingPlayerSaves > 0;

    private static async UniTask SaveCurrentPlayerStateAsync()
    {
        if (_playerStore == null || _playerRecord == null || _playerState == null) return;
        _pendingPlayerSaves++;
        await PlayerSaveQueue.WaitAsync();
        try
        {
            if (_playerStore == null || _playerRecord == null || _playerState == null) return;
            var record = _playerRecord;
            var state = _playerState;
            var next = record.Copy();
            next.Capture(state);
            await _playerStore.SaveAsync(next);
            // 匹配阶段可在I/O期间变更；仅同步玩家字段，保留当前记录的身份与最新阶段。
            record.Capture(state);
        }
        finally { PlayerSaveQueue.Release(); _pendingPlayerSaves--; }
    }

    /// <summary>只持久化候选；调用方在成功返回后向原玩家对象发布状态。</summary>
    public static async UniTask SavePlayerCandidateAsync(MainMenuPlayerState candidate)
    {
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (_playerStore == null || _playerRecord == null) throw new InvalidOperationException("玩家存档尚未就绪。");
        _pendingPlayerSaves++;
        await PlayerSaveQueue.WaitAsync();
        try
        {
            var record = _playerRecord;
            var next = record.Copy();
            next.Capture(candidate);
            await _playerStore.SaveAsync(next);
            record.Capture(candidate);
        }
        finally { PlayerSaveQueue.Release(); _pendingPlayerSaves--; }
    }

    public static async UniTask<bool> PruneExpiredAssistsAsync()
    {
        if (IsPlayerSavePending || _playerState == null) return false;
        var candidate = _playerState.Clone();
        if (!candidate.PruneExpiredAssists(DateTimeOffset.UtcNow)) return false;
        await SavePlayerCandidateAsync(candidate);
        _playerState.ApplyFrom(candidate);
        return true;
    }
}
