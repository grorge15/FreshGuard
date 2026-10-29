using System;
using System.IO;
using System.Linq;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace GameLogic.Tests
{
    public class RobotContentTests
    {
        private Tables _tables;

        [SetUp]
        public void SetUp()
        {
            _tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
        }

        [TestCase(501, "CannonRobotEntity")]
        [TestCase(502, "ScatterRobotEntity")]
        [TestCase(503, "PiercingRobotEntity")]
        [TestCase(504, "RapidFireRobotEntity")]
        [TestCase(505, "ImpactRobotEntity")]
        [TestCase(601, "FrostRobotEntity")]
        [TestCase(602, "TractorRobotEntity")]
        [TestCase(603, "PoisonRobotEntity")]
        public void ConfigCreatesConcreteRobotAndKeepsRegistryLifecycle(int configId, string typeName)
        {
            var config = _tables.TbRobot.Get(configId);
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(config, BattleSide.Opponent, 2, 5);
                Assert.AreEqual(typeName, robot.GetType().Name);
                Assert.AreEqual(configId, robot.ConfigId);
                Assert.AreEqual(config.ShapeId, robot.ShapeId);
                Assert.AreEqual(config.SkillId, robot.SkillId);
                Assert.AreEqual(2, robot.Level);
                Assert.AreEqual(config.QualityId, robot.QualityId);
                Assert.AreEqual(config.ShapeId_Ref.CellOffsets.Length, robot.Definition.Cells.Count);
                var board = new BoardModel(5, 5);
                Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 0), robot.Definition));
                for (var x = 0; x < 5; x++)
                    for (var y = 0; y < 5; y++)
                        Assert.AreEqual(Array.Exists(config.ShapeId_Ref.CellOffsets, cell => cell[0] == x && cell[1] == y),
                            board.IsOccupied(new BoardCoordinate(x, y)), "只能占用配置指定的格子");
                Assert.AreEqual(BattleSide.Opponent, robot.Side);
                Entity registered;
                Assert.IsTrue(registry.TryGet(robot.InstanceId, out registered));
                Assert.AreSame(robot, registered);
                Assert.IsTrue(robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(0, 0)));
                Assert.IsTrue(robot.CanParticipate);
                Assert.IsTrue(robot.TryUpgrade());
                EntityRemovalSnapshot removal;
                Assert.IsTrue(registry.Remove(robot.InstanceId, EntityRemovalReason.Consumed, out removal));
                Assert.AreEqual(3, removal.RobotLevel);
                Assert.IsTrue(robot.IsRemoved);
                Assert.IsFalse(robot.TryUpgrade());
            }
        }

        [Test]
        public void TwoRobotsOfSameConfigKeepDifferentBoardOccupants()
        {
            using (var registry = new BattleEntityRegistry())
            {
                var config = _tables.TbRobot.Get(501);
                var first = registry.CreateRobotFromConfig(config, BattleSide.Player);
                var second = registry.CreateRobotFromConfig(config, BattleSide.Player);
                var board = new BoardModel(4, 2);
                Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 0), first.Definition));
                Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 1), second.Definition));
                Assert.AreNotEqual(board.GetOccupant(new BoardCoordinate(0, 0)), board.GetOccupant(new BoardCoordinate(0, 1)),
                    "同型号的两台机器人必须保留独立实例身份");
            }
        }

        [Test]
        public void InvalidCreationDoesNotConsumeAnInstanceId()
        {
            using (var registry = new BattleEntityRegistry())
            {
                var config = _tables.TbRobot.Get(501);
                Assert.Throws<ArgumentNullException>(() => registry.CreateRobotFromConfig(null, BattleSide.Player));
                Assert.Throws<ArgumentOutOfRangeException>(() => registry.CreateRobotFromConfig(config, BattleSide.Player, 0));
                Assert.AreEqual(1, registry.CreateRobotFromConfig(config, BattleSide.Player).InstanceId);
            }
        }

        [Test]
        public void AllEightRobotsHaveCollectedPrefabAndIconAddresses()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                "Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset");
            var assets = settings.BeginCollect("DefaultPackage", true, false).CollectAssets;
            Assert.AreEqual(8, _tables.TbRobot.DataList.Count);
            Assert.AreEqual(28, _tables.TbRobotShape.DataList.Count);
            foreach (var id in new[] { 501, 502, 503, 504, 505, 601, 602, 603 })
            {
                var config = _tables.TbRobot.Get(id);
                Assert.AreEqual(config.RobotPrefab, assets.Single(asset => asset.AssetInfo.AssetPath ==
                    "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab").Address);
                Assert.AreEqual(config.RobotIcon, assets.Single(asset => asset.AssetInfo.AssetPath ==
                    "Assets/AssetRaw/Actor/Robots/Icons/" + config.RobotIcon + ".png").Address);
                var portrait = RobotLevelStyle.PortraitForIcon(config.RobotIcon);
                Assert.AreEqual(portrait, assets.Single(asset => asset.AssetInfo.AssetPath ==
                    "Assets/AssetRaw/Actor/Robots/Portraits/" + portrait + ".png").Address);
            }
        }

        [TestCase(501, "0,0;1,0")]
        [TestCase(502, "0,0;1,0;0,1")]
        [TestCase(503, "0,0;0,1;0,2")]
        [TestCase(504, "0,0;0,1")]
        [TestCase(505, "0,0;1,0;2,0;1,1")]
        [TestCase(601, "0,0;1,0;0,1;1,1")]
        [TestCase(602, "0,0;0,1;1,1")]
        [TestCase(603, "0,0;1,0;1,1;2,1")]
        public void PrefabAndIconMatchRequestedFootprint(int configId, string expectedOffsets)
        {
            var config = _tables.TbRobot.Get(configId);
            var cells = config.ShapeId_Ref.CellOffsets;
            Assert.AreEqual(expectedOffsets, string.Join(";", cells.Select(cell => string.Join(",", cell))));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab");
            Assert.IsNotNull(prefab, "配置中的预制体必须存在");
            var view = prefab.GetComponent("RobotView");
            Assert.IsNotNull(view);
            Assert.AreEqual(configId, new SerializedObject(view).FindProperty("_configId").intValue);
            var renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(r => r.transform.parent == prefab.transform && r.name.StartsWith("Cell_", StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(cells.Length, renderers.Length);
            var unique = new System.Collections.Generic.HashSet<BoardCoordinate>();
            for (var i = 0; i < cells.Length; i++)
            {
                Assert.AreEqual(2, cells[i].Length);
                Assert.IsTrue(unique.Add(new BoardCoordinate(cells[i][0], cells[i][1])));
                var cell = prefab.transform.Find("Cell_" + cells[i][0] + "_" + cells[i][1]);
                Assert.IsNotNull(cell);
                Assert.That(Vector3.Distance(new Vector3(cells[i][0] * 0.35f, cells[i][1] * 0.35f, 0), cell.localPosition), Is.LessThan(0.0001f));
                var sprite = cell.GetComponent<SpriteRenderer>();
                Assert.IsNotNull(sprite.sprite);
                Assert.AreEqual(Color.white, sprite.color);
                Assert.Greater(sprite.sortingOrder, -10);
                Assert.AreEqual(0.329f, sprite.sprite.bounds.size.x * cell.localScale.x, 0.0001f);
                Assert.IsNull(cell.GetComponent<Collider2D>(), "格子只负责显示，不再保留独立碰撞体。");
            }
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/AssetRaw/Actor/Robots/Icons/" + config.RobotIcon + ".png"));
            var body = prefab.GetComponent<PolygonCollider2D>();
            Assert.IsNotNull(body, "机器人需要根据占格生成整体实体碰撞轮廓。");
            Assert.IsTrue(body.enabled);
            Assert.IsFalse(body.isTrigger);
            Assert.GreaterOrEqual(body.pathCount, 1);
            CollectionAssert.AreEqual(new Collider2D[] { body }, prefab.GetComponentsInChildren<Collider2D>(true),
                "机器人只能保留根节点的整体碰撞体。");
            Assert.IsNull(prefab.transform.Find("DragHitArea"), "整体轮廓也用于拖拽命中，不需要重复区域。");
            Assert.IsNotNull(prefab.GetComponent<RobotBoardPointerInput>());
            Assert.IsNull(prefab.transform.Find("LevelLabel"), "机器人等级只使用颜色表示，不再显示等级文字。");
            Assert.IsEmpty(prefab.GetComponentsInChildren<TextMesh>(true));
            Assert.IsEmpty(prefab.GetComponentsInChildren<Rigidbody2D>(true));
            var instance = UnityEngine.Object.Instantiate(prefab, new Vector3(100, 100, 0), Quaternion.identity);
            try
            {
                Physics2D.SyncTransforms();
                var polygon = instance.GetComponent<RobotView>().GetPhysicalCollider();
                Physics2D.SyncTransforms();
                var minimumX = cells.Min(c => c[0]) * 0.35f - 0.175f;
                Assert.Greater(polygon.points.Min(p => p.x), minimumX + 0.001f,
                    "整体轮廓必须内收，为相邻机器人保留碰撞间隙。");
                var originalPoints = polygon.points;
                instance.GetComponent<RobotView>().GetPhysicalCollider();
                CollectionAssert.AreEqual(originalPoints, polygon.points, "重复刷新不得继续缩小碰撞轮廓。");
                var neighbor = UnityEngine.Object.Instantiate(prefab, instance.transform.parent);
                try
                {
                    neighbor.transform.position = instance.transform.position + Vector3.right *
                        ((cells.Max(c => c[0]) - cells.Min(c => c[0]) + 1) * 0.35f);
                    var neighborCollider = neighbor.GetComponent<RobotView>().GetPhysicalCollider();
                    Physics2D.SyncTransforms();
                    var distance = polygon.Distance(neighborCollider);
                    Assert.IsTrue(distance.isValid);
                    Assert.Greater(distance.distance, 0.02f, "相邻机器人整体碰撞轮廓之间应留出缝隙。");
                }
                finally { UnityEngine.Object.DestroyImmediate(neighbor); }
                // 格子中心、相邻格接缝均属于实体；形状内的空白格必须保持畅通。
                for (var x = 0; x <= cells.Max(c => c[0]); x++)
                    for (var y = 0; y <= cells.Max(c => c[1]); y++)
                    {
                        var occupied = cells.Any(c => c[0] == x && c[1] == y);
                        var point = instance.transform.TransformPoint(new Vector3(x * 0.35f, y * 0.35f, 0));
                        Assert.AreEqual(occupied, polygon.OverlapPoint(point), "整体轮廓必须保留配置凹口。");
                        if (!occupied) continue;
                        foreach (var direction in new[] { Vector2Int.right, Vector2Int.up })
                        {
                            if (!cells.Any(c => c[0] == x + direction.x && c[1] == y + direction.y)) continue;
                            var seam = new Vector3((x + direction.x * 0.5f) * 0.35f, (y + direction.y * 0.5f) * 0.35f, 0);
                            Assert.IsTrue(polygon.OverlapPoint(instance.transform.TransformPoint(seam)), "相邻格接缝不能有洞。");
                        }
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
    }
}
