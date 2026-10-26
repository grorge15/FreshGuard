using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEngine;
#if ENABLE_OBFUZ
using Obfuz;
#endif
#pragma warning disable CS0436
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;
    private static bool _battleStarting;
    private static MainMenuPlayerState _playerState;
    private static DemoBattleProfile _demoProfile;
    private static MatchPlayerStore _playerStore;
    private static MatchPlayerRecord _playerRecord;
    private static BattleMatchSession _matchSession;
    private static BattleShopSceneController _matchController;
    private static CancellationTokenSource _appLifetime;
    public static MainMenuPlayerState PlayerState => _playerState ?? (_playerState = CreatePlayerState());
    public static BattleMatchSession CurrentMatch => _matchSession;

    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        _appLifetime = new CancellationTokenSource();
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        StartMainMenuAsync().Forget();
    }
    private static async UniTask StartMainMenuAsync()
    {
        var lifetime = _appLifetime.Token;
        try
        {
            if (!GameModule.UI.EnsureUIRoot()) throw new InvalidOperationException("启动场景缺少 UIRoot。");
            _demoProfile = await GameModule.Resource.LoadAssetAsync<DemoBattleProfile>("DemoBattleProfile", cancellationToken:lifetime);
            lifetime.ThrowIfCancellationRequested();
            if (_demoProfile == null) throw new InvalidOperationException("缺少DemoBattleProfile资源。");
            _playerState = CreatePlayerState();
            _playerState.PlayerName = _demoProfile.PlayerName;
            _playerStore = new MatchPlayerStore(Path.Combine(Application.persistentDataPath,"match-player-v1.json"));
            var saved = await _playerStore.LoadAsync();
            lifetime.ThrowIfCancellationRequested();
            if (saved != null)
                _playerState = saved.Restore(_playerState.EnergyCapacity,_playerState.RecoveryInterval,DateTimeOffset.UtcNow);
            _playerRecord = saved ?? new MatchPlayerRecord();
            _playerRecord.RefundMatching(_playerState,DateTimeOffset.UtcNow);
            await SavePlayerStateAsync();
            lifetime.ThrowIfCancellationRequested();
            await GameModule.UI.ShowUIAsyncAwait<MainMenuUI>();
            lifetime.ThrowIfCancellationRequested();
            BattleSettlementPresenter.Instance.Active();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Error("主界面初始化失败：" + e); }
    }
    public static async UniTask SavePlayerStateAsync()
    {
        if (_playerStore == null || _playerRecord == null || _playerState == null) return;
        _playerRecord.Capture(_playerState);
        await _playerStore.SaveAsync(_playerRecord);
    }
    public static UniTask BeginBattleFromMainMenu(MainMenuUI source)
    {
        if (_battleStarting) return UniTask.CompletedTask;
        _battleStarting = true;
        return BeginBattleAsync(source);
    }
    public static void CancelBattleMatch()
    {
        var session = _matchSession;
        if (session == null || !session.TryCancel()) return;
        _matchController?.CancelMatch(session.EntryId);
        GameModule.UI.CloseUI<BattleLoadingUI>();
    }
    private static async UniTask BeginBattleAsync(MainMenuUI source)
    {
        BattleLoadingUI loading = null;
        BattleMatchSession session = null;
        MatchEntrySnapshot entry = null;
        UniTask<BattleShopSceneController>? preparation = null;
        var released = false;
        var lifetime = _appLifetime?.Token ?? CancellationToken.None;
        try
        {
            if (_demoProfile == null || _playerRecord == null) throw new InvalidOperationException("Demo入场数据尚未就绪。");
            var tables = ConfigSystem.Instance.Tables;
            entry = new MatchEntrySnapshot(_demoProfile,PlayerState,ShopRules.FromTables(tables));
            foreach (var id in entry.Candidates)
            {
                var robot = tables.TbRobot.Get(id);
                if (!GameModule.Resource.CheckLocationValid(robot.RobotPrefab) || !GameModule.Resource.CheckLocationValid(robot.RobotIcon))
                    throw new InvalidOperationException("Demo机器人资源无效：" + id);
            }
            foreach (var buff in entry.PlayerBuffs)
                if (!GameModule.Resource.CheckLocationValid(buff.IconLocation)) throw new InvalidOperationException("BUFF图片未收集：" + buff.IconLocation);
            foreach (var buff in entry.OpponentBuffs)
                if (!GameModule.Resource.CheckLocationValid(buff.IconLocation)) throw new InvalidOperationException("BUFF图片未收集：" + buff.IconLocation);
            if (!GameModule.Resource.CheckLocationValid(entry.PlayerAvatar) || !GameModule.Resource.CheckLocationValid(entry.OpponentAvatar))
                throw new InvalidOperationException("Demo头像资源未收集。");
            _playerRecord.BeginEntry(PlayerState,entry.EntryId,DateTimeOffset.UtcNow);
            await SavePlayerStateAsync();
            lifetime.ThrowIfCancellationRequested();
            session = new BattleMatchSession(entry.EntryId);
            _matchSession = session;
            loading = await GameModule.UI.ShowUIAsyncAwait<BattleLoadingUI>();
            lifetime.ThrowIfCancellationRequested();
            if (loading == null) throw new InvalidOperationException("匹配窗口加载失败。");
            loading.Bind(entry,session,CancelBattleMatch);
            preparation = PrepareBattleForMatchAsync(entry,loading,session,lifetime).Preserve();
            while (preparation.Value.Status == UniTaskStatus.Pending)
            {
                lifetime.ThrowIfCancellationRequested();
                if (session.Phase == BattleMatchPhase.Matching)
                {
                    session.Advance(Time.unscaledDeltaTime,MatchApplicationState.IsForeground);
                    loading.Render();
                }
                await UniTask.Yield();
            }
            var controller = await preparation.Value;
            lifetime.ThrowIfCancellationRequested();
            if (session.Phase == BattleMatchPhase.Cancelled) throw new OperationCanceledException();
            if (controller == null || !controller.IsReady)
                throw controller?.InitializationError ?? new InvalidOperationException("战斗初始化未完成。");
            session.MarkReady();
            while (session.Phase == BattleMatchPhase.Matching)
            {
                lifetime.ThrowIfCancellationRequested();
                session.Advance(Time.unscaledDeltaTime,MatchApplicationState.IsForeground);
                if (session.Phase == BattleMatchPhase.Matching)
                {
                    loading.Render();
                    await UniTask.Yield();
                }
            }
            if (session.Phase == BattleMatchPhase.Cancelled) throw new OperationCanceledException();
            _playerRecord.CommitSuccess(entry.EntryId);
            await SavePlayerStateAsync();
            lifetime.ThrowIfCancellationRequested();
            loading.Render();
            while (session.Phase == BattleMatchPhase.Success)
            {
                await UniTask.Yield();
                lifetime.ThrowIfCancellationRequested();
                session.Advance(Time.unscaledDeltaTime,MatchApplicationState.IsForeground);
                loading.Render();
            }
            GameModule.UI.CloseUI<BattleLoadingUI>();
            if (source != null) GameModule.UI.CloseUI<MainMenuUI>();
            if (!controller.ReleaseMatch(entry.EntryId)) throw new InvalidOperationException("本局无法放行开战。");
            released = true;
        }
        catch (Exception e)
        {
            // 退出应用后不重新创建窗口；Matching磁盘记录交给下次启动一次性恢复。
            if (lifetime.IsCancellationRequested) return;
            var cancelled = e is OperationCanceledException || session?.Phase == BattleMatchPhase.Cancelled;
            if (!cancelled) { session?.Fail(); Log.Error("进入战斗失败：" + e.Message); }
            if (entry != null && !released)
            {
                _matchController?.CancelMatch(entry.EntryId);
                if (_playerRecord != null && _playerRecord.RefundFailure(PlayerState,entry.EntryId,DateTimeOffset.UtcNow))
                {
                    try { await SavePlayerStateAsync(); }
                    catch (Exception saveError) { Log.Error("返还保存失败：" + saveError.Message); }
                }
            }
            if (loading != null && !loading.IsDestroyed) GameModule.UI.CloseUI<BattleLoadingUI>();
            if (preparation.HasValue && preparation.Value.Status == UniTaskStatus.Pending)
            {
                try { await preparation.Value; } catch (Exception late) { if (!(late is OperationCanceledException)) Log.Warning(late.Message); }
            }
            if (entry != null) _matchController?.CancelMatch(entry.EntryId);
            lifetime.ThrowIfCancellationRequested();
            var menu = await GameModule.UI.ShowUIAsyncAwait<MainMenuUI>();
            lifetime.ThrowIfCancellationRequested();
            menu?.ResetStartButton();
            if (!cancelled) menu?.ShowStatus(e.Message);
        }
        finally
        {
            _battleStarting = false;
            _matchSession = null;
            _matchController = null;
            if (!lifetime.IsCancellationRequested && source != null && !source.IsDestroyed) source.ResetStartButton();
        }
    }
    private static async UniTask<BattleShopSceneController> PrepareBattleForMatchAsync(MatchEntrySnapshot entry,
        BattleLoadingUI loading, BattleMatchSession session, CancellationToken lifetime)
    {
        // Launcher的main与业务Main是不同场景，不能忽略大小写。
        var mainScene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/AssetRaw/Scenes/Main.unity");
        var inMain = mainScene.IsValid() && mainScene.isLoaded;
        if (!inMain)
            await GameModule.Scene.LoadSceneAsync("Main",progressCallBack:p=>loading.Report(new BattleLoadingProgress(BattleLoadingStage.Scene,p*.35f,"加载战斗场景")));
        lifetime.ThrowIfCancellationRequested();
        if (session.Phase == BattleMatchPhase.Cancelled) throw new OperationCanceledException();
        var controller = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
        if (controller == null) throw new InvalidOperationException("Main缺少BattleShopSceneController。");
        _matchController = controller;
        if (controller.Context != null && controller.Context.IsEnded) controller.EndBattle();
        await controller.PrepareMatchAsync(entry,new Progress<BattleLoadingProgress>(p=>
        {
            if (_matchSession == session && session.Phase != BattleMatchPhase.Cancelled && !loading.IsDestroyed) loading.Report(p);
        }));
        lifetime.ThrowIfCancellationRequested();
        if (session.Phase == BattleMatchPhase.Cancelled) { controller.CancelMatch(entry.EntryId); throw new OperationCanceledException(); }
        return controller;
    }
    private static MainMenuPlayerState CreatePlayerState()
    {
        var capacity = MainMenuPlayerState.DefaultEnergyCapacity; var minutes = 5;
        try
        {
            var table=ConfigSystem.Instance.Tables.TbGlobal;
            capacity=Mathf.Max(1,Mathf.RoundToInt(table.Get(51).Value));
            minutes=Mathf.Max(1,Mathf.RoundToInt(table.Get(52).Value));
        }
        catch(Exception e) { Log.Warning("Global读取失败，使用Demo体力默认值："+e.Message); }
        return new MainMenuPlayerState(capacity,TimeSpan.FromMinutes(minutes),capacity,
            MainMenuPlayerState.DefaultCoins,_demoProfile==null?MainMenuPlayerState.DefaultTrophies:_demoProfile.InitialTrophies,DateTimeOffset.UtcNow);
    }
    private static void Release()
    {
        _appLifetime?.Cancel();
        _appLifetime?.Dispose();
        _appLifetime=null;
        if (_demoProfile != null) GameModule.Resource.UnloadAsset(_demoProfile);
        _demoProfile=null; _playerState=null; _playerRecord=null; _playerStore=null;
        _matchSession=null; _matchController=null; _battleStarting=false;
        SingletonSystem.Release();
    }
}
