using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    internal static class FirstBossWavePreview
    {
        private const string Pending = "FreshGuard.PreviewFirstBossWave";
        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                SessionState.SetBool(Pending, false);
                ShowBoss().Forget();
            };
            EditorApplication.delayCall += () =>
            {
                const string flag = "Temp/open-boss-wave.once";
                if (!File.Exists(flag)) return;
                File.Delete(flag);
                Launch();
            };
        }

        [MenuItem("FreshGuard/Battle/Preview First Boss Wave")]
        private static void Launch()
        {
            if (EditorApplication.isPlaying) { EditorApplication.isPaused = false; ShowBoss().Forget(); return; }
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static async UniTaskVoid ShowBoss()
        {
            try
            {
                var host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host == null || !host.IsReady)
                {
                    await UniTask.WaitUntil(() => UIModule.IsValid && GameModule.UI.GetUI<MainMenuUI>() != null)
                        .Timeout(TimeSpan.FromSeconds(65));
                    await GameModule.Scene.LoadSceneAsync("Main");
                    GameModule.UI.CloseUI<MainMenuUI>();
                    host = Object.FindObjectOfType<BattleShopSceneController>();
                    await host.InitializeAsync();
                }
                await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(40));
                await UniTask.WaitUntil(() => host.TryOpenRobotInteraction(host.Context.BattleId))
                    .Timeout(TimeSpan.FromSeconds(30));
                var enemies = host.Enemies;
                var first = BattleWaveTimeline.FromTables(ConfigSystem.Instance.Tables).Groups
                    .Where(g => g.Config.EnemyType == 3).OrderBy(g => g.NextTime).First();
                while (enemies.ElapsedSeconds < first.NextTime && host.IsCombatRunning)
                {
                    foreach (var view in Object.FindObjectsOfType<EnemyView>())
                    {
                        if (!host.Context.Registry.TryGet(view.InstanceId, out var entity) ||
                            !(entity is EnemyEntity enemy) || enemy.Configuration?.EnemyType == 3) continue;
                        if (host.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal) == DamageResult.Killed)
                            enemies.NotifyKilled(removal);
                    }
                    enemies.Advance((float)Math.Min(1d, first.NextTime - enemies.ElapsedSeconds));
                }
                await UniTask.Delay(1100, ignoreTimeScale: true);
                var boss = Object.FindObjectsOfType<EnemyView>().First(v => v.ConfigId == first.Config.Id &&
                    host.Context.Registry.TryGet(v.InstanceId, out var e) && e is EnemyEntity live && live.IsAlive && live.Side == BattleSide.Player);
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                var before = enemies.ElapsedSeconds;
                await UniTask.DelayFrame(3);
                if (!host.CanInteractWithRobots || enemies.ElapsedSeconds <= before)
                    throw new InvalidOperationException("Boss 出场后交互或战斗计时未正常推进。");
                File.WriteAllText("Temp/boss-wave-preview.status", $"wave={enemies.CurrentWave}; boss={boss.ConfigId}; active={boss.gameObject.activeInHierarchy}; paused={EditorApplication.isPaused}; interactive=True; advancing=True");
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Temp/boss-wave-preview.status", e.ToString()); }
        }
    }
}
