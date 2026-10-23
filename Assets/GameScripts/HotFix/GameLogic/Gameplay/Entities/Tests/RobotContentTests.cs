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
                .Where(renderer => renderer.transform.parent == prefab.transform && renderer.name.StartsWith("Cell_", StringComparison.Ordinal)).ToArray();
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
                var collider = cell.GetComponent<BoxCollider2D>();
                Assert.IsNotNull(collider, "每个机器人实体格都需要静态碰撞体。");
                Assert.IsTrue(collider.enabled);
                Assert.IsFalse(collider.isTrigger);
                Assert.That(Vector2.Distance(collider.size, sprite.sprite.bounds.size), Is.LessThan(0.0001f));
                Assert.That(Vector2.Distance(collider.offset, sprite.sprite.bounds.center), Is.LessThan(0.0001f));
            }
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/AssetRaw/Actor/Robots/Icons/" + config.RobotIcon + ".png"));
            var outline = prefab.transform.Find("ShapeOutline");
            Assert.IsNotNull(outline, "机器人需要独立的图片描边节点。");
            Assert.AreEqual(Vector3.zero, outline.localPosition);
            Assert.AreEqual(Vector3.one, outline.localScale);
            var blackStrip = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Robots/Icons/heibian.png");
            var edges = outline.GetComponentsInChildren<SpriteRenderer>(true);
            var directions = new[] { new BoardCoordinate(1, 0), new BoardCoordinate(0, 1), new BoardCoordinate(-1, 0), new BoardCoordinate(0, -1) };
            var exposedEdges = 0;
            foreach (var cell in unique)
                foreach (var direction in directions)
                    if (!unique.Contains(new BoardCoordinate(cell.Column + direction.Column, cell.Row + direction.Row))) exposedEdges++;
            Assert.AreEqual(exposedEdges, edges.Count(edge => edge.name.StartsWith("Edge_", StringComparison.Ordinal)),
                "只给实际外边描边，不能沿包围盒或内部相邻边描边。");
            foreach (var edge in edges)
            {
                Assert.AreSame(blackStrip, edge.sprite);
                Assert.AreEqual(Color.white, edge.color, "使用原黑色图片，不叠加等级色。");
                Assert.AreEqual(renderers[0].sortingLayerID, edge.sortingLayerID);
                Assert.Greater(edge.sortingOrder, renderers.Max(renderer => renderer.sortingOrder));
                Assert.Less(edge.sortingOrder, prefab.transform.Find("LevelLabel").GetComponent<Renderer>().sortingOrder);
                // 每段中心及四角向内采样，都必须落在真实占格内，不能侵入凹陷。
                foreach (var point in new[] { Vector2.zero, new Vector2(-0.499f, -0.499f), new Vector2(-0.499f, 0.499f), new Vector2(0.499f, -0.499f), new Vector2(0.499f, 0.499f) })
                {
                    var local = prefab.transform.InverseTransformPoint(edge.transform.TransformPoint(
                        Vector3.Scale(point, edge.sprite.bounds.size)));
                    var covered = new BoardCoordinate(Mathf.FloorToInt(local.x / 0.35f + 0.5f), Mathf.FloorToInt(local.y / 0.35f + 0.5f));
                    Assert.IsTrue(unique.Contains(covered), "描边不得填入未占格区域：" + edge.name);
                }
            }
            Assert.IsEmpty(outline.GetComponentsInChildren<Collider2D>(true));
            Assert.AreEqual(cells.Length, prefab.GetComponentsInChildren<Collider2D>(true).Length,
                "仅实际占格参与碰撞，不能用根节点包围盒填满L/T空白区。");
            Assert.IsNotNull(prefab.GetComponent<RobotBoardPointerInput>());
            Assert.IsNotNull(prefab.transform.Find("LevelLabel"));
            Assert.IsEmpty(prefab.GetComponentsInChildren<Rigidbody2D>(true));
        }
    }
}
