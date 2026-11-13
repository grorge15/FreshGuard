using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    [Serializable]
    public sealed class MatchPlayerRecord
    {
        public int Version = 1;
        public int Energy;
        public int Coins;
        public int Trophies;
        public string PlayerName;
        public long LastEnergyUpdateUtcTicks;
        public string EntryId;
        public string EntryPhase;
        public BattleRewardRules EntryRules;
        public BattleSettlementRecord Settlement;

        public void Capture(MainMenuPlayerState state)
        {
            Energy = state.Energy; Coins = state.Coins; Trophies = state.Trophies;
            PlayerName = state.PlayerName; LastEnergyUpdateUtcTicks = state.LastEnergyUpdateUtc.UtcTicks;
        }
        public MainMenuPlayerState Restore(int capacity, TimeSpan recovery, DateTimeOffset now)
        {
            if (Version != 1 || LastEnergyUpdateUtcTicks <= 0 || LastEnergyUpdateUtcTicks > DateTimeOffset.MaxValue.Ticks)
                throw new InvalidDataException("玩家匹配记录格式无效。");
            var state = new MainMenuPlayerState(Energy, recovery, capacity, Coins, Trophies,
                new DateTimeOffset(LastEnergyUpdateUtcTicks, TimeSpan.Zero)) { PlayerName = PlayerName };
            state.Refresh(now);
            return state;
        }
        public void BeginEntry(MainMenuPlayerState state, string entryId, DateTimeOffset now)
        {
            if (EntryPhase == "Matching" || EntryPhase == "Success" || EntryPhase == "Playing")
                throw new InvalidOperationException("已有未完成的对局。");
            if (string.IsNullOrEmpty(entryId)) throw new ArgumentException("缺少入场标识。", nameof(entryId));
            state.Refresh(now);
            if (!state.TrySpendEnergy(MainMenuPlayerState.DefaultEnergyCost)) throw new InvalidOperationException("当前体力不足");
            EntryId = entryId; EntryPhase = "Matching"; EntryRules = null; Settlement = null; Capture(state);
        }
        public void CommitSuccess(string entryId)
        {
            if (EntryId != entryId || EntryPhase != "Matching") throw new InvalidOperationException("匹配记录归属变化。");
            EntryPhase = "Success";
        }
        public bool RefundMatching(MainMenuPlayerState state, DateTimeOffset now)
        {
            if (EntryPhase != "Matching") return false;
            state.Refresh(now); state.AddEnergy(MainMenuPlayerState.DefaultEnergyCost);
            EntryPhase = "Refunded"; Capture(state);
            return true;
        }
        public bool RefundFailure(MainMenuPlayerState state, string entryId, DateTimeOffset now)
        {
            if (EntryId != entryId || (EntryPhase != "Matching" && EntryPhase != "Success")) return false;
            state.Refresh(now); state.AddEnergy(MainMenuPlayerState.DefaultEnergyCost);
            EntryPhase = "Refunded"; Capture(state); return true;
        }

        public void StartBattle(MainMenuPlayerState state, string entryId, BattleRewardRules rules)
        {
            if (EntryId != entryId || EntryPhase != "Success") throw new InvalidOperationException("开战记录归属变化。");
            if (rules == null || rules.StartingTrophies != state.Trophies)
                throw new InvalidOperationException("开战杯数与入场快照不一致。");
            EntryRules = rules; Settlement = null; EntryPhase = "Playing"; Capture(state);
        }

        /// <summary>独立运行战斗场景时也使用相同账目；正式匹配通过 StartBattle 入场。</summary>
        public string StartStandaloneBattle(MainMenuPlayerState state, BattleRewardRules rules)
        {
            RecoverPending(state, DateTimeOffset.UtcNow);
            EntryId = Guid.NewGuid().ToString("N"); EntryPhase = "Success";
            StartBattle(state, EntryId, rules);
            return EntryId;
        }

        public void CancelUnreleasedBattle(string entryId)
        {
            if (EntryId == entryId && EntryPhase == "Playing" && Settlement == null) EntryPhase = "Success";
        }

        public BattleSettlementRecord SettleBattle(MainMenuPlayerState state, string entryId,
            BattleSettlementResult result, bool abandoned)
        {
            if (string.IsNullOrEmpty(entryId) || EntryId != entryId) throw new InvalidOperationException("结算记录归属变化。");
            if (EntryPhase == "Settled" && Settlement != null)
            {
                Settlement.EnsureOpponentTrophies(EntryRules);
                return Settlement;
            }
            if (EntryPhase != "Playing" || EntryRules == null) throw new InvalidOperationException("本局尚未正式开战。");
            if (state.Trophies != EntryRules.StartingTrophies) throw new InvalidOperationException("本局杯数已被其他逻辑改变。");
            var outcome = abandoned ? BattleSettlementResult.Defeat : result;
            var after = EntryRules.GetTrophiesAfter(outcome);
            var data = new BattleSettlementRecord
            {
                EntryId = entryId, Result = outcome, Abandoned = abandoned,
                TrophiesBefore = state.Trophies, TrophiesAfter = after,
                BaseCoins = abandoned ? 0 : outcome == BattleSettlementResult.Victory ? EntryRules.WinCoins : EntryRules.LoseCoins,
                ItemName = EntryRules.ItemName
            };
            data.EnsureOpponentTrophies(EntryRules);
            state.ChangeTrophies(data.TrophyDelta);
            Settlement = data; EntryPhase = "Settled"; Capture(state);
            return data;
        }

        public void ClaimSettlement(MainMenuPlayerState state, string entryId, int multiplier)
        {
            if (EntryId != entryId || EntryPhase != "Settled" || Settlement == null)
                throw new InvalidOperationException("奖励记录归属变化。");
            if (multiplier < 1 || multiplier > 2) throw new ArgumentOutOfRangeException(nameof(multiplier));
            if (Settlement.Abandoned) multiplier = 1;
            multiplier = Math.Max(multiplier, Settlement.ClaimedMultiplier);
            state.AddCoins(checked(Settlement.BaseCoins * (multiplier - Settlement.ClaimedMultiplier)));
            Settlement.ClaimedMultiplier = multiplier;
            Capture(state);
        }

        /// <summary>加载未完成不扣杯；已开战未结束按弃局处理；已保存胜负绝不改判。</summary>
        public void RecoverPending(MainMenuPlayerState state, DateTimeOffset now)
        {
            if (EntryPhase == "Matching" || EntryPhase == "Success") RefundFailure(state, EntryId, now);
            else if (EntryPhase == "Playing") SettleBattle(state, EntryId, BattleSettlementResult.Defeat, true);
            // 兼容尚未记录敌方杯数的旧账目，只补齐展示数据，不重复改变玩家余额。
            if (EntryPhase == "Settled" && Settlement != null) Settlement.EnsureOpponentTrophies(EntryRules);
            // 已确定胜负但未点击领取的基础金币，在重启时补领一次。
            if (EntryPhase == "Settled" && Settlement != null && Settlement.ClaimedMultiplier == 0)
                ClaimSettlement(state, EntryId, 1);
        }
    }

    /// <summary>只在同一串行队列写入；临时文件与目标同目录，替换提交体力与入场状态。</summary>
    public sealed class MatchPlayerStore
    {
        private readonly string _path;
        private readonly SemaphoreSlim _serial = new SemaphoreSlim(1, 1);
        public MatchPlayerStore(string path) { _path = path ?? throw new ArgumentNullException(nameof(path)); }
        public async UniTask<MatchPlayerRecord> LoadAsync()
        {
            var json = await UniTask.RunOnThreadPool(() => File.Exists(_path) ? File.ReadAllText(_path, Encoding.UTF8) : null);
            return json == null ? null : JsonUtility.FromJson<MatchPlayerRecord>(json);
        }
        public async UniTask SaveAsync(MatchPlayerRecord record)
        {
            var json = JsonUtility.ToJson(record);
            await _serial.WaitAsync();
            try
            {
                await UniTask.RunOnThreadPool(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    var temporary = _path + ".tmp";
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var data = Encoding.UTF8.GetBytes(json);
                        stream.Write(data, 0, data.Length); stream.Flush(true);
                    }
                    if (File.Exists(_path)) File.Replace(temporary, _path, null);
                    else File.Move(temporary, _path);
                });
            }
            finally { _serial.Release(); }
        }
    }
}
