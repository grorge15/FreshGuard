#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            var cell = robot.GetComponentsInChildren<BoxCollider2D>().First();
            main.GetComponent<Rigidbody2D>().position = cell.bounds.center;
            Physics2D.SyncTransforms();
            _bootstrap.AdvanceBerserk(5.9f); Assert.IsTrue(main.IsBerserk);
            _bootstrap.AdvanceBerserk(0.11f);
            Assert.IsFalse(main.IsBerserk);
            Assert.IsTrue(main.IsLaunched);
            Assert.AreEqual(1, _balls.Count(b => b.Side == BattleSide.Player && b.IsLaunched));
            Assert.IsTrue(extras.All(b => !b.gameObject.activeSelf));
            AssertRobotCollisionMode(robot, false);
            Assert.IsTrue(Physics2D.GetIgnoreCollision(main.GetComponent<Collider2D>(), cell));
            var contactCount = _contacts;
            main.SendMessage("FixedUpdate"); _scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.AreEqual(contactCount, _contacts); Assert.AreEqual(0, main.ComboCount);
            main.GetComponent<Rigidbody2D>().position = new Vector2(50f, 50f);
            Physics2D.SyncTransforms(); main.SendMessage("FixedUpdate");
            Assert.IsFalse(Physics2D.GetIgnoreCollision(main.GetComponent<Collider2D>(), cell));
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
            var boxes = robot.GetComponentsInChildren<BoxCollider2D>().OrderBy(c => c.bounds.center.x).ToArray();
            var y = boxes[0].bounds.center.y;
            var start = new Vector2(boxes[0].bounds.min.x - 0.6f, y);
            ball.LaunchAt(start, 0f);
            var end = boxes.Last().bounds.max.x + 0.6f;
            for (var i = 0; i < 100 && ball.GetComponent<Rigidbody2D>().position.x < end; i++) Tick();
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
            var top = robot.GetComponentsInChildren<BoxCollider2D>().OrderByDescending(c => c.bounds.max.y).First();
            ball.LaunchAt(new Vector2(top.bounds.center.x, top.bounds.max.y + 0.8f), 270f);
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
            foreach (var cell in robot.GetComponentsInChildren<BoxCollider2D>())
                Assert.IsTrue(cell.isTrigger, "格子碰撞体必须始终是触发器。");
        }
        private PhysicalCollisionBall Ball(string name)
        {
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab"));
            SceneManager.MoveGameObjectToScene(go, _scene); go.name = name;
            var ball = go.GetComponent<PhysicalCollisionBall>(); ball.Stop(); go.transform.localScale = Vector3.one;
            ball.GlobalValueProvider = key => key == 41 ? 6f : key == 34 ? 1.5f : 0f;
            _balls.Add(ball); return ball;
        }
        private RobotView Robot(string shape, BattleSide side = BattleSide.Player)
        {
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
