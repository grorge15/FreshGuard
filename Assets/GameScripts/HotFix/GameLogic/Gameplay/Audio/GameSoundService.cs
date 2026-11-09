using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public enum GameSound
    {
        UiClick, UiOpen, UiClose, Error, MatchFound, Countdown, BattleStart,
        LeverDown, LeverReturn, ReelSpin, ReelStop, Purchase, BallWall,
        GlassHit, GlassBreak, GlassRainbow, CoinCollect, Combo, RageStart, RageEnd,
        TowerHit, TowerDestroyed, Victory, Defeat, RewardClaim,
        BuffAcquired, BuffStack, BuffRemove, WaveAdvance, BuffChoose, RobotHit
    }

    [Serializable]
    public sealed class GameSoundEntry
    {
        public string id;
        public string location;
        public float volume;
        public float cooldown;
        public bool ui;
        public bool loop;
    }

    [Serializable]
    public sealed class GameSoundCatalog
    {
        public GameSoundEntry[] entries;
    }

    /// <summary>业务音效门面。异步预热框架音频池；播放时不加载资源，不改变玩法状态。</summary>
    public static class GameSoundService
    {
        private static readonly Dictionary<GameSound, GameSoundEntry> Entries = new Dictionary<GameSound, GameSoundEntry>();
        private static readonly Dictionary<GameSound, float> LastPlayed = new Dictionary<GameSound, float>();
        private static readonly Dictionary<AudioAgent, long> Leases = new Dictionary<AudioAgent, long>();
        private static readonly List<string> OwnedPaths = new List<string>();
        private static long _nextLease;
        public static bool IsReady { get; private set; }
        public static int PlaybackCount { get; private set; }

        public static async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            if (IsReady) return;
            TextAsset asset = null;
            try
            {
                asset = await GameModule.Resource.LoadAssetAsync<TextAsset>("FreshGuardSoundCatalog", cancellationToken);
                var catalog = JsonUtility.FromJson<GameSoundCatalog>(asset.text);
                foreach (var entry in catalog.entries)
                {
                    var id = (GameSound)Enum.Parse(typeof(GameSound), entry.id);
                    if (Entries.ContainsKey(id) || string.IsNullOrEmpty(entry.location) ||
                        entry.volume < 0f || entry.volume > 1f || entry.cooldown < 0f)
                        throw new InvalidOperationException("无效音效配置：" + entry.id);
                    Entries.Add(id, entry);
                    if (!GameModule.Audio.AudioClipPool.ContainsKey(entry.location))
                    {
                        var handle = GameModule.Resource.LoadAssetAsyncHandle<AudioClip>(entry.location);
                        try
                        {
                            await handle.Task;
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!(handle.AssetObject is AudioClip)) throw new InvalidOperationException("缺少音效：" + entry.location);
                            GameModule.Audio.AudioClipPool.Add(entry.location, handle);
                            OwnedPaths.Add(entry.location);
                        }
                        catch { handle.Dispose(); throw; }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                IsReady = true;
            }
            catch (OperationCanceledException) { Release(); throw; }
            catch (Exception exception)
            {
                Release();
                Debug.LogWarning("音效初始化失败，游戏继续运行：" + exception.Message);
            }
            finally { if (asset != null) GameModule.Resource.UnloadAsset(asset); }
        }

        /// <summary>棋盘音效只播放己方事件；敌方事件不占用声道和播放间隔。</summary>
        public static GameSoundPlayback PlayForSide(GameSound sound, BattleSide side)
        {
            return side == BattleSide.Player ? Play(sound) : null;
        }

        public static GameSoundPlayback Play(GameSound sound, float gain = 1f, float pitch = 1f)
        {
            GameSoundEntry entry;
            if (!IsReady || !Application.isPlaying || !Entries.TryGetValue(sound, out entry)) return null;
            var now = Time.unscaledTime;
            float last;
            if (LastPlayed.TryGetValue(sound, out last) && now - last < entry.cooldown) return null;
            var agent = GameModule.Audio.Play(entry.ui ? TEngine.AudioType.UISound : TEngine.AudioType.Sound,
                entry.location, entry.loop, Mathf.Clamp01(entry.volume * gain), bAsync: true, bInPool: true);
            if (agent == null) return null;
            var source = agent.AudioResource();
            source.pitch = Mathf.Clamp(pitch, 0.65f, 1.6f);
            source.spatialBlend = 0f;
            source.ignoreListenerPause = entry.ui;
            var lease = ++_nextLease;
            Leases[agent] = lease;
            LastPlayed[sound] = now;
            PlaybackCount++;
            return new GameSoundPlayback(agent, lease, entry.location);
        }

        internal static void Stop(AudioAgent agent, long lease, string location)
        {
            long current;
            // 框架会复用声道；旧的滚轴句柄不能停止该声道后来播放的声音。
            if (agent != null && Leases.TryGetValue(agent, out current) && current == lease &&
                agent.AudioData?.AssetHandle?.GetAssetInfo().Address == location)
            {
                agent.Stop();
                Leases.Remove(agent);
            }
        }

        public static void BindButtons(GameObject root)
        {
            if (root == null) return;
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                if (button.GetComponent<GameButtonSound>() == null) button.gameObject.AddComponent<GameButtonSound>().Bind(button);
        }

        public static void Release()
        {
            IsReady = false;
            foreach (var agent in Leases.Keys) agent.Stop();
            Leases.Clear();
            if (OwnedPaths.Count > 0) GameModule.Audio.RemoveClipFromPool(OwnedPaths);
            OwnedPaths.Clear();
            Entries.Clear();
            LastPlayed.Clear();
            PlaybackCount = 0;
        }
    }

    public sealed class GameSoundPlayback
    {
        private readonly AudioAgent _agent;
        private readonly long _lease;
        private readonly string _location;
        internal GameSoundPlayback(AudioAgent agent, long lease, string location)
        { _agent = agent; _lease = lease; _location = location; }
        public void Stop() { GameSoundService.Stop(_agent, _lease, _location); }
    }
}
