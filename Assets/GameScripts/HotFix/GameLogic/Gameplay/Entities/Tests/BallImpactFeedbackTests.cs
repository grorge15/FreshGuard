using System.IO;
using System.Linq;
using System.Reflection;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class BallImpactFeedbackTests
    {
        private GameObject _root;
        private PhysicalCollisionBall _ball;
        private BallImpactFeedback _feedback;

        [SetUp]
        public void SetUp()
        {
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab"));
            _ball = _root.GetComponent<PhysicalCollisionBall>();
            if (_root.GetComponent<BallImpactFeedback>() == null)
                typeof(PhysicalCollisionBall).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_ball, null);
            _feedback = _root.GetComponent<BallImpactFeedback>();
            Assert.IsNotNull(_feedback);
            Assert.IsTrue(_feedback.IsReady);
            _ball.GlobalValueProvider = key => 0f;
            _ball.BindBattle(1, BattleSide.Player, () => true);
            _ball.LaunchAt(Vector2.zero, 45f);
        }

        [TearDown]
        public void TearDown() { if (_root != null) Object.DestroyImmediate(_root); }

        [Test]
        public void GlassBurstSurvivesFreezeThenExpiresWithoutChangingBallPhysics()
        {
            var body = _ball.GetComponent<Rigidbody2D>();
            var velocity = body.velocity;
            var scale = _ball.transform.localScale;
            _feedback.PlayGlass(Vector2.zero, Vector2.up);
            _feedback.Advance(0.08f);
            var renderer = _root.transform.Find("GoldenBallImpacts").GetComponent<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            Assert.Greater(vertices.Length, 0);
            _ball.SetFrozen(true);
            _feedback.Advance(10f);
            CollectionAssert.AreEqual(vertices, mesh.vertices, "暂停不能消耗玻璃反馈的时长或移动水滴。");
            Assert.AreEqual(1, _feedback.ActiveBurstCount);
            _ball.SetFrozen(false);
            _feedback.Advance(0.06f);
            Assert.IsTrue(renderer.enabled, "水滴在 0.14 秒仍有收尾。");
            _feedback.Advance(0.07f);
            Assert.AreEqual(0, _feedback.ActiveBurstCount);
            Assert.IsFalse(renderer.enabled);
            Assert.AreEqual(velocity, body.velocity);
            Assert.AreEqual(scale, _ball.transform.localScale);
        }

        [Test]
        public void GlassVariationDoesNotConsumeGameplayRandomValues()
        {
            var saved = Random.state;
            try
            {
                Random.InitState(73421);
                var before = Random.state;
                for (var i = 0; i < 32; i++) _feedback.PlayGlass(Vector2.zero, Vector2.up);
                var afterFeedback = Random.value;
                Random.state = before;
                Assert.AreEqual(Random.value, afterFeedback, "水滴的随机变化不能影响反弹和其他玩法的随机数。");
            }
            finally { Random.state = saved; }
        }

        [TestCase(0f, 1f)]
        [TestCase(-1f, 0f)]
        public void GlassDropletsRemainWarmColoredAndOutsideTheHitSurface(float x, float y)
        {
            var normal = new Vector2(x, y);
            _feedback.PlayGlass(Vector2.zero, normal);
            _feedback.Advance(0.08f);
            var node = _root.transform.Find("GoldenBallImpacts");
            var mesh = node.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, 0);
            foreach (var color in mesh.colors32)
                Assert.IsTrue(color.r > color.g && color.g > color.b, "普通撞击只保留金色反馈，不混入冰蓝碎片。");
            foreach (var vertex in mesh.vertices)
                Assert.GreaterOrEqual(Vector2.Dot(node.TransformPoint(vertex), normal), -0.0001f,
                    "水滴应向接触面的外侧散开。");
        }

        [Test]
        public void DenseImpactsReuseTwoMeshesAndStopClearsEverything()
        {
            for (var i = 0; i < 100; i++) _feedback.PlayWall(Vector2.right * i * 0.001f, Vector2.up);
            Assert.AreEqual(BallImpactFeedback.MaximumBursts, _feedback.ActiveBurstCount);
            Assert.AreEqual(2, _root.GetComponentsInChildren<MeshFilter>(true).Length);
            _feedback.Advance(0.02f);
            _ball.Stop();
            _feedback.Advance(1f);
            Assert.AreEqual(0, _feedback.ActiveBurstCount);
            Assert.AreEqual(0, _feedback.TrailPointCount);
            Assert.IsFalse(_root.GetComponentsInChildren<MeshRenderer>().Any(renderer => renderer.enabled));
        }

        [Test]
        public void ReflectionCornerPersistsAndTeleportOrBerserkClearsNormalTrail()
        {
            _feedback.RecordCorner(new Vector2(-0.3f, 0.3f));
            _feedback.RecordCorner(Vector2.zero);
            _ball.transform.position = new Vector3(0.3f, 0.3f, 0f);
            Physics2D.SyncTransforms();
            _feedback.Advance(0.04f);
            Assert.GreaterOrEqual(_feedback.TrailPointCount, 3);
            var trail = _root.transform.Find("GoldenBallTrail").GetComponent<MeshRenderer>();
            Assert.IsTrue(trail.enabled);
            _ball.SetBerserk(true);
            Assert.AreEqual(0, _feedback.TrailPointCount);
            Assert.IsFalse(trail.enabled);
            _ball.SetBerserk(false);
            _feedback.Advance(0.02f);
            _ball.LaunchAt(Vector2.one * 20f, 45f);
            Assert.AreEqual(0, _feedback.TrailPointCount, "重新发射不能在新旧位置之间连出长线。");
        }

        [Test]
        public void TemporaryBallOwnsItsMeshesAndCannotDestroyTheMainBallTrail()
        {
            _feedback.Advance(0.02f);
            var mainMesh = _root.transform.Find("GoldenBallTrail").GetComponent<MeshFilter>().sharedMesh;
            var temporary = _ball.CreateTemporary();
            try
            {
                // EditMode does not invoke Awake automatically on the cloned object.
                typeof(PhysicalCollisionBall).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(temporary, null);
                var copy = temporary.transform.Find("GoldenBallTrail").GetComponent<MeshFilter>().sharedMesh;
                Assert.AreNotSame(mainMesh, copy);
            }
            finally { Object.DestroyImmediate(temporary.gameObject); }
            Assert.IsNotNull(mainMesh);
            Assert.Greater(mainMesh.vertexCount, 0);
        }

        [TestCase(502)]
        [TestCase(504)]
        [TestCase(505)]
        public void RobotOutlinePreservesConcaveCornersAndDoesNotModifyCollider(int id)
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,
                "AssetRaw/Configs/bytes", name + ".bytes"))));
            var config = tables.TbRobot.Get(id);
            using (var registry = new BattleEntityRegistry())
            {
                var entity = registry.CreateRobotFromConfig(config, BattleSide.Player);
                entity.TrySetLocation(RobotLocation.Board, new BoardCoordinate(1, 1));
                var robot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab"));
                try
                {
                    var view = robot.GetComponent<RobotView>();
                    view.Bind(registry, entity.InstanceId);
                    var collider = view.GetPhysicalCollider();
                    var original = collider.GetPath(0);
                    view.PlayCollisionFeedback();
                    var outline = robot.GetComponent<RobotImpactOutline>();
                    outline.Advance(0.08f);
                    Assert.IsTrue(outline.IsPlaying);
                    var mesh = robot.transform.Find("RobotIcon/RobotImpactOutline").GetComponent<MeshFilter>().sharedMesh;
                    // Three narrow ribbons around each real edge, preserving the L/T concavity.
                    Assert.AreEqual(original.Length * 3 * 6, mesh.vertexCount);
                    CollectionAssert.AreEqual(original, collider.GetPath(0));
                    Assert.AreEqual(1, robot.GetComponentsInChildren<Collider2D>().Length);
                    outline.Advance(0.21f);
                    Assert.IsFalse(outline.IsPlaying);
                    Assert.AreEqual(0, mesh.vertexCount);
                }
                finally { Object.DestroyImmediate(robot); }
            }
        }
    }
}
