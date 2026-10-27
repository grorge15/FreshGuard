#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class BerserkPlaybackTests
    {
        private Scene _scene;
        private BattleEntityRegistry _registry;
        private PlacementBoardsBootstrap _bootstrap;
        private readonly List<PhysicalCollisionBall> _balls = new List<PhysicalCollisionBall>();
        private readonly List<RobotView> _robots = new List<RobotView>();
        private bool _running;
        private int _contacts;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("BerserkPhysics", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            _registry = new BattleEntityRegistry(); _running = true; _contacts = 0;
            var go = NewObject("Bootstrap"); _bootstrap = go.AddComponent<PlacementBoardsBootstrap>();
            var player = Ball("Player"); var opponent = Ball("Opponent");
            var playerExtra = new[] { Ball("PlayerExtra1"), Ball("PlayerExtra2") };
            var opponentExtra = new[] { Ball("OpponentExtra1"), Ball("OpponentExtra2") };
            Set(_bootstrap, "_ball", player); Set(_bootstrap, "_opponentBall", opponent);
            Set(_bootstrap, "_playerBerserkBalls", playerExtra); Set(_bootstrap, "_opponentBerserkBalls", opponentExtra);
            _bootstrap.BindBattle(100, () => _running, _registry, NewObject("Popups").transform);
            _bootstrap.RobotContactDetected += (ball, id) => _contacts++;
            _bootstrap.BerserkChanged += ApplyMode;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _bootstrap.UnbindBattle(); _registry.Dispose(); _balls.Clear(); _robots.Clear();
            yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator GlobalRulesControlRealTemporaryBallsAndTheirLifetime()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,
                "AssetRaw/Configs/bytes", name + ".bytes"))));
            var buf = new ByteBuf(); buf.WriteSize(tables.TbGlobal.DataList.Count);
            foreach (var row in tables.TbGlobal.DataList)
            {
                buf.WriteInt(row.Id);
                buf.WriteFloat(row.Id == 35 ? 3f : row.Id == 10 ? 2f : row.Id == 15 ? 0.2f : row.Value);
                var range = row.Id == 21 ? new[] { 270, 270 } : row.IntArrVal;
                buf.WriteSize(range.Length); foreach (var value in range) buf.WriteInt(value);
            }
            tables.TbGlobal = new GameConfig.globalcfg.TbGlobal(buf);
            _bootstrap.UnbindBattle();
            _bootstrap.PlayerBall.GlobalValueProvider = id => tables.TbGlobal.Get(id).Value;
            _bootstrap.OpponentBall.GlobalValueProvider = id => tables.TbGlobal.Get(id).Value;
            _bootstrap.ConfigureFromTables(tables);
            _bootstrap.BindBattle(100, () => _running, _registry, NewObject("ConfiguredPopups").transform);
            var robot = Robot("I"); var main = _bootstrap.PlayerBall;
            Approach(main, robot);
            typeof(PhysicalCollisionBall).GetProperty("ComboCount").SetValue(main, 1);
            TickUntil(() => main.IsBerserk);
            var active = _scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<PhysicalCollisionBall>(true))
                .Where(ball => ball.Side == BattleSide.Player && ball.IsLaunched).ToArray();
            Assert.AreEqual(4, active.Length, "配置3颗额外球时应有主球加3颗临时球。");
            foreach (var ball in active)
            {
                Assert.AreEqual(tables.TbGlobal.Get(3).Value, ball.Speed);
                Assert.AreEqual(_scene, ball.gameObject.scene);
                if (ball != main) Assert.That(ball.CurrentDirection.y, Is.EqualTo(-1f).Within(0.0001f));
            }
            _bootstrap.AdvanceBerserk(0.21f);
            Assert.IsFalse(main.IsBerserk);
            Assert.AreEqual(tables.TbGlobal.Get(2).Value, main.Speed);
            Assert.IsTrue(active.Where(ball => ball != main).All(ball => !ball.IsLaunched && !ball.gameObject.activeSelf));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThresholdSpawnsTwoBallsAndExpirationProtectsOverlappingMain()
        {
            var robot = Robot("L"); var main = _bootstrap.PlayerBall;
            EnterThroughCollision(main, robot);
            Assert.IsTrue(_bootstrap.IsBerserk(BattleSide.Player));
            Assert.IsFalse(_bootstrap.IsBerserk(BattleSide.Opponent));
            Assert.AreEqual(3, _balls.Count(b => b.Side == BattleSide.Player && b.IsLaunched));
            Assert.AreEqual(0, main.ComboCount);
            var extras = _balls.Where(b => b != main && b.Side == BattleSide.Player).ToArray();
            foreach (var extra in extras)
            {
                Assert.IsTrue(extra.IsBerserk);
                Assert.That(Vector2.Distance(main.GetComponent<Rigidbody2D>().position, extra.GetComponent<Rigidbody2D>().position), Is.LessThan(0.01f));
                Assert.IsTrue(Physics2D.GetIgnoreCollision(main.GetComponent<Collider2D>(), extra.GetComponent<Collider2D>()));
            }
            Assert.AreNotEqual(extras[0].CurrentDirection, extras[1].CurrentDirection);
            var body = robot.GetPhysicalCollider();
            main.GetComponent<Rigidbody2D>().position = robot.transform.Find("Cell_0_0").position;
            Physics2D.SyncTransforms();
            _bootstrap.AdvanceBerserk(5.9f); Assert.IsTrue(main.IsBerserk);
            _bootstrap.AdvanceBerserk(0.11f);
            Assert.IsFalse(main.IsBerserk);
            Assert.IsTrue(main.IsLaunched);
            Assert.AreEqual(1, _balls.Count(b => b.Side == BattleSide.Player && b.IsLaunched));
            Assert.IsTrue(extras.All(b => !b.gameObject.activeSelf));
            AssertRobotCollisionMode(robot, false);
            Assert.IsTrue(Physics2D.GetIgnoreCollision(main.GetComponent<Collider2D>(), body));
            var contactCount = _contacts;
            main.SendMessage("FixedUpdate"); _scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.AreEqual(contactCount, _contacts); Assert.AreEqual(0, main.ComboCount);
            main.GetComponent<Rigidbody2D>().position = new Vector2(50f, 50f);
            Physics2D.SyncTransforms(); main.SendMessage("FixedUpdate");
            Assert.IsFalse(Physics2D.GetIgnoreCollision(main.GetComponent<Collider2D>(), body));
            Approach(main, robot); TickUntil(() => main.ComboCount == 1);
            Assert.AreEqual(1, main.ComboCount); Assert.Greater(_contacts, contactCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TriggerCrossesRobotCellsOnceAndOnlyReentryMakesAnotherRequest()
        {
            var robot = Robot("I"); var ball = _balls[2];
            robot.SetBerserkTrigger(true); ball.gameObject.SetActive(true); ball.SetBerserk(true);
            Physics2D.SyncTransforms();
            var body = robot.GetPhysicalCollider();
            var y = robot.transform.Find("Cell_0_0").position.y;
            var start = new Vector2(body.bounds.min.x - 0.6f, y);
            ball.LaunchAt(start, 0f);
            var end = body.bounds.max.x + 0.6f;
            for (var i = 0; i < 100 && ball.GetComponent<Rigidbody2D>().position.x <= end; i++) Tick();
            Assert.Greater(ball.GetComponent<Rigidbody2D>().position.x, end);
            Assert.AreEqual(1, _contacts);
            Assert.AreEqual(0, ball.ComboCount); Assert.AreEqual(0, ball.BounceCount);
            ball.GetComponent<Rigidbody2D>().position = start;
            Physics2D.SyncTransforms(); ball.SendMessage("FixedUpdate");
            for (var i = 0; i < 100 && _contacts == 1; i++) Tick();
            Assert.AreEqual(2, _contacts);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ModeSwitchPreservesContactAndStopClearsBothSides()
        {
            var player = Robot("I"); var opponent = Robot("T", BattleSide.Opponent);
            opponent.transform.position = Vector3.right * 20f;
            var mainContacts = 0;
            _bootstrap.RobotContactDetected += (ball, id) => { if (ball == _bootstrap.PlayerBall) mainContacts++; };
            EnterThroughCollision(_bootstrap.PlayerBall, player);
            Assert.AreEqual(1, _contacts, "第50次Collision切Trigger不能重复处理主球接触。");
            Tick(); Tick();
            Assert.AreEqual(1, mainContacts, "切换后的真实Trigger回调也必须保留主球接触去重。");
            Assert.IsTrue(player.GetComponentsInChildren<Collider2D>().All(c => c.isTrigger));
            AssertRobotCollisionMode(opponent, false);
            EnterThroughCollision(_bootstrap.OpponentBall, opponent);
            Assert.IsTrue(_bootstrap.IsBerserk(BattleSide.Opponent));
            _bootstrap.StopBalls();
            Assert.IsTrue(_balls.All(b => !b.IsLaunched && !b.IsBerserk));
            Assert.IsFalse(_bootstrap.IsBerserk(BattleSide.Player));
            Assert.IsFalse(_bootstrap.IsBerserk(BattleSide.Opponent));
            foreach (var view in _robots) AssertRobotCollisionMode(view, false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedTemporaryLaunchRollsBackAllBallsAndRobotMode()
        {
            var robot = Robot("I"); var main = _bootstrap.PlayerBall;
            Set(_balls[3], "speed", 0f);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("能源球停止.*速度与半径"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("暴走启动失败.*临时暴走球发射失败"));
            Approach(main, robot);
            typeof(PhysicalCollisionBall).GetProperty("ComboCount").SetValue(main, 49);
            TickUntil(() => main.ComboCount == 0);
            Assert.IsFalse(_bootstrap.IsBerserk(BattleSide.Player));
            Assert.AreEqual(0f, _bootstrap.BerserkRemaining(BattleSide.Player));
            Assert.IsTrue(main.IsLaunched && !main.IsBerserk);
            Assert.IsTrue(_balls.Skip(2).All(b => !b.IsLaunched && !b.gameObject.activeSelf && !b.IsBerserk));
            AssertRobotCollisionMode(robot, false);
            yield return null;
        }

        private void EnterThroughCollision(PhysicalCollisionBall main, RobotView robot)
        {
            Approach(main, robot);
            typeof(PhysicalCollisionBall).GetProperty("ComboCount").SetValue(main, 49);
            TickUntil(() => main.IsBerserk);
        }
        private void Approach(PhysicalCollisionBall ball, RobotView robot)
        {
            Physics2D.SyncTransforms();
            var top = robot.transform.Cast<Transform>().Where(c => c.name.StartsWith("Cell_", StringComparison.Ordinal))
                .OrderByDescending(c => c.position.y).First();
            ball.LaunchAt(new Vector2(top.position.x, top.position.y + 0.8f), 270f);
        }
        private void TickUntil(Func<bool> done)
        {
            for (var i = 0; i < 50 && !done(); i++) Tick();
            Assert.IsTrue(done(), "真实物理接触未达到预期状态。");
        }
        private void Tick()
        {
            foreach (var ball in _balls) if (ball.gameObject.activeInHierarchy) ball.SendMessage("FixedUpdate");
            _scene.GetPhysicsScene2D().Simulate(0.02f);
        }
        private void ApplyMode(BattleSide side, bool active)
        {
            var views = _robots.Where(r => r.TryGetComboRobot(_registry, side, out _)).ToArray();
            if (!active) (side == BattleSide.Player ? _bootstrap.PlayerBall : _bootstrap.OpponentBall).ProtectOverlappingRobots(views);
            foreach (var view in views) view.SetBerserkTrigger(active);
        }
        private static void AssertRobotCollisionMode(RobotView robot, bool bodyTrigger)
        {
            var body = robot.GetComponent<PolygonCollider2D>();
            Assert.IsNotNull(body);
            Assert.AreEqual(bodyTrigger, body.isTrigger);
            CollectionAssert.AreEqual(new Collider2D[] { body }, robot.GetComponentsInChildren<Collider2D>(true));
        }
        private PhysicalCollisionBall Ball(string name)
        {
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab"));
            SceneManager.MoveGameObjectToScene(go, _scene); go.name = name;
            var ball = go.GetComponent<PhysicalCollisionBall>(); ball.Stop(); go.transform.localScale = Vector3.one;
            ball.GlobalValueProvider = key => key == 15 ? 6f : key == 34 ? 1.5f : 0f;
            _balls.Add(ball); return ball;
        }
        private RobotView Robot(string shape, BattleSide side = BattleSide.Player)
        {
            shape = shape == "I" ? "Cannon" : shape == "L" ? "Scatter" : "Impact";
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/Robot" + shape + ".prefab"));
            SceneManager.MoveGameObjectToScene(go, _scene);
            var view = go.GetComponent<RobotView>(); var robot = _registry.CreateRobot(view.ConfigId, side, 1, 1);
            typeof(RobotEntity).GetMethod("TrySetLocation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(robot,
                new object[] { RobotLocation.Board, new BoardCoordinate(0, 0) });
            view.Bind(_registry, robot.InstanceId); _robots.Add(view); return view;
        }
        private GameObject NewObject(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, _scene); return go;
        }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
#endif
