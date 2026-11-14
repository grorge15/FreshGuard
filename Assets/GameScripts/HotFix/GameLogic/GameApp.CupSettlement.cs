using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEngine;

public partial class GameApp
{
    private static async UniTask PrepareBattleSettlementAsync(MatchEntrySnapshot entry)
    {
        _playerRecord.StartBattle(PlayerState, entry.EntryId,
            BattleRewardRules.FromTables(ConfigSystem.Instance.Tables, entry.Trophies));
        // 确认所有资源就绪后先保存开战账目，再放行战斗，避免强退绕过失败结算。
        await SavePlayerStateAsync();
    }

    public static string RegisterBattleSettlement(MatchEntrySnapshot entry)
    {
        if (entry != null)
        {
            if (_playerRecord == null || _playerRecord.EntryId != entry.EntryId || _playerRecord.EntryPhase != "Playing")
                throw new InvalidOperationException("本局开战账目尚未保存。");
            return entry.EntryId;
        }
        // 编辑器直接运行战斗场景时，没有主界面匹配入场，也要走同一结算流程。
        if (_playerRecord == null) _playerRecord = new MatchPlayerRecord();
        _playerRecord.RecoverPending(PlayerState, DateTimeOffset.UtcNow);
        var id = _playerRecord.StartStandaloneBattle(PlayerState,
            BattleRewardRules.FromTables(ConfigSystem.Instance.Tables, PlayerState.Trophies));
        PersistBattleStateAsync(_playerRecord, PlayerState).Forget();
        return id;
    }

    public static int GetBattleLeaveCupDelta(string entryId)
    {
        if (_playerRecord == null || _playerRecord.EntryId != entryId || _playerRecord.EntryRules == null || _playerRecord.EntryPhase != "Playing") return 0;
        return _playerRecord.EntryRules.GetTrophiesAfter(BattleSettlementResult.Defeat) - _playerRecord.EntryRules.StartingTrophies;
    }

    public static BattleSettlementReward SettleBattle(string entryId, BattleSettlementResult result, bool abandoned = false)
    {
        var record = _playerRecord ?? throw new InvalidOperationException("缺少玩家结算记录。");
        var player = PlayerState;
        var settlement = record.SettleBattle(player, entryId, result, abandoned);
        // 保存与窗口加载独立；即使结算 UI 加载失败，胜负和杯数也会保留。
        PersistBattleStateAsync(record, player).Forget();
        return new BattleSettlementReward(settlement, async multiplier =>
        {
            if (!ReferenceEquals(_playerRecord, record) || !ReferenceEquals(record.Settlement, settlement))
                throw new InvalidOperationException("这笔结算已不属于当前对局。");
            record.ClaimSettlement(player, entryId, multiplier);
            await SavePlayerStateAsync();
        });
    }

    private static async UniTask PersistBattleStateAsync(MatchPlayerRecord record, MainMenuPlayerState player)
    {
        var token = _appLifetime?.Token ?? CancellationToken.None;
        var warned = false;
        try
        {
            while (!token.IsCancellationRequested && ReferenceEquals(record, _playerRecord) && ReferenceEquals(player, _playerState))
            {
                try { await SavePlayerStateAsync(); return; }
                catch (Exception exception)
                {
                    if (!warned) Debug.LogWarning("对局结果暂未保存，将自动重试：" + exception.Message);
                    warned = true;
                    await UniTask.Delay(3000, ignoreTimeScale: true, cancellationToken: token);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
