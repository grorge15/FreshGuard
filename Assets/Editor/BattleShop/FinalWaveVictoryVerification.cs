using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>通过真实Launcher和配置推进战斗；仅操作运行实例，不保存场景或配置。</summary>
    public static class FinalWaveVictoryVerification
    {
        private const string Pending = "FreshGuard.FinalWaveVictory.LaunchPending";
        private static bool _running;

        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string details;
            public int finalWave;
            public int enemiesPerSide;
            public int killed;
            public bool deathWait;
            public bool pausedWithoutSettlement;
            public bool victoryWindow;
            public bool duplicateIgnored;
            public int playerHp;
            public int opponentHp;
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("FreshGuard/Battle/Verify Final Wave Victory via TEngine Launcher")]
        public static void RunViaLauncher()
        {
            Require(!EditorApplication.isPlaying && !EditorApplication.isCompiling, "请在停止运行、编译完成后启动验证。");
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty, "场景有未保存修改，不能切换Launcher。");
            var helper = typeof(TEngine.UnityToolbarExtenderLeft).GetNestedType("SceneHelper", BindingFlags.NonPublic);
            var start = helper?.GetMethod("StartScene", BindingFlags.Public | BindingFlags.Static);
            Require(start != null, "没有找到TEngine Launcher入口。");
            SessionState.SetBool(Pending, true);
            try { start.Invoke(null, new object[] { "Main" }); }
            catch { SessionState.SetBool(Pending, false); throw; }
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            if (!_running) VerifyAsync().Forget();
        }

        private static async UniTaskVoid VerifyAsync()
        {
            _running = true;
            var report = new Report();
            try
            {
                await UniTask.WaitUntil(() => UIModule.IsValid && GameModule.UI.GetUI<MainMenuUI>() != null)
                    .Timeout(TimeSpan.FromSeconds(60));
                await GameApp.BeginBattleFromMainMenu(GameModule.UI.GetUI<MainMenuUI>());
                var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                Require(host != null && host.IsCombatOperational, "Launcher没有进入真实运行战斗。");
                var context = host.Context;
                var enemies = host.Enemies;
                var timeline = BattleWaveTimeline.FromTables(ConfigSystem.Instance.Tables);
                report.finalWave = timeline.Groups.Max(g => g.Wave);
                report.enemiesPerSide = timeline.Groups.Sum(g => g.Rule.EnemyCount);
                var lastWindow = timeline.Groups.Max(g => g.Rule.SpawnSecRange[1]);
                Require(host.ApplyTowerDamageBatch(context.BattleId, 1, 2), "无法设置非致命耐久验证值。");
                report.playerHp = host.Towers.PlayerTower.CurrentHp;
                report.opponentHp = host.Towers.OpponentTower.CurrentHp;
                // 同步推进实际EnemyBattleController，让每只敌人经过正常击杀与死亡表现入口。
                // 不改时间线、波次表、生成序号或活动敌人集合。
                for (var step = 0; step <= lastWindow + 1; step++)
                {
                    foreach (var view in UnityEngine.Object.FindObjectsOfType<EnemyView>())
                    {
                        if (!context.Registry.TryGet(view.InstanceId, out var entity) || !(entity is EnemyEntity)) continue;
                        if (context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal) != DamageResult.Killed) continue;
                        enemies.NotifyKilled(removal);
                        report.killed++;
                    }
                    if (enemies.IsAllEnemiesCleared) break;
                    Require(host.IsCombatRunning, "出兵未完成就提前结束战斗。");
                    enemies.Advance(1f);
                }
                Require(enemies.CurrentWave == report.finalWave && enemies.IsAllEnemiesCleared, "未清空配置中的最后一波。");
                Require(enemies.GetSpawnedCount(BattleSide.Player) == report.enemiesPerSide &&
                    enemies.GetSpawnedCount(BattleSide.Opponent) == report.enemiesPerSide &&
                    report.killed == 2 * report.enemiesPerSide, "双方实际出兵或击杀数量不一致。");
                report.deathWait = enemies.DeathViewCount > 0 && !enemies.IsWaveVictoryReady;
                Require(report.deathWait, "最后的死亡表现尚未播放就可判胜。");
                host.SetFrozen(true);
                await UniTask.Delay(TimeSpan.FromMilliseconds(200), ignoreTimeScale: true);
                report.pausedWithoutSettlement = host.IsReady && !context.IsEnded && GameModule.UI.GetUI<CheckOutUI>() == null;
                Require(report.pausedWithoutSettlement, "暂停时误触发结算。");
                host.SetFrozen(false);
                await UniTask.WaitUntil(() =>
                {
                    var result = GameModule.UI.GetUI<CheckOutUI>();
                    return result != null && result.IsPrepare && result.gameObject != null;
                }).Timeout(TimeSpan.FromSeconds(10));
                var window = GameModule.UI.GetUI<CheckOutUI>();
                var snapshot = (BattleSettlementSnapshot)window.UserData;
                report.victoryWindow = context.IsEnded && host.Context == null &&
                    snapshot.Reason == BattleSettlementReason.FinalWaveCleared && snapshot.Result == BattleSettlementResult.Victory &&
                    snapshot.PlayerTowerHp == report.playerHp && snapshot.OpponentTowerHp == report.opponentHp &&
                    snapshot.DefeatedSide == null && window.transform.Find("m_go_Victory").gameObject.activeSelf;
                Require(report.victoryWindow, "胜利窗口、真实耐久或清理状态错误。");
                GameEvent.Send(BattleSettlementEvents.Requested, snapshot);
                await UniTask.Yield();
                report.duplicateIgnored = ReferenceEquals(window, GameModule.UI.GetUI<CheckOutUI>());
                Require(report.duplicateIgnored, "重复终局通知重复创建窗口。");
                report.passed = true;
                report.details = "TEngine Launcher真实启动，按原配置完整出兵与正常击杀，等待死亡表现，暂停不结算，清理后仅打开一次胜利窗口。";
            }
            catch (Exception error) { report.details = error.ToString(); Debug.LogError("[FinalWaveVictoryVerification] " + error); }
            finally
            {
                Directory.CreateDirectory("Library/FinalWaveVerification");
                File.WriteAllText("Library/FinalWaveVerification/launcher.json", JsonUtility.ToJson(report, true));
                Debug.Log("[FinalWaveVictoryVerification] " + JsonUtility.ToJson(report));
                _running = false;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
