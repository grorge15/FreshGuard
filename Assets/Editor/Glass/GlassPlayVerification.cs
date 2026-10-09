using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>从启动场景运行，在真实Physics2D及商店经济链路上验证玻璃。</summary>
    public static class GlassPlayVerification
    {
        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string error;
            public List<string> checks = new List<string>();
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [MenuItem("FreshGuard/Glass/Verify Running Glass")]
        public static void Verify() { VerifyAsync().Forget(); }

        private static async UniTaskVoid VerifyAsync()
        {
            var report = new Report();
            BattleShopSceneController host = null;
            try
            {
                Require(Application.isPlaying, "需要从Assets/Scenes/main.unity进入PlayMode。");
                await UniTask.WaitUntil(() => UnityEngine.Object.FindObjectOfType<BattleShopSceneController>() != null)
                    .Timeout(TimeSpan.FromSeconds(20));
                host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                await UniTask.WaitUntil(() => host.IsReady).Timeout(TimeSpan.FromSeconds(20));
                host.EndBattle();
                await host.InitializeAsync();
                Require(host.IsReady, "新局初始化失败。");
                var player = host.Bootstrap.PlayerBall;
                var opponent = host.Bootstrap.OpponentBall;
                var context = host.Context;
                var board = host.PlayerGlass.GetComponentInParent<PlacementBoardView>();
                Require(host.PlayerGlass.LiveCount == 84 && host.OpponentGlass.LiveCount == 84, "双方初始玻璃应为84块。");
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    var normal = 0;
                    var colored = 0;
                    for (var x = 0; x < 11; x++)
                        for (var y = 0; y < 9; y++)
                            if (context.Registry.TryGetGlass(side, new BoardCoordinate(x, y), out var glass))
                            {
                                if (glass.GlassKind == GlassKind.Normal) normal++;
                                else { colored++; Require(glass.Coordinate.Equals(new BoardCoordinate(5, 6)), "彩色位置错误。"); }
                            }
                    Require(normal == 83 && colored == 1, "每方应为83普通+1彩色。");
                }
                var firstBattle = context.BattleId;
                await host.InitializeAsync();
                Require(host.Context.BattleId == firstBattle && host.PlayerGlass.LiveCount == 84, "同局重复初始化新增对象。");
                Require(!player.IsLaunched && !opponent.IsLaunched, "准备期提前发射。");
                host.SetFrozen(true);
                var countdown = host.PreparationRemaining;
                await UniTask.Delay(120, ignoreTimeScale: true);
                Require(host.PreparationRemaining == countdown && !player.IsLaunched, "冻结仍推进准备时间。");
                host.SetFrozen(false);
                await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(8));
                Require(player.IsLaunched && opponent.IsLaunched && Vector2.Dot(player.CurrentDirection, opponent.CurrentDirection) > 0.99f,
                    "双方未同时以共享方向发射。");
                player.Stop(); opponent.Stop();
                report.checks.Add("每方84块、83普通+1彩色、同局幂等、准备期冻结及双方同向发射");

                var normalCell = new BoardCoordinate(2, 4);
                Require(host.PlayerGlass.TryGetView(normalCell, out var normalView), "普通玻璃视图缺失。");
                var normalId = normalView.InstanceId;
                Require(!board.Model.IsOpen(normalCell), "完整玻璃格不能部署。");
                await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(4, 4)), 180f);
                Require(context.Registry.TryGet(normalId, out var normalEntity) && ((GlassEntity)normalEntity).CurrentDurability == 2 &&
                    player.CurrentDirection.x > 0f && normalView.StageIndex == 1, "普通球反弹、损耗或轻裂错误。");
                Require(normalView.Collide(player) == GlassCollisionResult.Ignored, "共享冷却没有拦截。");
                host.SetFrozen(true);
                var cooldown = ((GlassEntity)normalEntity).DamageCooldownRemaining;
                var frozenPosition = player.transform.position;
                await UniTask.Delay(120, ignoreTimeScale: true);
                Require(((GlassEntity)normalEntity).DamageCooldownRemaining == cooldown && player.transform.position == frozenPosition,
                    "冻结仍推进冷却或球位置。");
                Require(normalView.Collide(player) == GlassCollisionResult.Ignored, "冻结仍允许扣耐久。");
                host.SetFrozen(false);
                await UniTask.Delay(280);
                await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(4, 4)), 180f);
                Require(normalView.StageIndex == 0, "普通玻璃第二击未切换重裂。");
                await UniTask.Delay(280);
                await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(4, 4)), 180f);
                Require(!context.Registry.TryGet(normalId, out _) && board.Model.IsOpen(normalCell) &&
                    normalView != null && !normalView.GetComponent<Collider2D>().enabled, "破碎未立即移除碰撞或开放内部格。");
                Require(context.GetState(BattleSide.Player).EnergyCoins == 30 && context.GetState(BattleSide.Opponent).EnergyCoins == 20,
                    "普通破碎奖励或所属方错误。");
                Require(ReadCoins() == "30", "普通玻璃奖励没有更新真实UI余额。");
                GameEvent.Send<BattleRewardEvent>(BattleRewardAdapter.REWARD_EVENT,
                    new BattleRewardEvent(context.BattleId, BattleSide.Player, ShopRewardSource.NormalGlass, normalId));
                Require(context.GetState(BattleSide.Player).EnergyCoins == 30, "重复奖励没有去重。");
                report.checks.Add("普通3击破碎、真实反弹、冷却、冻结、裂纹、内部开放及10币仅到账一次");

                var coloredCell = new BoardCoordinate(5, 6);
                Require(host.PlayerGlass.TryGetView(coloredCell, out var coloredView), "彩色玻璃缺失。");
                var coloredId = coloredView.InstanceId;
                player.SetBerserk(true);
                await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(5, 4)), 90f);
                Require(context.Registry.TryGet(coloredId, out var coloredEntity) && ((GlassEntity)coloredEntity).CurrentDurability == 5 &&
                    ((GlassEntity)coloredEntity).DamageCooldownRemaining == 0f && player.CurrentDirection.y < 0f,
                    "暴走没有反弹，或错误扣耐久/刷新冷却。");
                player.SetBerserk(false);
                for (var hit = 1; hit <= 5; hit++)
                {
                    await UniTask.Delay(280);
                    await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(5, 4)), 90f);
                    if (hit < 5)
                    {
                        Require(context.Registry.TryGet(coloredId, out var live) && ((GlassEntity)live).CurrentDurability == 5 - hit,
                            "彩色玻璃每击应扣1。");
                        Require(coloredView.StageIndex == (hit == 1 ? 2 : hit < 4 ? 1 : 0), "彩色裂纹阶段错误。");
                    }
                }
                Require(context.GetState(BattleSide.Player).EnergyCoins == 60 && context.GetState(BattleSide.Opponent).EnergyCoins == 20 &&
                    !context.Registry.TryGet(coloredId, out _) && board.Model.IsOpen(coloredCell), "彩色奖励30币或破碎格开放错误。");
                Require(ReadCoins() == "60", "彩色玻璃奖励没有更新真实UI余额。");
                report.checks.Add("暴走真实反弹不损耗、不占冷却，彩色5击、三阶段及30币奖励");

                // 打通左侧走廊后验证外围玻璃破碎与实际墙壁反弹。
                foreach (var cell in new[] { new BoardCoordinate(1, 4), new BoardCoordinate(0, 4) })
                {
                    Require(host.PlayerGlass.TryGetView(cell, out var view), "外围验收走廊玻璃缺失。");
                    var id = view.InstanceId;
                    for (var hit = 0; hit < 3; hit++)
                    {
                        await UniTask.Delay(280);
                        await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(4, 4)), 180f);
                    }
                    Require(!context.Registry.TryGet(id, out _), "走廊玻璃未破碎。");
                }
                Require(!board.Model.IsOpen(new BoardCoordinate(0, 4)), "外围破碎后错误开放部署。");
                await Hit(player, board.GetCellWorldPosition(new BoardCoordinate(4, 4)), 180f);
                Require(player.CurrentDirection.x > 0f, "外围破碎后墙壁没有继续反弹。");
                report.checks.Add("内部走廊可通行，外围破碎永久禁部署且外墙仍阻挡");

                var beforeCorner = new Dictionary<int, int>();
                for (var x = 0; x < 11; x++)
                    for (var y = 0; y < 9; y++)
                        if (context.Registry.TryGetGlass(BattleSide.Player, new BoardCoordinate(x, y), out var glass))
                            beforeCorner.Add(glass.InstanceId, glass.CurrentDurability);
                var corner = board.transform.TransformPoint(board.GetCellLocalPosition(new BoardCoordinate(7, 5)) +
                    new Vector3(board.CellSize * 0.5f, board.CellSize * 0.5f));
                var cornerStart = board.GetCellWorldPosition(new BoardCoordinate(5, 4));
                var cornerDirection = corner - cornerStart;
                await Hit(player, cornerStart, Mathf.Atan2(cornerDirection.y, cornerDirection.x) * Mathf.Rad2Deg);
                var cornerDamaged = false;
                foreach (var pair in beforeCorner)
                {
                    Require(context.Registry.TryGet(pair.Key, out var live), "角落单次命中意外破碎。");
                    var loss = pair.Value - ((GlassEntity)live).CurrentDurability;
                    Require(loss >= 0 && loss <= 1, "角落多接触点重复扣除同块耐久。");
                    cornerDamaged |= loss == 1;
                }
                Require(cornerDamaged, "角落碰撞没有交给玻璃逻辑。");
                report.checks.Add("实际角落命中反弹，同一物理步同块玻璃最多损耗1");

                var oldBattle = context.BattleId;
                host.EndBattle();
                Require(!player.IsLaunched && !opponent.IsLaunched && host.Context == null, "终局没有停止双方球或清理上下文。");
                await host.InitializeAsync();
                Require(host.PlayerGlass.LiveCount == 84 && host.OpponentGlass.LiveCount == 84 &&
                    host.Context.GetState(BattleSide.Player).EnergyCoins == 20, "新局没有重置玻璃或能源币。");
                GameEvent.Send<BattleRewardEvent>(BattleRewardAdapter.REWARD_EVENT,
                    new BattleRewardEvent(oldBattle, BattleSide.Player, ShopRewardSource.ColoredGlass, coloredId));
                Require(host.Context.GetState(BattleSide.Player).EnergyCoins == 20, "旧局奖励污染新局。");
                host.EndBattle();
                var canceled = host.InitializeAsync();
                host.EndBattle();
                var replacement = host.InitializeAsync();
                await canceled;
                await replacement;
                Require(host.IsReady && host.PlayerGlass.LiveCount == 84 && host.OpponentGlass.LiveCount == 84,
                    "取消初始化的旧回调破坏了新局。");
                await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(8));
                Require(player.IsLaunched && opponent.IsLaunched, "逻辑终局验收前双方应已运动。");
                host.Context.EndBattle();
                Require(host.Context == null && !player.IsLaunched && !opponent.IsLaunched &&
                    !player.GetComponent<Rigidbody2D>().simulated && !opponent.GetComponent<Rigidbody2D>().simulated &&
                    player.GetComponent<Rigidbody2D>().velocity == Vector2.zero && opponent.GetComponent<Rigidbody2D>().velocity == Vector2.zero,
                    "运动中逻辑终局没有立即停球和清理。");
                var interrupted = host.InitializeAsync();
                Require(host.Context != null, "初始化没有建立待准备上下文。");
                host.Context.EndBattle();
                await interrupted;
                Require(host.Context == null, "初始化期间终局留下无法重试的上下文。");
                await host.InitializeAsync();
                Require(host.IsReady && host.PlayerGlass.LiveCount == 84 && host.OpponentGlass.LiveCount == 84,
                    "初始化期间终局后无法重开。");
                report.checks.Add("终局停止、新局重置、旧奖励隔离、初始化取消与立即重试、逻辑终局及初始化中终局");
                report.passed = true;
            }
            catch (Exception e) { report.error = e.ToString(); Debug.LogError("[Glass Verification] " + e); }
            finally
            {
                if (host != null)
                {
                    host.EndBattle();
                    await host.InitializeAsync();
                }
                var path = Path.Combine(Application.dataPath, "../Library/GlassPlayVerification.json");
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                if (report.passed) Debug.Log("[Glass Verification] PASS " + string.Join("；", report.checks));
            }
        }

        private static async UniTask Hit(PhysicalCollisionBall ball, Vector3 start, float angle)
        {
            ball.LaunchAt(start, angle);
            Require(ball.IsLaunched, "验收球发射失败。");
            await UniTask.WaitUntil(() => ball.BounceCount > 0).Timeout(TimeSpan.FromSeconds(3));
            ball.Stop();
        }

        private static string ReadCoins()
        {
            var ui = GameModule.UI.GetUI<BattleShopUI>();
            Require(ui != null, "能源币UI不存在。");
            return ui.transform.Find("m_rect_ShopPanel/m_text_EnergyCoins").GetComponent<Text>().text;
        }
    }
}
