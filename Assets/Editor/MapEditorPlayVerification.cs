using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>从启动场景进入PlayMode后显式运行；只改运行实例，不保存场景或地图。</summary>
    public static class MapEditorPlayVerification
    {
        private const string LAUNCH_PENDING = "FreshGuard.MapVerification.LaunchPending";
        private const string LAUNCH_ACTIVE = "FreshGuard.MapVerification.LaunchActive";
        private const string TEST_MAP = "FreshGuard.MapVerification.LayerMap";
        private const string TEST_LARGE_MAP = "FreshGuard.MapVerification.LargeLayerMap";
        private static bool _runningVerification;

        [InitializeOnLoadMethod]
        private static void RegisterLauncherVerification()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("FreshGuard/Map/Verify via TEngine Launcher")]
        public static void RunViaLauncher()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在停止运行、编译完成后启动Launcher验证。");
            for (var index = 0; index < UnityEngine.SceneManagement.SceneManager.sceneCount; index++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty)
                    throw new InvalidOperationException("场景有未保存修改，请先处理后再通过Launcher验证。");
            // 直接复用工具栏Launcher按钮的实际入口，不复制其场景选择和返回逻辑。
            var helper = typeof(TEngine.UnityToolbarExtenderLeft).GetNestedType("SceneHelper", BindingFlags.NonPublic);
            var start = helper?.GetMethod("StartScene", BindingFlags.Public | BindingFlags.Static);
            if (start == null) throw new InvalidOperationException("未找到当前TEngine Launcher入口。");
            PrepareVerificationAssets();
            SessionState.SetBool(LAUNCH_PENDING, true);
            try { start.Invoke(null, new object[] { "main" }); }
            catch { SessionState.SetBool(LAUNCH_PENDING, false); throw; }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(LAUNCH_PENDING, false)) return;
            SessionState.SetBool(LAUNCH_PENDING, false);
            SessionState.SetBool(LAUNCH_ACTIVE, true);
            Run();
        }

        [MenuItem("FreshGuard/Map/Verify Running Demo Map")]
        public static void Run()
        {
            if (_runningVerification) return;
            RunAsync().Forget();
        }

        private static async UniTaskVoid RunAsync()
        {
            _runningVerification = true;
            var checks = new List<string>();
            BattleShopSceneController host = null;
            string originalLocation = null;
            var passed = false;
            try
            {
                Require(Application.isPlaying, "需要从Assets/Scenes/main.unity启动PlayMode。");
                Require(SessionState.GetBool(LAUNCH_ACTIVE, false), "请使用FreshGuard/Map/Verify via TEngine Launcher启动完整验证。");
                await UniTask.WaitUntil(() => UnityEngine.Object.FindObjectOfType<BattleShopSceneController>() != null)
                    .Timeout(TimeSpan.FromSeconds(30));
                host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                await UniTask.WaitUntil(() => host.IsReady).Timeout(TimeSpan.FromSeconds(30));
                originalLocation = host.MapLocation;
                checks.Add("PASS 通过TEngine工具栏Launcher的SceneHelper.StartScene启动，框架初始化后进入业务Main");
                // 先恢复上一局网格倍率，再读取场景作者基准。
                host.EndBattle();
                await UniTask.Yield();
                var originalCamera = host.Placement.SceneCamera;
                var originalCameraPosition = originalCamera.transform.position;
                var originalCameraRect = originalCamera.rect;
                var originalCameraSize = originalCamera.orthographicSize;
                var originalCameraRotation = originalCamera.transform.rotation;
                var player = (PlacementBoardView)typeof(BattleShopSceneController).GetField("_playerBoard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var opponent = (PlacementBoardView)typeof(BattleShopSceneController).GetField("_opponentBoard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var playerScale = player.transform.localScale;
                var opponentScale = opponent.transform.localScale;
                var fixedLayout = CaptureFixedLayout(host, player, opponent);
                SetLocation(host, SessionState.GetString(TEST_MAP, ""));
                await host.InitializeAsync();
                Require(host.IsReady && host.UsesTerrainMap, "二进制地图未就绪。");
                Require(player.Model.Columns == 11 && player.Model.Rows == 9, "地图尺寸错误。");
                Require(host.PlayerGlass.LiveCount == 4 && host.OpponentGlass.LiveCount == 4, "应分别生成2普通+2彩色玻璃，覆盖四种底层组合。");
                var track = new BoardCoordinate(0, 0);
                Require(player.Model.GetBaseTerrain(track) == BaseTerrainType.Track && !player.Model.CanPlaceUnit(track) &&
                    player.Model.CanEnergyBallPass(track), "轨道许可错误。");
                Require(player.Model.TryPlace(new BoardCoordinate(4, 3), new PlaceableDefinition("map-verification", 1, 1)), "空位部署失败。");
                player.Model.Release("map-verification");
                checks.Add("PASS 二进制资源加载、双方99格与四种玻璃组合、轨道通行禁部署、空位部署");
                var camera = host.Placement.SceneCamera;
                var initialPlayer = player.transform.position;
                var initialOpponent = opponent.transform.position;
                var initialCamera = camera.transform.position;
                var initialZoom = camera.orthographicSize;
                Require(Vector3.Distance(playerScale, player.transform.localScale) < 0.0001f &&
                    Vector3.Distance(opponentScale, opponent.transform.localScale) < 0.0001f, "11×9参考地图改变了棋盘原始缩放。");
                RequireFixedLayout(fixedLayout, originalCamera, originalCameraPosition, originalCameraRotation, originalCameraSize, originalCameraRect);
                checks.Add($"PASS 11×9参考图倍率1；相机Position/Rotation/Size={initialZoom:G5}/Rect及固定布局不变");
                Require(initialOpponent.y > initialPlayer.y, "双方上下布局方向错误。");
                var context = host.Context;
                var beforeCoins = context.GetState(BattleSide.Player).EnergyCoins;
                await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(8));
                var ball = host.Bootstrap.PlayerBall;
                var otherBall = host.Bootstrap.OpponentBall;
                Require(ball.IsLaunched && otherBall.IsLaunched, "双方能源球未发射。");
                ball.Stop(); otherBall.Stop();
                checks.Add("PASS 准备期后双方能源球发射");

                await VerifyGlassBreakAsync(host, player, opponent, ball, new BoardCoordinate(2, 2),
                    new BoardCoordinate(2, 4), 270f, BaseTerrainType.Empty, ObstacleType.GlassNormal, 3, 10);
                checks.Add("PASS 普通玻璃+Empty：真实物理3次命中，保留底层并可部署，奖励10币、双方隔离、重复命中不重复奖励");
                await VerifyGlassBreakAsync(host, player, opponent, ball, new BoardCoordinate(7, 6),
                    new BoardCoordinate(7, 4), 90f, BaseTerrainType.Empty, ObstacleType.GlassColored, 5, 30);
                checks.Add("PASS 彩色玻璃+Empty：真实物理5次命中，保留底层并可部署，奖励30币");
                await VerifyGlassBreakAsync(host, player, opponent, ball, new BoardCoordinate(8, 2),
                    new BoardCoordinate(8, 4), 270f, BaseTerrainType.Track, ObstacleType.GlassNormal, 3, 10);
                checks.Add("PASS 普通玻璃+Track：破碎后球可通行、仍不可部署，底图保持Track，奖励10币");
                await VerifyGlassBreakAsync(host, player, opponent, ball, new BoardCoordinate(2, 6),
                    new BoardCoordinate(2, 4), 90f, BaseTerrainType.Track, ObstacleType.GlassColored, 5, 30);
                checks.Add("PASS 彩色玻璃+Track：破碎后球可通行、仍不可部署，底图保持Track，奖励30币");
                Require(context.GetState(BattleSide.Player).EnergyCoins == beforeCoins + 80, "四组合奖励总额错误。");
                Require(context.GetState(BattleSide.Opponent).EnergyCoins == beforeCoins, "奖励污染对手。");

                host.EndBattle();
                await UniTask.Yield();
                await host.InitializeAsync();
                Require(host.IsReady && host.PlayerGlass.LiveCount == 4 && host.OpponentGlass.LiveCount == 4, "新局未恢复初始地形。");
                Require(Vector3.Distance(initialPlayer, player.transform.position) < 0.001f &&
                    Vector3.Distance(initialOpponent, opponent.transform.position) < 0.001f &&
                    Vector3.Distance(initialCamera, camera.transform.position) < 0.001f && Mathf.Abs(initialZoom - camera.orthographicSize) < 0.001f,
                    "重开布局或相机发生漂移。");
                checks.Add("PASS 重开恢复初始地图与布局，无状态串局或布局漂移");
                host.EndBattle();
                await UniTask.Yield();
                SetLocation(host, SessionState.GetString(TEST_LARGE_MAP, ""));
                await host.InitializeAsync();
                Require(host.IsReady && player.Columns == 13 && player.Rows == 13, "13×13独立测试地图加载失败。");
                var fit = 9f / 13f;
                Require(Mathf.Abs(player.transform.localScale.x - playerScale.x * fit) < 0.0001f &&
                    Mathf.Abs(opponent.transform.localScale.y - opponentScale.y * fit) < 0.0001f &&
                    Mathf.Abs(ball.MapScale - fit) < 0.0001f && Mathf.Abs(ball.EffectiveSpeed - ball.Speed * fit) < 0.0001f,
                    "13×13网格或球体尺寸/速度倍率错误。");
                Require(player.transform.position == initialPlayer && opponent.transform.position == initialOpponent, "13×13改变了棋盘中心。");
                RequireFixedLayout(fixedLayout, originalCamera, originalCameraPosition, originalCameraRotation, originalCameraSize, originalCameraRect);
                foreach (var cell in new[] { new BoardCoordinate(0, 0), new BoardCoordinate(6, 6), new BoardCoordinate(12, 12) })
                {
                    BoardCoordinate actual;
                    Require(player.TryGetCoordinate(player.GetCellWorldPosition(cell), out actual) && actual.Equals(cell), "缩放后世界坐标往返错误。");
                }
                checks.Add("PASS 独立V2地图13×13：倍率9/13、双方中心及所有固定节点不变、坐标往返、球体速度同步");
                await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(8));
                Require(Vector2.Distance(ball.GetComponent<Rigidbody2D>().velocity, ball.CurrentDirection * ball.EffectiveSpeed) < 0.01f,
                    "缩放后球体实际物理速度错误。");
                host.EndBattle();
                await UniTask.Yield();
                await host.InitializeAsync();
                Require(Mathf.Abs(player.transform.localScale.x - playerScale.x * fit) < 0.0001f, "13×13重开累计缩放。");
                var layoutController = host.GetComponent<BattleMapLayoutController>();
                layoutController.enabled = false;
                Require(camera.rect == originalCameraRect && Mathf.Abs(camera.orthographicSize - originalCameraSize) < 0.001f &&
                    Vector3.Distance(camera.transform.position, originalCameraPosition) < 0.001f &&
                    Vector3.Distance(player.transform.localScale, playerScale) < 0.0001f && ball.MapScale == 1f, "禁用网格适配未恢复棋盘和球体。");
                layoutController.enabled = true;
                checks.Add("PASS 13×13连续重开无累计；OnDisable恢复网格和球体，相机始终不变");
                host.EndBattle();
                await UniTask.Yield();
                SetLocation(host, "");
                await host.InitializeAsync();
                Require(host.IsReady && !host.UsesTerrainMap && host.PlayerGlass.LiveCount == 84 && host.OpponentGlass.LiveCount == 84,
                    "旧布局兼容失败。");
                Require(Vector3.Distance(player.transform.localScale, playerScale) < 0.0001f && ball.MapScale == 1f, "旧图没有恢复原始倍率。");
                RequireFixedLayout(fixedLayout, originalCamera, originalCameraPosition, originalCameraRotation, originalCameraSize, originalCameraRect);
                checks.Add("PASS 空地址恢复旧布局：双方84块玻璃");
                passed = true;
            }
            catch (Exception e) { checks.Add("FAIL " + e); Debug.LogError("[Map Play Verification] " + e); }
            finally
            {
                if (host != null && originalLocation != null)
                {
                    host.EndBattle();
                    await UniTask.Yield();
                    SetLocation(host, originalLocation);
                    await host.InitializeAsync();
                }
                File.WriteAllText(Path.Combine(Application.dataPath, "../Library/MapEditorPlayVerification.txt"),
                    (passed ? "PASS" : "FAIL") + Environment.NewLine + string.Join(Environment.NewLine, checks));
                if (passed) Debug.Log("[Map Play Verification] PASS " + string.Join("；", checks));
                _runningVerification = false;
                if (SessionState.GetBool(LAUNCH_ACTIVE, false))
                {
                    SessionState.SetBool(LAUNCH_ACTIVE, false);
                    EditorApplication.isPlaying = false;
                }
            }
        }

        // 仅从已确认工程的Editor入口创建独立临时资源，不读写用户地图。
        private static void PrepareVerificationAssets()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var map = MapData.Create();
            for (var row = 0; row < map.Rows; row++)
                for (var column = 0; column < map.Columns; column++)
                    if (row == 0 || column == 0 || row == map.Rows - 1 || column == map.Columns - 1)
                        map.SetBaseTerrain(new BoardCoordinate(column, row), BaseTerrainType.Track);
            map.SetObstacle(new BoardCoordinate(2, 2), ObstacleType.GlassNormal);
            map.SetObstacle(new BoardCoordinate(7, 6), ObstacleType.GlassColored);
            map.SetBaseTerrain(new BoardCoordinate(8, 2), BaseTerrainType.Track);
            map.SetObstacle(new BoardCoordinate(8, 2), ObstacleType.GlassNormal);
            map.SetBaseTerrain(new BoardCoordinate(2, 6), BaseTerrainType.Track);
            map.SetObstacle(new BoardCoordinate(2, 6), ObstacleType.GlassColored);
            WriteVerificationMap(TEST_MAP, "Map_VerifyLayers_" + suffix, map);
            WriteVerificationMap(TEST_LARGE_MAP, "Map_VerifyLarge_" + suffix, MapData.Create(13, 13));
        }

        private static void WriteVerificationMap(string key, string location, MapData map)
        {
            var path = "Assets/AssetRaw/Configs/Maps/" + location + ".bytes";
            File.WriteAllBytes(path, MapBinarySerializer.Serialize(map));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            SessionState.SetString(key, location);
        }

        public static void CleanupVerificationAssets()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("停止运行并等待编译后才能清理临时验证地图。");
            foreach (var key in new[] { TEST_MAP, TEST_LARGE_MAP })
            {
                var location = SessionState.GetString(key, "");
                if (location.StartsWith("Map_Verify", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset("Assets/AssetRaw/Configs/Maps/" + location + ".bytes");
                SessionState.EraseString(key);
            }
        }

        private static async UniTask VerifyGlassBreakAsync(BattleShopSceneController host, PlacementBoardView player,
            PlacementBoardView opponent, PhysicalCollisionBall ball, BoardCoordinate cell, BoardCoordinate origin,
            float angle, BaseTerrainType baseTerrain, ObstacleType obstacle, int hits, int reward)
        {
            var context = host.Context;
            Require(player.Model.GetBaseTerrain(cell) == baseTerrain && player.Model.GetObstacle(cell) == obstacle &&
                !player.Model.CanPlaceUnit(cell) && !player.Model.CanEnergyBallPass(cell), "玻璃破碎前的两层或阻挡规则错误。");
            Require(host.PlayerGlass.TryGetView(cell, out var view), "组合玻璃缺失。");
            var coins = context.GetState(BattleSide.Player).EnergyCoins;
            for (var hit = 0; hit < hits; hit++)
            {
                await UniTask.Delay(280);
                await HitGlassAsync(context, ball, view, player.GetCellWorldPosition(origin), angle);
            }
            Require(player.Model.GetObstacle(cell) == ObstacleType.None && player.Model.GetBaseTerrain(cell) == baseTerrain &&
                player.Model.CanEnergyBallPass(cell) && player.Model.CanPlaceUnit(cell) == (baseTerrain == BaseTerrainType.Empty),
                "破碎后未保留底层规则。");
            Require(opponent.Model.GetObstacle(cell) == obstacle && opponent.Model.GetBaseTerrain(cell) == baseTerrain,
                "破碎污染对方地图。");
            Require(context.GetState(BattleSide.Player).EnergyCoins == coins + reward, "玻璃奖励错误。");
            Require(host.PlayerGlass.ApplyCollision(view, ball) == GlassCollisionResult.Ignored &&
                context.GetState(BattleSide.Player).EnergyCoins == coins + reward, "重复命中重复发奖。");
        }

        private static void SetLocation(BattleShopSceneController host, string location) =>
            typeof(BattleShopSceneController).GetField("_mapLocation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, location);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        private static async UniTask HitGlassAsync(BattleShopContext context, PhysicalCollisionBall ball,
            GlassView target, Vector3 position, float angle)
        {
            Entity entity;
            Require(context.Registry.TryGet(target.InstanceId, out entity) && entity is GlassEntity, "目标玻璃实体已丢失。");
            var glass = (GlassEntity)entity;
            var durability = glass.CurrentDurability;
            ball.LaunchAt(position, angle);
            try
            {
                // 反弹计数也包含墙壁，必须等待指定玻璃真正受伤，避免把其他碰撞算作一次命中。
                await UniTask.WaitUntil(() => target.IsBroken || glass.CurrentDurability < durability || context.IsEnded)
                    .Timeout(TimeSpan.FromSeconds(3));
                Require(!context.IsEnded, "命中验证期间对局已结束。");
                Require(target.IsBroken || glass.CurrentDurability < durability, "目标玻璃未受伤。");
            }
            finally { ball.Stop(); }
        }

        private static Dictionary<Transform, Matrix4x4> CaptureFixedLayout(BattleShopSceneController host,
            PlacementBoardView player, PlacementBoardView opponent)
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            foreach (var root in host.gameObject.scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                {
                    if (node == player.transform || node == opponent.transform ||
                        IsGridContent(node, player.transform) || IsGridContent(node, opponent.transform) ||
                        node.IsChildOf(host.Bootstrap.PlayerBall.transform) || node.IsChildOf(host.Bootstrap.OpponentBall.transform)) continue;
                    result[node] = node.localToWorldMatrix;
                }
            // 棋盘中心和旋转另由场景节点及重开检查覆盖；此处排除网格倍率本身。
            return result;
        }

        private static bool IsGridContent(Transform node, Transform board)
        {
            if (!node.IsChildOf(board)) return false;
            for (var current = node; current != board; current = current.parent)
                if (current.name == "Slots" || current.name == "Boundary" || current.name == "Glasses" ||
                    current.GetComponent<RobotView>() != null) return true;
            return false;
        }

        private static void RequireFixedLayout(Dictionary<Transform, Matrix4x4> nodes, Camera camera,
            Vector3 position, Quaternion rotation, float size, Rect rect)
        {
            Require(camera.transform.position == position && camera.transform.rotation == rotation &&
                camera.orthographicSize == size && camera.rect == rect, "相机场景配置发生变化。");
            foreach (var pair in nodes)
            {
                if (pair.Key == null) continue;
                for (var index = 0; index < 16; index++)
                    Require(Mathf.Abs(pair.Key.localToWorldMatrix[index] - pair.Value[index]) < 0.0001f,
                        "固定场景节点发生移动/缩放：" + pair.Key.name);
            }
        }
    }
}
