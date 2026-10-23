#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class RobotCombatPlaybackTests
    {
        [UnityTest]
        public IEnumerator ShopOfferDeployedDuringBerserkUsesTriggersAndDragRestoreKeepsMode()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && ConfigSystem.Instance.Tables != null)
            {
                host.EndBattle();
                host.SendMessage("Start");
            }
            else
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                host = null;
            }
            var deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host != null && host.IsCombatRunning) break;
                yield return null;
            }
            Assert.IsNotNull(host);
            Assert.IsTrue(host.IsCombatRunning);
            try
            {
                // 阈值和真实碰撞由 BerserkPlaybackTests 覆盖，这里只准备暴走状态，走实际商品拖拽入口。
                typeof(PlacementBoardsBootstrap).GetMethod("EnterBerserk", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(host.Bootstrap, new object[] { host.Bootstrap.PlayerBall });
                Assert.IsTrue(host.Bootstrap.IsBerserk(BattleSide.Player));
                host.SetFrozen(true);
                var slot = host.Context.GetState(BattleSide.Player).Slots.First(s => s.Offer != null);
                var source = RobotDragSource.Offer(slot.SlotId, slot.Offer.OfferId);
                GameConfig.robot.Robot config;
                int level;
                PlaceableDefinition definition;
                Assert.AreEqual(ShopOperationResult.Success, host.Context.GetDragContent(BattleSide.Player, source,
                    out config, out level, out definition));
                var board = Object.FindObjectsOfType<PlacementBoardView>()
                    .Single(b => host.Placement.GetBoardSide(b) == BattleSide.Player);
                BoardCoordinate? anchor = null;
                for (var row = 0; row < board.Model.Rows && !anchor.HasValue; row++)
                    for (var column = 0; column < board.Model.Columns && !anchor.HasValue; column++)
                    {
                        var candidate = new BoardCoordinate(column, row);
                        if (board.Model.Evaluate(candidate, definition).IsValid) anchor = candidate;
                    }
                Assert.IsTrue(anchor.HasValue);
                RobotDragSession session;
                Assert.AreEqual(ShopOperationResult.Success, host.Context.TryBeginDrag(BattleSide.Player, source, out session));
                yield return host.DeployDragAsync(session, new BoardPlacementTarget(board, anchor.Value), CancellationToken.None)
                    .ToCoroutine(result => Assert.AreEqual(ShopOperationResult.Success, result));
                var view = Object.FindObjectsOfType<RobotView>().Single();
                var colliders = view.GetPhysicalColliders();
                Assert.IsTrue(colliders.All(c => c.enabled && c.isTrigger), "新部署机器人启用时必须已是 Trigger。");
                Assert.AreEqual(ShopOperationResult.Success, host.Context.TryBeginDrag(BattleSide.Player,
                    RobotDragSource.Owned(view.InstanceId), out session));
                Assert.IsTrue(colliders.All(c => !c.enabled));
                host.Context.CancelInteraction(session.InteractionId);
                Assert.IsTrue(colliders.All(c => c.enabled && c.isTrigger), "取消拖拽恢复时不能重新变为实体障碍。");
                host.Bootstrap.StopBalls();
                Assert.IsTrue(colliders.All(c => c.enabled && !c.isTrigger));
            }
            finally { host.EndBattle(); }
        }

        [UnityTest]
        public IEnumerator LaunchSceneDeploysBothSidesFreezesHitsAndRestartsWithoutGhostBullets()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            // 同批次已有启动场景时重开对局，避免重新注册仍在运行的全局框架。
            if (host != null && ConfigSystem.Instance.Tables != null)
            {
                host.EndBattle();
                host.SendMessage("Start");
            }
            else
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                host = null;
            }
            var deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host != null && host.IsReady) break;
                yield return null;
            }
            Assert.IsNotNull(host);
            Assert.IsTrue(host.IsReady, "真实 YooAsset 资源和战斗初始化应完成");
            Assert.AreEqual(0, BulletResourceReferences(), "未激活的预检子弹也必须归还资源引用");
            Assert.AreEqual(0, BulletAssetSpawnCount(), "预检后资源池使用计数必须归零");
            Assert.AreEqual(0, host.Enemies.LiveCount);
            host.SetFrozen(true);
            // 本测试显式提交接触，停止实体球以保持伤害及资源计数断言确定。
            host.Bootstrap.StopBalls();
            RobotEntity player = null;
            var deployed = new List<RobotEntity>();
            foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
            {
                var offer = host.Context.GetState(side).Slots[0].Offer;
                RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.Success, host.Context.TryPurchase(side, 0, offer.OfferId, out robot));
                if (side == BattleSide.Player) player = robot;
                deployed.Add(robot);
                var board = Object.FindObjectsOfType<PlacementBoardView>().Single(b => host.Placement.GetBoardSide(b) == side);
                BoardCoordinate? anchor = null;
                for (var row = 0; row < board.Model.Rows && !anchor.HasValue; row++)
                    for (var column = 0; column < board.Model.Columns && !anchor.HasValue; column++)
                    {
                        var candidate = new BoardCoordinate(column, row);
                        if (board.Model.Evaluate(candidate, robot.Definition).IsValid) anchor = candidate;
                    }
                Assert.IsTrue(anchor.HasValue);
                RobotDragSession session;
                Assert.AreEqual(ShopOperationResult.Success, host.Context.TryBeginDrag(side, RobotDragSource.Owned(robot.InstanceId), out session));
                yield return host.DeployDragAsync(session, new BoardPlacementTarget(board, anchor.Value), CancellationToken.None)
                    .ToCoroutine(result => Assert.AreEqual(ShopOperationResult.Success, result));
            }
            host.SetFrozen(false);
            deadline = Time.realtimeSinceStartup + 20f;
            while (!host.IsCombatRunning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.IsCombatRunning);
            // host 在准备倒计时结束时会重新发射双方球，必须在战斗启动后再停止。
            host.Bootstrap.StopBalls();
            while (BulletAssetSpawnCount() < deployed.Count && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(deployed.Count, BulletAssetSpawnCount(), "双方待发子弹应已准备完成");
            Assert.Greater(host.Enemies.LiveCount, 0);
            var noContactUntil = Time.realtimeSinceStartup + 0.25f;
            while (Time.realtimeSinceStartup < noContactUntil) yield return null;
            Assert.AreEqual(0, host.Combat.LiveBulletCount, "资源和目标就绪后，超过触发间隔仍不能自动攻击");
            Assert.IsTrue(deployed.All(robot => robot.TriggerCooldownRemaining == 0f));
            deadline = Time.realtimeSinceStartup + 20f;
            RobotBulletView[] bullets = null;
            var contacted = new HashSet<int>();
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var robot in deployed)
                    if (!contacted.Contains(robot.InstanceId) &&
                        TryContact(host, robot)) contacted.Add(robot.InstanceId);
                bullets = Object.FindObjectsOfType<RobotBulletView>();
                if (bullets.Length >= 2 && bullets.All(b => b.Elapsed > 0.05f)) break;
                yield return null;
            }
            Assert.IsTrue(host.IsCombatRunning);
            Assert.AreEqual(2, contacted.Count, "双方机器人必须通过接触入口成功发射");
            Assert.AreEqual(2, bullets.Length);
            Assert.AreEqual(2, bullets.Select(b => b.Side).Distinct().Count());
            Assert.Greater(BulletAssetSpawnCount(), 0, "飞行中的真实子弹应占用对应资源引用");
            foreach (var bullet in bullets)
            {
                Entity target;
                Assert.IsTrue(host.Context.Registry.TryGet(bullet.TargetInstanceId, out target));
                Assert.AreEqual(bullet.Side, ((EnemyEntity)target).Side);
                Assert.AreEqual(Color.white, bullet.GetComponent<SpriteRenderer>().color);
            }
            var oldBullet = bullets.Single(b => b.Side == BattleSide.Player);
            var targetId = oldBullet.TargetInstanceId;
            var position = oldBullet.transform.position;
            var elapsed = oldBullet.Elapsed;
            var cooldown = player.TriggerCooldownRemaining;
            host.SetFrozen(true);
            var frozenUntil = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < frozenUntil) yield return null;
            Assert.AreEqual(position, oldBullet.transform.position);
            Assert.AreEqual(elapsed, oldBullet.Elapsed);
            Assert.AreEqual(cooldown, player.TriggerCooldownRemaining);
            Capture(host.Placement.SceneCamera);
            host.SetFrozen(false);
            deadline = Time.realtimeSinceStartup + 2f;
            while (oldBullet != null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(oldBullet == null, "恢复后原子弹应正常到达并回收");
            Entity hitTarget;
            Assert.IsTrue(host.Context.Registry.TryGet(targetId, out hitTarget));
            Assert.Less(((EnemyEntity)hitTarget).CurrentHp, ((EnemyEntity)hitTarget).MaxHp);
            var oldBattle = host.Context.BattleId;
            host.EndBattle(); host.EndBattle();
            yield return null;
            Assert.AreEqual(0, Object.FindObjectsOfType<RobotBulletView>().Length);
            Assert.AreEqual(0, Object.FindObjectsOfType<RobotView>().Length);
            Assert.AreEqual(0, BulletResourceReferences(), "在途和未发射子弹均不得残留资源引用");
            Assert.AreEqual(0, BulletAssetSpawnCount(), "显式释放及下一帧 OnDestroy 后不能漏还或重复归还");
            host.SendMessage("Start");
            deadline = Time.realtimeSinceStartup + 30f;
            while (!host.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.IsReady);
            Assert.AreNotEqual(oldBattle, host.Context.BattleId);
            Assert.AreEqual(0, host.Combat.LiveBulletCount);
            Assert.AreEqual(0, host.Enemies.LiveCount);
            Assert.AreEqual(0, BulletResourceReferences());
            Assert.AreEqual(0, BulletAssetSpawnCount());
            host.EndBattle();
        }

        private static bool TryContact(BattleShopSceneController host, RobotEntity robot)
            => host.Combat.TryAttackFromContact(host.Context.BattleId, robot.Side, robot.InstanceId);

        private static int BulletResourceReferences()
        {
            var references = (Dictionary<GameObject, AssetsReference>)typeof(AssetsReference)
                .GetField("_originalRefs", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            // 保留被销毁键也计数，捕获从未激活实例未收到 OnDestroy 的泄漏。
            var sourceField = typeof(AssetsReference).GetField("sourceGameObject", BindingFlags.Instance | BindingFlags.NonPublic);
            return references.Values.Count(reference =>
            {
                var source = (GameObject)sourceField.GetValue(reference);
                return source != null && source.GetComponent<RobotBulletView>() != null;
            });
        }

        private static int BulletAssetSpawnCount()
        {
            var resource = GameModule.Resource;
            var pool = resource.GetType().GetField("_assetPool", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(resource);
            var infos = (ObjectInfo[])pool.GetType().GetMethod("GetAllObjectInfos", System.Type.EmptyTypes).Invoke(pool, null);
            return infos.Where(info => info.Name.Contains("RobotBullet")).Sum(info => info.SpawnCount);
        }

        private static void Capture(Camera camera)
        {
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            var texture = new RenderTexture(900, 1600, 24);
            var image = new Texture2D(900, 1600, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 900, 1600), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "freshguard-robot-combat.png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = active;
                Object.Destroy(texture); Object.Destroy(image);
            }
        }
    }
}
#endif
