using System.IO;
using System.Reflection;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public class RobotMergePresentationTests
    {
        [TestCase(501)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        [TestCase(505)]
        [TestCase(601)]
        [TestCase(602)]
        [TestCase(603)]
        public void BoardViewStopsCollisionDuringDragAndRestoresSameInstance(int configId)
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            using (var registry = new BattleEntityRegistry())
            {
                var config = tables.TbRobot.Get(configId);
                var robot = registry.CreateRobotFromConfig(config, BattleSide.Player);
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(4, 3));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab");
                var instance = Object.Instantiate(prefab);
                try
                {
                    var view = instance.GetComponent<RobotView>();
                    view.Bind(registry, robot.InstanceId);
                    var render = typeof(RobotView).GetMethod("Render", new[] { typeof(RobotEntity) });
                    Assert.IsNotNull(render, "盘面视图需要按机器人当前拖拽状态同步碰撞与显示。");
                    var outline = instance.transform.Find("ShapeOutline");
                    Assert.IsNotNull(outline);
                    var edges = outline.GetComponentsInChildren<SpriteRenderer>(true);
                    Assert.IsNotEmpty(edges);
                    Assert.IsTrue(robot.TryUpgrade());
                    view.SetMergeCandidate(true);
                    view.PlayMergeFeedback();
                    foreach (var edge in edges) { Assert.IsTrue(edge.enabled); Assert.AreEqual(Color.white, edge.color); }
                    robot.TrySetDragging(true);
                    render.Invoke(view, new object[] { robot });
                    foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true)) Assert.IsFalse(collider.enabled);
                    foreach (var edge in edges) Assert.IsFalse(edge.enabled, "拖拽时不能遗留黑边。");
                    robot.TrySetDragging(false);
                    render.Invoke(view, new object[] { robot });
                    foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true)) Assert.IsTrue(collider.enabled);
                    foreach (var edge in edges) Assert.IsTrue(edge.enabled);
                    Assert.AreEqual(robot.InstanceId, view.InstanceId);
                    Assert.AreEqual(new BoardCoordinate(4, 3), robot.Anchor);
                    Assert.IsTrue(robot.TrySetLocation(RobotLocation.Storage, null));
                    view.Render(robot);
                    foreach (var edge in edges) Assert.IsFalse(edge.enabled, "进入暂存时隐藏盘面黑边。");
                    Assert.IsTrue(robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(4, 3)));
                    view.Render(robot);
                    foreach (var edge in edges) Assert.IsTrue(edge.enabled);
                    EntityRemovalSnapshot removal;
                    Assert.IsTrue(registry.Remove(robot.InstanceId, EntityRemovalReason.Manual, out removal));
                    view.Render(robot);
                    foreach (var edge in edges) Assert.IsFalse(edge.enabled, "移除时不能遗留黑边。");
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void LevelPaletteUsesRunLevelInsteadOfFixedQuality()
        {
            var style = typeof(RobotView).Assembly.GetType("GameLogic.RobotLevelStyle");
            Assert.IsNotNull(style, "等级背景必须独立于固定品质。");
            var method = style.GetMethod("ColorForLevel", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            var white = (Color)method.Invoke(null, new object[] { 1 });
            var gold = (Color)method.Invoke(null, new object[] { 5 });
            Assert.AreEqual(Color.white, white);
            Assert.Greater(gold.r, gold.b);
            Assert.Greater(gold.g, gold.b);
            for (var level = 1; level < 5; level++)
                Assert.AreNotEqual(method.Invoke(null, new object[] { level }), method.Invoke(null, new object[] { level + 1 }));
        }
    }
}
