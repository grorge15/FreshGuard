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
        [Test]
        public void BoardViewStopsCollisionDuringDragAndRestoresSameInstance()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(tables.TbRobot.Get(1001), BattleSide.Player);
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(4, 3));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/RobotI.prefab");
                var instance = Object.Instantiate(prefab);
                try
                {
                    var view = instance.GetComponent<RobotView>();
                    view.Bind(registry, robot.InstanceId);
                    var render = typeof(RobotView).GetMethod("Render", new[] { typeof(RobotEntity) });
                    Assert.IsNotNull(render, "盘面视图需要按机器人当前拖拽状态同步碰撞与显示。");
                    robot.TrySetDragging(true);
                    render.Invoke(view, new object[] { robot });
                    foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true)) Assert.IsFalse(collider.enabled);
                    robot.TrySetDragging(false);
                    render.Invoke(view, new object[] { robot });
                    foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true)) Assert.IsTrue(collider.enabled);
                    Assert.AreEqual(robot.InstanceId, view.InstanceId);
                    Assert.AreEqual(new BoardCoordinate(4, 3), robot.Anchor);
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
