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
    public sealed class RobotCollisionFeedbackTests
    {
        [TestCase(501)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        [TestCase(505)]
        [TestCase(601)]
        [TestCase(602)]
        [TestCase(603)]
        public void HitPulseKeepsPhysicsAlignedAndRestoresAfterRepeatedHitsAndDrag(int configId)
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,
                "AssetRaw/Configs/bytes", name + ".bytes"))));
            var config = tables.TbRobot.Get(configId);
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(config, BattleSide.Player);
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(1, 1));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab");
                var instance = Object.Instantiate(prefab, new Vector3(3, 4, 0), Quaternion.Euler(0, 0, 20));
                instance.transform.localScale = new Vector3(1.4f, 1.4f, 1f);
                try
                {
                    var view = instance.GetComponent<RobotView>();
                    view.Bind(registry, robot.InstanceId);
                    var icon = instance.transform.Find("RobotIcon");
                    var portrait = instance.transform.Find("RobotPortrait");
                    var frame = instance.transform.Find("RobotLevelFrame");
                    var stars = instance.transform.Find("LevelStars");
                    var initialStarPosition = stars.localPosition;
                    var initialStarScale = stars.localScale;
                    var initialPosition = icon.localPosition;
                    var initialScale = icon.localScale;
                    var initialRootScale = instance.transform.localScale;
                    var attackOrigin = view.GetAttackOrigin();
                    var center = icon.GetComponent<SpriteRenderer>().bounds.center;
                    var collider = view.GetPhysicalCollider();
                    Physics2D.SyncTransforms();
                    var bounds = collider.bounds;
                    var cells = robot.Definition.Cells.Select(cell => instance.transform.Find(
                        "Cell_" + cell.Column + "_" + cell.Row)).ToArray();
                    var cellPositions = cells.Select(cell => cell.position).ToArray();

                    view.PlayCollisionFeedback();
                    Advance(view, 0.055f);
                    Assert.Greater(icon.localScale.x, initialScale.x * 1.05f);
                    Assert.Less(Vector3.Distance(center, icon.GetComponent<SpriteRenderer>().bounds.center), 0.0001f,
                        "非居中形状也应以整张外观中心放大。");
                    Assert.AreEqual(icon.localScale, portrait.localScale);
                    Assert.AreEqual(icon.localScale, frame.localScale, "等级边框应随底板同步回弹。");
                    Assert.Greater(stars.localScale.x, initialStarScale.x * 1.05f, "星星应随外观同步回弹。");
                    Assert.AreEqual(icon.localPosition, portrait.localPosition);
                    Assert.AreEqual(icon.localPosition, frame.localPosition);
                    Assert.AreEqual(initialRootScale, instance.transform.localScale);
                    Assert.AreEqual(attackOrigin, view.GetAttackOrigin());
                    CollectionAssert.AreEqual(cellPositions, cells.Select(cell => cell.position));
                    Physics2D.SyncTransforms();
                    Assert.AreEqual(bounds, collider.bounds, "视觉放大不能改变碰撞尺寸。");
                    for (var i = 0; i < 20; i++)
                    {
                        var beforeHit = icon.localScale;
                        view.PlayCollisionFeedback();
                        Assert.AreEqual(beforeHit, icon.localScale, "重新触发不能先跳回原始大小。");
                        Advance(view, 0.025f);
                        Assert.LessOrEqual(icon.localScale.x, initialScale.x * 1.09f,
                            "连续碰撞不能累乘缩放倍率。");
                    }
                    Advance(view, 0.3f);
                    Assert.That(Vector3.Distance(initialPosition, icon.localPosition), Is.LessThan(0.0001f));
                    Assert.AreEqual(initialScale, icon.localScale);
                    Assert.AreEqual(initialScale, frame.localScale);
                    Assert.AreEqual(initialStarScale, stars.localScale);
                    Assert.That(Vector3.Distance(initialStarPosition, stars.localPosition), Is.LessThan(0.0001f));
                    view.PlayCollisionFeedback();
                    Advance(view, 0.055f);
                    robot.TrySetDragging(true);
                    view.Render(robot);
                    Assert.AreEqual(initialScale, icon.localScale, "拖拽隐藏前应恢复大小。");
                    robot.TrySetDragging(false);
                    view.Render(robot);
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        private static void Advance(RobotView view, float seconds) => typeof(RobotView)
            .GetMethod("AdvanceCollisionFeedback", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, new object[] { seconds });
    }
}
