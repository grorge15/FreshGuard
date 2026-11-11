using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>真实 Launcher、购买部署、接触施法、弹丸碰撞与 Combat 扣血的串行验收。</summary>
    public static class ChainLightningPlayVerification
    {
        private const string Pending = "FreshGuard.ChainLightning.VerifyPending";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static bool _running;

        [Serializable]
        public sealed class ShotEvidence
        {
            public int attackNumber;
            public int bulletId;
            public int cycleCountAfterLaunch;
            public long sequence;
            public bool resolved;
        }

        [Serializable]
        public sealed class HitEvidence
        {
            public int targetId;
            public long sequence;
            public int requestedDamage;
            public int appliedDamage;
            public int hpBefore;
            public int hpAfter;
            public bool sameSnapshot;
            public bool actualHpDeltaMatched;
        }

        [Serializable]
        public sealed class Report
        {
            public bool passed;
            public string details;
            public string utc;
            public string entry;
            public string fixture;
            public long battleId;
            public int robotInstanceId;
            public int robotLevel;
            public int shopRefreshes;
            public bool purchased;
            public bool deployed;
            public bool fifthCycleReset;
            public bool chainVisible;
            public bool pauseHeldEffect;
            public bool resumedEffectExpired;
            public bool battleEndCleared;
            public bool sharedSnapshot;
            public string screenshot;
            public List<ShotEvidence> shots = new List<ShotEvidence>();
            public List<HitEvidence> chainHits = new List<HitEvidence>();
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        [MenuItem("FreshGuard/Skills/Verify Chain Lightning via TEngine Launcher")]
        public static void RunViaLauncher()
        {
            Require(!_running && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling,
                "请停止运行并等待编译，再从 Launcher 启动验收。");
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                Require(!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty,
                    "场景有未保存修改，无法切换 Main；请先处理场景改动。");
            var helper = typeof(TEngine.UnityToolbarExtenderLeft).GetNestedType("SceneHelper", BindingFlags.NonPublic);
            var start = helper?.GetMethod("StartScene", BindingFlags.Public | BindingFlags.Static);
            Require(start != null, "找不到 TEngine Launcher 的 Main 启动入口。");
            SessionState.SetBool(Pending, true);
            try { start.Invoke(null, new object[] { "main" }); }
            catch { SessionState.SetBool(Pending, false); throw; }
            Debug.Log("[ChainLightning Play] 已请求 Main Launcher；进入 PlayMode 后自动等待主界面并验收。");
        }

        [MenuItem("FreshGuard/Skills/Verify Running Chain Lightning")]
        public static void RunCurrent()
        {
            Require(Application.isPlaying && !EditorApplication.isPaused, "需要未暂停的 PlayMode，且 Main 主界面或真实 BattleShop 已准备。");
            Require(!_running, "闪电链验收已经运行中。");
            VerifyAsync(false).Forget();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            if (!_running) VerifyAsync(true).Forget();
        }

        private static async UniTaskVoid VerifyAsync(bool fromLauncher)
        {
            _running = true;
            var report = new Report { utc = DateTime.UtcNow.ToString("o"), entry = fromLauncher ? "main Launcher -> MainMenu -> BeginBattle -> Main" : "Running MainMenu/BattleShop",
                fixture = "保留真实场景、商店、实体、弹丸和Combat；仅通过显式正数奖励接口补充抽取购买的验证资金；停止能源球并禁用Host自动Update；从真实出兵时间线集齐至少6敌人，冻结其移动并排列近邻；只以TryAttackFromContact模拟接触，不修改HP、伤害、配置或攻击计数。" };
            BattleShopSceneController host = null;
            BattleShopContext context = null;
            CombatRuntime combatRuntime = null;
            Action<CombatDamageContext> damageListener = null;
            var oldHostEnabled = true;
            var oldTimeScale = Time.timeScale;
            try
            {
                if (fromLauncher || Object.FindObjectOfType<BattleShopSceneController>()?.IsReady != true)
                {
                    await Wait(() => UIModule.IsValid && GameModule.UI.GetUI<MainMenuUI>() != null, 90f, "Main 主界面初始化");
                    await GameApp.BeginBattleFromMainMenu(GameModule.UI.GetUI<MainMenuUI>());
                }
                await Wait(() => (host = Object.FindObjectOfType<BattleShopSceneController>()) != null && host.IsReady &&
                    host.CanInteractWithRobots && host.IsCombatRunning, 30f, "真实 BattleShop 开战及交互开放");
                context = host.Context;
                combatRuntime = context.Combat;
                report.battleId = context.BattleId;
                oldHostEnabled = host.enabled;
                host.enabled = false;
                Time.timeScale = 0f;
                host.Bootstrap.StopBalls();
                var combat = host.Combat;
                var enemies = host.Enemies;
                Require(combat != null && enemies != null, "真实战斗控制器缺失。");
                var state = context.GetState(BattleSide.Player);
                var slot = -1;
                for (var attempt = 0; attempt < 120; attempt++)
                {
                    slot = Enumerable.Range(0, state.Slots.Count).Where(i => state.Slots[i].Offer?.RobotId == 701).DefaultIfEmpty(-1).First();
                    if (slot >= 0) break;
                    Require(context.Rewards.TryGrantEnemyKill(context.BattleId, 7010000 + attempt, BattleSide.Player, 50) ==
                        ShopOperationResult.Success, "补充真实商店抽取验证资金失败。");
                    Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Success, "抽取701时真实商店刷新失败。");
                    report.shopRefreshes++;
                }
                Require(slot >= 0, "120次真实刷新内没有抽到701，请核对演示候选与权重。");
                if (state.EnergyCoins < state.Slots[slot].Offer.Price)
                    Require(context.Rewards.TryGrantEnemyKill(context.BattleId, 7010200, BattleSide.Player, 50) ==
                        ShopOperationResult.Success, "补充701购买验证资金失败。");
                var coinsBefore = state.EnergyCoins;
                var offer = state.Slots[slot].Offer;
                Require(context.TryPurchase(BattleSide.Player, slot, offer.OfferId, out var robot) == ShopOperationResult.Success,
                    "701真实购买失败。");
                report.purchased = robot.ConfigId == 701 && coinsBefore - state.EnergyCoins == offer.Price;
                report.robotInstanceId = robot.InstanceId;
                report.robotLevel = robot.Level;
                Require(report.purchased, "购买型号或扣费不符。");
                var board = host.PlayerGlass.GetComponentInParent<PlacementBoardView>();
                OpenGlass(host, board);
                BoardCoordinate? anchor = null;
                for (var row = 0; row < board.Model.Rows && !anchor.HasValue; row++)
                    for (var column = 0; column < board.Model.Columns; column++)
                    {
                        var candidate = new BoardCoordinate(column, row);
                        if (!board.Model.Evaluate(candidate, robot.Definition).IsValid) continue;
                        anchor = candidate; break;
                    }
                Require(anchor.HasValue && context.TryBeginInteraction(BattleSide.Player, robot.InstanceId), "701没有合法部署位置或无法拿起。");
                Require(await host.DeployRobotAsync(robot.InstanceId, new BoardPlacementTarget(board, anchor.Value),
                    host.GetCancellationTokenOnDestroy()) == ShopOperationResult.Success, "701真实 DeployRobotAsync 失败。");
                report.deployed = robot.Location == RobotLocation.Board && Nullable.Equals(robot.Anchor, anchor) &&
                    Object.FindObjectsOfType<RobotView>().Any(v => v.InstanceId == robot.InstanceId);
                Require(report.deployed, "购买实体未原样部署到真实场景。");
                var behaviorField = typeof(BuffObj).GetField("Behavior", Private);
                Require(behaviorField != null, "找不到BuffObj.Behavior观察点。");
                var cycle = robot.Buffs.Items.Select(b => behaviorField.GetValue(b)).OfType<AttackCycleBuffBehavior>().Single();
                Require(cycle.AttackCount == 0, "701部署后循环计数不是0，运行中入口需要未攻击的新机器人。");

                // 使用本局已经预加载的真实波次生成，不构造替代世界或伪造敌人血量。
                for (var tick = 0; tick < 240 && enemies.GetBulletTargets(BattleSide.Player).Count < 6; tick++)
                {
                    FreezeEnemyMovement(context);
                    enemies.Advance(0.25f);
                }
                FreezeEnemyMovement(context);
                var targets = enemies.GetBulletTargets(BattleSide.Player).OrderBy(t => t.InstanceId).ToArray();
                Require(targets.Length >= 6, "真实波次未能准备至少6名敌人。");
                var cluster = host.GetBulletBounds(BattleSide.Player).center;
                cluster.z = targets[0].Position.z;
                cluster -= new Vector3(0.1f, 0.1f, 0f);
                var enemyViews = Object.FindObjectsOfType<EnemyView>().ToDictionary(v => v.InstanceId);
                for (var i = 0; i < targets.Length; i++)
                    enemyViews[targets[i].InstanceId].transform.position = cluster + new Vector3(0.1f * (i % 3), 0.1f * (i / 3), 0f);
                Debug.Log("[ChainLightning Play] 已真实购买部署701；敌人近邻夹具就绪，开始连续5次接触施法。");

                CombatActionSnapshot fifthAction = null;
                Dictionary<int, int> fifthHp = null;
                damageListener = damage =>
                {
                    if (fifthAction == null || damage.Action.SourceInstanceId != report.robotInstanceId ||
                        damage.Action.Sequence != fifthAction.Sequence) return;
                    var id = damage.Result.TargetInstanceId;
                    var before = fifthHp.TryGetValue(id, out var hp) ? hp : -1;
                    var after = ReadHp(context, id);
                    report.chainHits.Add(new HitEvidence { targetId = id, sequence = damage.Action.Sequence,
                        requestedDamage = damage.Result.RequestedDamage, appliedDamage = damage.Result.AppliedDamage,
                        hpBefore = before, hpAfter = after, sameSnapshot = ReferenceEquals(damage.Action, fifthAction),
                        actualHpDeltaMatched = before >= 0 && before - after == damage.Result.AppliedDamage });
                };
                combatRuntime.TargetResolved += damageListener;

                for (var attack = 1; attack <= 5; attack++)
                {
                    if (attack > 1) Advance(host, robot.Configuration.MinTriggerInterval + 0.01f);
                    var fired = false;
                    await Wait(() => fired = combat.TryAttackFromContact(context.BattleId, BattleSide.Player, robot.InstanceId),
                        15f, "第" + attack + "击弹丸资源准备");
                    Require(fired, "真实接触施法未成功。");
                    var view = Object.FindObjectsOfType<RobotBulletView>().Single(v => v.State != null &&
                        !v.State.IsResolved && v.State.Action.SourceInstanceId == robot.InstanceId);
                    var bullet = view.State;
                    var evidence = new ShotEvidence { attackNumber = attack, bulletId = bullet.Launcher.Model.Id,
                        cycleCountAfterLaunch = cycle.AttackCount, sequence = bullet.Action.Sequence };
                    report.shots.Add(evidence);
                    Require(evidence.bulletId == (attack == 5 ? 3600 : 3605) && cycle.AttackCount == attack % 5,
                        "弹丸替换或成功发射计数错误，第" + attack + "击。");
                    if (attack == 5)
                    {
                        fifthAction = bullet.Action;
                        fifthHp = enemies.GetBulletTargets(BattleSide.Player).ToDictionary(t => t.InstanceId, t => ReadHp(context, t.InstanceId));
                    }
                    for (var step = 0; step < 800 && !bullet.IsResolved; step++) Advance(host, 0.01f);
                    evidence.resolved = bullet.IsResolved;
                    Require(evidence.resolved, "真实直线弹丸在8战斗秒内没有结束。");
                    if (attack < 5) await UniTask.Yield();
                }
                report.fifthCycleReset = cycle.AttackCount == 0;
                Require(report.chainHits.Count >= 2 && report.chainHits.Select(h => h.targetId).Distinct().Count() == report.chainHits.Count &&
                    report.chainHits.All(h => h.appliedDamage > 0 && h.actualHpDeltaMatched), "第五击没有产生至少两名去重敌人的真实扣血。");
                report.sharedSnapshot = report.chainHits.All(h => h.sameSnapshot && h.sequence == fifthAction.Sequence);
                Require(report.sharedSnapshot, "各跳未使用同一个真实发射快照。");
                var effect = Object.FindObjectsOfType<LightningChainEffectView>().SingleOrDefault(v => v.gameObject.activeInHierarchy);
                var chain = effect != null ? effect.transform.Find("ChainSegments")
                    .GetComponentsInChildren<SpriteRenderer>().FirstOrDefault(r => r.enabled) : null;
                report.chainVisible = chain != null && chain.enabled && chain.sprite != null;
                Require(report.chainVisible, "链伤后未显示真实 LightningChainEffect 连线。");
                var geometry = new[] { chain.transform.position, chain.transform.localScale, chain.transform.eulerAngles };
                var sprite = chain.sprite;
                var clock = combatRuntime.BattleTime;
                host.SetFrozen(true);
                Advance(host, 1f);
                await UniTask.Yield();
                var pausedGeometry = new[] { chain.transform.position, chain.transform.localScale, chain.transform.eulerAngles };
                report.pauseHeldEffect = chain.enabled && geometry.SequenceEqual(pausedGeometry) &&
                    sprite == chain.sprite && Mathf.Approximately(clock, combatRuntime.BattleTime);
                Require(report.pauseHeldEffect, "业务暂停期间链线或战斗时钟仍推进。");
                var screenshotPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../plans/artifacts/chain-lightning-play.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
                ScreenCapture.CaptureScreenshot(screenshotPath);
                await UniTask.NextFrame();
                await UniTask.NextFrame();
                report.screenshot = File.Exists(screenshotPath) ? screenshotPath : "截图尚未写出，以JSON和实际场景检查为准";
                host.SetFrozen(false);
                Advance(host, effect.Lifetime + 0.01f);
                report.resumedEffectExpired = !effect.gameObject.activeSelf && !chain.enabled;
                Require(report.resumedEffectExpired, "恢复后表现没有按战斗时间到期回池。");
                host.EndBattle();
                await UniTask.NextFrame();
                report.battleEndCleared = context.IsEnded && context.Battle.Bullets.Count == 0 &&
                    Object.FindObjectsOfType<LightningChainEffectView>(true).Length == 0 &&
                    Object.FindObjectsOfType<RobotBulletView>(true).Length == 0;
                Require(report.battleEndCleared, "终局仍残留弹丸或闪电链表现。");
                report.passed = true;
                report.details = "真实Main入场、701抽取购买部署、3605/3605/3605/3605/3600、第五次计数归零、至少两敌同快照真实扣血、连线显示、业务暂停、恢复回池和终局清理均通过。";
            }
            catch (Exception exception)
            {
                report.details = exception.ToString();
                Debug.LogError("[ChainLightning Play] " + report.details);
            }
            finally
            {
                if (combatRuntime != null && damageListener != null) combatRuntime.TargetResolved -= damageListener;
                if (host != null)
                {
                    if (!report.passed && host.Context != null) host.EndBattle();
                    host.enabled = oldHostEnabled;
                }
                Time.timeScale = oldTimeScale;
                _running = false;
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../plans/artifacts/chain-lightning-play.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("[ChainLightning Play] " + (report.passed ? "PASS " : "FAIL ") + path);
            }
        }

        private static void FreezeEnemyMovement(BattleShopContext context)
        {
            var setSpeed = typeof(EnemyEntity).GetMethod("SetMoveSpeed", Private);
            foreach (var view in Object.FindObjectsOfType<EnemyView>())
                if (context.Registry.TryGet(view.InstanceId, out var entity) && entity is EnemyEntity enemy && enemy.IsAlive)
                    setSpeed.Invoke(enemy, new object[] { 0f });
        }

        private static int ReadHp(BattleShopContext context, int id) =>
            context.Registry.TryGet(id, out var entity) && entity is EnemyEntity enemy ? enemy.CurrentHp : 0;

        private static void Advance(BattleShopSceneController host, float delta) =>
            typeof(BattleShopSceneController).GetMethod("AdvanceBattle", Private).Invoke(host, new object[] { delta });

        private static void OpenGlass(BattleShopSceneController host, PlacementBoardView board)
        {
            for (var row = 1; row < board.Model.Rows - 1; row++)
                for (var column = 1; column < board.Model.Columns - 1; column++)
                {
                    GlassView glass;
                    while (host.PlayerGlass.TryGetView(new BoardCoordinate(column, row), out glass))
                    {
                        host.Context.Registry.AdvanceTime(1f);
                        Require(host.PlayerGlass.ApplyCollision(glass, host.Bootstrap.PlayerBall) != GlassCollisionResult.Ignored,
                            "真实玻璃碰撞无法打开部署区域。");
                    }
                }
        }

        private static async UniTask Wait(Func<bool> ready, float seconds, string stage)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!ready())
            {
                Require(Time.realtimeSinceStartup < deadline, "等待超时：" + stage);
                await UniTask.Yield();
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
