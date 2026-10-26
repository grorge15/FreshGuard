using System.Collections.Generic;
using System.Reflection;
using System;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEngine;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
#pragma warning disable CS0436


/// <summary>
/// 游戏App。
/// </summary>
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;
    private static bool _battleStarting;
    private static MainMenuPlayerState _playerState;

    public static MainMenuPlayerState PlayerState => _playerState ?? (_playerState = CreatePlayerState());

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic();
    }
    
    private static void StartGameLogic()
    {
        StartMainMenuAsync().Forget();
    }

    private static async UniTask StartMainMenuAsync()
    {
        if (!GameModule.UI.EnsureUIRoot())
            throw new InvalidOperationException("启动场景缺少 UIRoot，无法显示 MainMenuUI。");
        await GameModule.UI.ShowUIAsyncAwait<MainMenuUI>();
        BattleSettlementPresenter.Instance.Active();
    }

    /// <summary>由 MainMenuUI 的开始按钮调用；同一时间只允许一个战斗入口。</summary>
    public static UniTask BeginBattleFromMainMenu(MainMenuUI source)
    {
        if (_battleStarting) return UniTask.CompletedTask;
        _battleStarting = true;
        return BeginBattleAsync(source);
    }

    private static async UniTask BeginBattleAsync(MainMenuUI source)
    {
        BattleLoadingUI loading = null;
        BattleShopSceneController controller = null;
        BattleShopContext startupContext = null;
        var spentEnergy = false;
        try
        {
            loading = await GameModule.UI.ShowUIAsyncAwait<BattleLoadingUI>();
            loading?.Report(new BattleLoadingProgress(BattleLoadingStage.Scene, 0f, "进入战斗场景"));
            // Launcher 的占位场景 main 与战斗场景 Main 同名，须按实际装配判断是否可复用。
            var alreadyInBattleScene = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>() != null;
            if (alreadyInBattleScene)
            {
                loading?.Report(new BattleLoadingProgress(BattleLoadingStage.Scene, 0.35f, "战斗场景已就绪"));
            }
            else
            {
                await GameModule.Scene.LoadSceneAsync("Main", progressCallBack: progress =>
                    loading?.Report(new BattleLoadingProgress(BattleLoadingStage.Scene, progress * 0.35f,
                        "加载战斗场景")));
            }

            controller = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
            if (controller == null) throw new InvalidOperationException("Main 场景缺少 BattleShopSceneController。");
            var reporter = new Progress<BattleLoadingProgress>(progress => loading?.Report(progress));
            await controller.InitializeAsync(reporter);
            var timeout = 0f;
            while (!controller.IsReady && controller.IsInitializing && timeout < 90f)
            {
                timeout += UnityEngine.Time.unscaledDeltaTime;
                await UniTask.Yield();
            }
            if (!controller.IsReady)
                throw controller.InitializationError ?? new InvalidOperationException("战斗初始化未完成。");
            startupContext = controller.Context;

            // IsReady 只代表 MainCombatUI 和 AI 装配完成；真正进入战斗还要等待双方球发射、首批敌人生成，
            // 以及双方能源球完成首次物理反弹，确保玻璃/碰撞系统已经实际运行。
            loading?.Report(new BattleLoadingProgress(BattleLoadingStage.Ready, 0.98f, "等待球体碰撞与首批敌人"));
            timeout = 0f;
            while (controller != null && controller.Context == startupContext && !startupContext.IsEnded &&
                !controller.IsCombatOperational && timeout < 30f)
            {
                timeout += UnityEngine.Time.unscaledDeltaTime;
                await UniTask.Yield();
            }
            if (controller == null || controller.Context != startupContext || !controller.IsCombatOperational)
                throw new InvalidOperationException("战斗未启动或对局已变化：能源球碰撞或首批敌人没有进入运行态。");
            loading?.Report(new BattleLoadingProgress(BattleLoadingStage.CombatStarted, 1f, "战斗开始"));

            if (loading != null) GameModule.UI.CloseUI<BattleLoadingUI>();
            if (source != null) GameModule.UI.CloseUI<MainMenuUI>();
            // 等窗口显隐和布局更新完成后再开放双方操作，AI初始延迟从此时开始累计。
            await UniTask.Yield();
            if (controller == null || controller.Context != startupContext ||
                !controller.TryOpenRobotInteraction(startupContext.BattleId))
                throw new InvalidOperationException("战斗界面尚不可交互或对局已变化。");
            spentEnergy = true;
        }
        catch (Exception exception)
        {
            Log.Error("进入战斗失败：" + exception.Message);
            // 失败返回主界面前清理本次拥有的对局，不能误清理通知中新开的其他对局。
            if (controller != null && startupContext != null && controller.Context == startupContext)
                controller.EndBattle();
            if (!spentEnergy) PlayerState.AddEnergy(MainMenuPlayerState.DefaultEnergyCost);
            if (loading != null) GameModule.UI.CloseUI<BattleLoadingUI>();
            if (source != null) source.ResetStartButton();
            try
            {
                await GameModule.UI.ShowUIAsyncAwait<MainMenuUI>();
            }
            catch (Exception returnException)
            {
                Log.Error("返回主界面失败：" + returnException.Message);
            }
        }
        finally
        {
            _battleStarting = false;
        }
    }

    private static MainMenuPlayerState CreatePlayerState()
    {
        var capacity = MainMenuPlayerState.DefaultEnergyCapacity;
        var minutes = 5;
        try
        {
            var table = ConfigSystem.Instance.Tables.TbGlobal;
            capacity = Mathf.Max(1, Mathf.RoundToInt(table.Get(51).Value));
            minutes = Mathf.Max(1, Mathf.RoundToInt(table.Get(52).Value));
        }
        catch (Exception exception)
        {
            Log.Warning("主界面 Global 读取失败，使用 Demo 默认体力配置：" + exception.Message);
        }
        return new MainMenuPlayerState(capacity, TimeSpan.FromMinutes(minutes), capacity,
            MainMenuPlayerState.DefaultCoins, MainMenuPlayerState.DefaultTrophies, DateTimeOffset.UtcNow);
    }
    
    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}
