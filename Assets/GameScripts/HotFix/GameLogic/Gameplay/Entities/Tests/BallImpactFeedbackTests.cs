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
            BeginBounceAcceleration();
            _feedback.RecordCorner(new Vector2(-0.06f, 0.06f));
            _feedback.RecordCorner(Vector2.zero);
            _ball.transform.position = new Vector3(0.06f, 0.06f, 0f);
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
            BeginBounceAcceleration();
            _feedback.RecordCorner(Vector2.left * 0.1f);
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

        [Test]
        public void OrdinaryMotionAndZeroAccelerationHaveNoTrailOrGlow()
        {
            _ball.transform.position = Vector3.right * 0.2f;
            Physics2D.SyncTransforms();
            _feedback.Advance(0.04f);
            var renderer = _root.transform.Find("GoldenBallTrail").GetComponent<MeshRenderer>();
            Assert.IsFalse(renderer.enabled);
            Assert.AreEqual(0, _feedback.TrailPointCount);
            BeginBounceAcceleration(0f);
            _feedback.RecordCorner(Vector2.zero);
            _ball.transform.position = Vector3.right * 0.4f;
            Physics2D.SyncTransforms();
            _feedback.Advance(0.04f);
            Assert.IsFalse(renderer.enabled, "反弹未增加球速时不能显示短尾。");
            Assert.AreEqual(0, _feedback.TrailPointCount);
            Assert.IsFalse(_root.transform.Find("NormalTrail").GetComponent<ParticleSystem>().isPlaying);
        }

        [Test]
        public void AcceleratedTrailStaysShortFreezesAndFadesWithAcceleration()
        {
            BeginBounceAcceleration();
            var tick = typeof(PhysicalCollisionBall).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var frame = 0; frame < 7; frame++)
            {
                _ball.transform.position = Vector3.right * (frame * 0.09f);
                Physics2D.SyncTransforms();
                tick.Invoke(_ball, null);
                _feedback.Advance(0.02f);
            }
            var trail = _root.transform.Find("GoldenBallTrail");
            var renderer = trail.GetComponent<MeshRenderer>();
            var mesh = trail.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(renderer.enabled);
            var world = mesh.vertices.Select(vertex => trail.TransformPoint(vertex)).ToArray();
            var length = world.Max(vertex => vertex.x) - world.Min(vertex => vertex.x);
            Assert.LessOrEqual(length, _ball.WorldRadius * 3.5f + 0.0001f);
            Assert.Greater(length, _ball.WorldRadius * 2f);
            var peakAlpha = mesh.colors32.Max(color => color.a);
            Assert.GreaterOrEqual(peakAlpha, 240, "加速初期应保持清晰可见。");
            var progress = _ball.BounceAccelerationProgress;
            _ball.SetFrozen(true);
            tick.Invoke(_ball, null);
            _feedback.Advance(1f);
            Assert.AreEqual(progress, _ball.BounceAccelerationProgress);
            Assert.AreEqual(peakAlpha, mesh.colors32.Max(color => color.a));
            _ball.SetFrozen(false);
            for (var frame = 0; frame < 11; frame++) tick.Invoke(_ball, null);
            _feedback.Advance(0.02f);
            Assert.Less(mesh.colors32.Max(color => color.a), peakAlpha / 2, "加速末期短尾应整体变透明。");
            for (var frame = 0; frame < 10; frame++) tick.Invoke(_ball, null);
            _feedback.Advance(0.02f);
            Assert.IsFalse(renderer.enabled);
            Assert.AreEqual(0, _feedback.TrailPointCount);
        }

        private void BeginBounceAcceleration(float increase = 1f)
        {
            _ball.GlobalValueProvider = id => id == 11 ? increase : id == 12 ? 0.5f : id == 13 || id == 14 ? 1f : 0f;
            typeof(PhysicalCollisionBall).GetMethod("ApplyBounceAcceleration", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_ball, new object[] { false });
        }

        [TestCase(501)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        [TestCase(505)]
        [TestCase(601)]
        [TestCase(602)]
        [TestCase(603)]
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
                    var icon = robot.transform.Find("RobotIcon");
                    var mesh = icon.Find("RobotImpactOutline").GetComponent<MeshFilter>().sharedMesh;
                    outline.Advance(0.025f);
                    var startVertices = mesh.vertices;
                    var startColors = mesh.colors32;
                    var startCore = startVertices.Where((point, index) => startColors[index].a > 200).ToArray();
                    var sprite = icon.GetComponent<SpriteRenderer>().sprite;
                    Assert.Greater(sprite.GetPhysicsShapeCount(), 0);
                    var visiblePoints = new System.Collections.Generic.List<Vector2>();
                    var visibleBounds = new Bounds();
                    for (var path = 0; path < sprite.GetPhysicsShapeCount(); path++)
                    {
                        sprite.GetPhysicsShape(path, visiblePoints);
                        if (path == 0) visibleBounds = new Bounds(visiblePoints[0], Vector3.zero);
                        foreach (var point in visiblePoints) visibleBounds.Encapsulate(point);
                    }
                    var marginX = Mathf.Abs(icon.InverseTransformVector(robot.transform.TransformVector(
                        Vector3.right * (MapGridMath.REFERENCE_CELL_SIZE * 0.1f))).x);
                    var marginY = Mathf.Abs(icon.InverseTransformVector(robot.transform.TransformVector(
                        Vector3.up * (MapGridMath.REFERENCE_CELL_SIZE * 0.1f))).y);
                    Assert.GreaterOrEqual(startCore.Min(point => point.x), visibleBounds.min.x - marginX,
                        "第一圈描边应从图片可见边缘开始，不能预留额外外移空隙。");
                    Assert.LessOrEqual(startCore.Max(point => point.x), visibleBounds.max.x + marginX);
                    Assert.GreaterOrEqual(startCore.Min(point => point.y), visibleBounds.min.y - marginY);
                    Assert.LessOrEqual(startCore.Max(point => point.y), visibleBounds.max.y + marginY);
                    outline.Advance(0.055f);
                    Assert.IsTrue(outline.IsPlaying);
                    var vertices = mesh.vertices;
                    var colors = mesh.colors32;
                    var core = vertices.Where((point, index) => colors[index].a > 200).ToArray();
                    Assert.Greater(core.Distinct().Count(), original.Length * 2, "圆角需要包含连续的弧线采样。");
                    var minX = core.Min(point => point.x);
                    var minY = core.Min(point => point.y);
                    Assert.IsFalse(core.Any(point => Mathf.Abs(point.x - minX) < 0.00001f &&
                        Mathf.Abs(point.y - minY) < 0.00001f), "外凸角不应保留尖锐的直角交点。");
                    Assert.IsTrue(colors.Any(color => color.a == 0), "柔光边缘应自然渐隐到透明。");
                    Assert.IsTrue(vertices.All(point => !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                        !float.IsInfinity(point.x) && !float.IsInfinity(point.y)));
                    AssertOutlineLeavesEmptyCellsClear(mesh, robot.transform.Find("RobotIcon"),
                        robot.transform, config.ShapeId_Ref.CellOffsets);
                    CollectionAssert.AreEqual(original, collider.GetPath(0));
                    Assert.AreEqual(1, robot.GetComponentsInChildren<Collider2D>().Length);
                    outline.Advance(0.21f);
                    Assert.IsFalse(outline.IsPlaying);
                    Assert.AreEqual(0, mesh.vertexCount);
                }
                finally { Object.DestroyImmediate(robot); }
            }
        }

        private static void AssertOutlineLeavesEmptyCellsClear(Mesh mesh, Transform icon, Transform robot, int[][] cells)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (var x = cells.Min(cell => cell[0]); x <= cells.Max(cell => cell[0]); x++)
                for (var y = cells.Min(cell => cell[1]); y <= cells.Max(cell => cell[1]); y++)
                {
                    if (cells.Any(cell => cell[0] == x && cell[1] == y)) continue;
                    var point = (Vector2)icon.InverseTransformPoint(robot.TransformPoint(
                        new Vector3(x * MapGridMath.REFERENCE_CELL_SIZE, y * MapGridMath.REFERENCE_CELL_SIZE)));
                    for (var index = 0; index < triangles.Length; index += 3)
                    {
                        var a = (Vector2)vertices[triangles[index]];
                        var b = (Vector2)vertices[triangles[index + 1]];
                        var c = (Vector2)vertices[triangles[index + 2]];
                        if (Mathf.Abs(Cross(b - a, c - a)) < 0.00000001f) continue;
                        var first = Cross(b - a, point - a);
                        var second = Cross(c - b, point - b);
                        var third = Cross(a - c, point - c);
                        var inside = first >= 0f && second >= 0f && third >= 0f ||
                            first <= 0f && second <= 0f && third <= 0f;
                        Assert.IsFalse(inside, "圆角描边和柔光不应填满异形机器人的空格。");
                    }
                }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
