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
            if (EntryPhase == "Matching") throw new InvalidOperationException("已有未完成的入场扣费。");
            state.Refresh(now);
            if (!state.TrySpendEnergy(MainMenuPlayerState.DefaultEnergyCost)) throw new InvalidOperationException("当前体力不足");
            EntryId = entryId; EntryPhase = "Matching"; Capture(state);
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
