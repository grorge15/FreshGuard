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
        public void BoardRobotRendersConfiguredIconWithLevelAndDragFeedback(int configId)
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var config = tables.TbRobot.Get(configId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab");
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(config, BattleSide.Player);
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(1, 1));
                var instance = Object.Instantiate(prefab);
                try
                {
                    var view = instance.GetComponent<RobotView>();
                    view.Bind(registry, robot.InstanceId);
                    var iconNode = instance.transform.Find("RobotIcon");
                    Assert.IsNotNull(iconNode, "盘面必须实际显示新 Icon，不能只替换商店资源。");
                    var icon = iconNode.GetComponent<SpriteRenderer>();
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(
                        "Assets/AssetRaw/Actor/Robots/Icons/" + config.RobotIcon + ".png"), icon.sprite);
                    var portrait = instance.transform.Find("RobotPortrait").GetComponent<SpriteRenderer>();
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(
                        "Assets/AssetRaw/Actor/Robots/Portraits/" + RobotLevelStyle.PortraitForIcon(config.RobotIcon) + ".png"), portrait.sprite);
                    Assert.AreEqual(icon.sprite.pivot, portrait.sprite.pivot);
                    Assert.AreEqual(icon.sprite.rect, portrait.sprite.rect);
                    Assert.AreEqual(icon.sprite.pixelsPerUnit, portrait.sprite.pixelsPerUnit);
                    Assert.AreEqual(icon.transform.localPosition, portrait.transform.localPosition);
                    Assert.AreEqual(icon.transform.localScale, portrait.transform.localScale);
                    Assert.Greater(portrait.sortingOrder, icon.sortingOrder);
                    CollectionAssert.AreEquivalent(new[] { icon, portrait },
                        instance.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer => renderer.enabled));
                    Assert.AreEqual(RobotLevelStyle.ColorForLevel(1), icon.color);
                    Assert.AreEqual(Color.white, portrait.color);
                    Assert.IsTrue(robot.TryUpgrade());
                    view.Render(robot);
                    Assert.AreEqual(RobotLevelStyle.ColorForLevel(2), icon.color);
                    Assert.AreEqual(Color.white, portrait.color);
                    view.SetMergeCandidate(true);
                    Assert.AreEqual(Color.Lerp(RobotLevelStyle.ColorForLevel(2), Color.cyan, 0.65f), icon.color);
                    Assert.AreEqual(Color.white, portrait.color);
                    view.SetMergeCandidate(false);
                    for (var level = 3; level <= 5; level++)
                    {
                        Assert.IsTrue(robot.TryUpgrade());
                        view.Render(robot);
                        Assert.AreEqual(RobotLevelStyle.ColorForLevel(level), icon.color);
                        Assert.AreEqual(Color.white, portrait.color, "升级只能改变底板，立绘必须保持原色。");
                    }
                    view.PlayMergeFeedback();
                    Assert.AreEqual(Color.white, portrait.color, "合成闪光不能给立绘染色。");
                    robot.TrySetDragging(true);
                    view.Render(robot);
                    Assert.IsFalse(instance.GetComponentsInChildren<SpriteRenderer>(true).Any(renderer => renderer.enabled));
                    Assert.IsFalse(view.GetPhysicalCollider().enabled);
                    robot.TrySetDragging(false);
                    view.Render(robot);
                    Assert.IsTrue(icon.enabled);
                    Assert.IsTrue(portrait.enabled);
                    Assert.IsTrue(view.GetPhysicalCollider().enabled);
                    foreach (var cell in robot.Definition.Cells)
                    {
                        var marker = instance.transform.Find("Cell_" + cell.Column + "_" + cell.Row);
                        BoardCoordinate selected;
                        Assert.IsTrue(view.TryGetNearestOccupiedCell(marker.position, out selected));
                        Assert.AreEqual(cell, selected, "新外观不能改变拖拽选格。");
                    }
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [TestCase("BattleShopUI")]
        [TestCase("ShopRobotItem")]
        [TestCase("StorageRobotSlot")]
        [TestCase("RobotDragGhost")]
        public void ShopStorageAndGhostHaveAlignedOriginalColorPortraits(string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/Shop/" + prefabName + ".prefab");
            var icons = prefab.GetComponentsInChildren<UnityEngine.UI.Image>(true)
                .Where(image => image.name == "m_img_RobotIcon").ToArray();
            Assert.IsNotEmpty(icons);
            foreach (var icon in icons)
            {
                var portrait = icon.transform.Find("m_img_RobotPortrait").GetComponent<UnityEngine.UI.Image>();
                Assert.AreEqual(Color.white, portrait.color);
                Assert.IsFalse(portrait.raycastTarget, "立绘不能阻挡原有商店和拖拽输入。");
                Assert.AreEqual(Vector2.zero, portrait.rectTransform.anchorMin);
                Assert.AreEqual(Vector2.one, portrait.rectTransform.anchorMax);
                Assert.AreEqual(Vector2.zero, portrait.rectTransform.offsetMin);
                Assert.AreEqual(Vector2.zero, portrait.rectTransform.offsetMax);
                Assert.IsTrue(icon.preserveAspect);
                Assert.IsTrue(portrait.preserveAspect);
            }
        }

        [Test]
        public void RobotLevelUsesColorWithoutWorldOrShopLevelText()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            foreach (var configId in new[] { 501, 502, 503, 504, 505, 601, 602, 603 })
            {
                var config = tables.TbRobot.Get(configId);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab");
                Assert.IsNull(prefab.transform.Find("LevelLabel"));
                Assert.IsEmpty(prefab.GetComponentsInChildren<TextMesh>(true));
            }
            var item = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/Shop/ShopRobotItem.prefab");
            var storage = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/Shop/StorageRobotSlot.prefab");
            Assert.IsNotNull(item);
            Assert.IsNotNull(storage);
            Assert.IsNull(item.transform.Find("m_text_Level"));
            Assert.IsNull(storage.transform.Find("m_text_Level"));
        }

        [Test]
        public void BoardViewStopsCollisionDuringDragAndRestoresSameInstance()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(tables.TbRobot.Get(501), BattleSide.Player);
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(4, 3));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/RobotCannon.prefab");
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
                    Assert.IsTrue(robot.TryUpgrade());
                    view.SetBerserkTrigger(true);
                    view.Render(robot);
                    foreach (var collider in instance.GetComponentsInChildren<Collider2D>(true)) Assert.IsTrue(collider.isTrigger);
                    view.SetBerserkTrigger(false);
                    view.Render(robot);
                    Assert.IsFalse(instance.GetComponent<PolygonCollider2D>().isTrigger);
                    CollectionAssert.AreEqual(new Collider2D[] { instance.GetComponent<PolygonCollider2D>() },
                        instance.GetComponentsInChildren<Collider2D>(true));
                    foreach (Transform child in instance.transform)
                        if (child.name.StartsWith("Cell_"))
                            Assert.AreEqual(RobotLevelStyle.ColorForLevel(2), child.GetComponent<SpriteRenderer>().color);
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void PolygonHitSelectsNearestRealCellWithoutChildColliders()
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            using (var registry = new BattleEntityRegistry())
            {
                var robot = registry.CreateRobotFromConfig(tables.TbRobot.Get(502), BattleSide.Player);
                robot.TrySetLocation(RobotLocation.Board, new BoardCoordinate(4, 3));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/RobotScatter.prefab");
                var instance = Object.Instantiate(prefab, new Vector3(12, 7, 0), Quaternion.Euler(0, 0, 30));
                instance.transform.localScale *= 1.5f;
                try
                {
                    var view = instance.GetComponent<RobotView>();
                    var method = typeof(RobotView).GetMethod("TryGetNearestOccupiedCell");
                    Assert.IsNotNull(method, "整体轮廓命中后仍需返回真实占格，不能丢失拖拽选格方法。");
                    object[] arguments = { instance.transform.position, default(BoardCoordinate) };
                    Assert.IsFalse((bool)method.Invoke(view, arguments), "未绑定实体时不能选择占格。");
                    view.Bind(registry, robot.InstanceId);
                    foreach (var offset in robot.Definition.Cells)
                    {
                        var cell = instance.transform.Find("Cell_" + offset.Column + "_" + offset.Row);
                        arguments[0] = cell.GetComponent<SpriteRenderer>().bounds.center;
                        Assert.IsTrue((bool)method.Invoke(view, arguments));
                        Assert.AreEqual(offset, arguments[1]);
                    }
                    arguments[0] = instance.transform.TransformPoint(new Vector3(0.175f, 0, 0));
                    Assert.IsTrue((bool)method.Invoke(view, arguments));
                    Assert.AreEqual(new BoardCoordinate(0, 0), arguments[1], "接缝等距时按坐标稳定选择。");
                    arguments[0] = new Vector3(float.NaN, 0, 0);
                    Assert.IsFalse((bool)method.Invoke(view, arguments));
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
            var baseColor = (Color)method.Invoke(null, new object[] { 1 });
            var gold = (Color)method.Invoke(null, new object[] { 5 });
            Assert.AreEqual((Color)new Color32(238, 243, 247, 255), baseColor);
            Assert.Greater(gold.r, gold.b);
            Assert.Greater(gold.g, gold.b);
            for (var level = 1; level < 5; level++)
                Assert.AreNotEqual(method.Invoke(null, new object[] { level }), method.Invoke(null, new object[] { level + 1 }));
        }
    }
}
