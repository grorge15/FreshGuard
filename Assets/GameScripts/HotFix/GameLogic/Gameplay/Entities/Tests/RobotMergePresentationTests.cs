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
                    var frame = instance.transform.Find("RobotLevelFrame").GetComponent<SpriteRenderer>();
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(
                        "Assets/AssetRaw/Actor/Robots/LevelFrames/" + RobotLevelStyle.FrameForIcon(config.RobotIcon) + ".png"), frame.sprite);
                    Assert.AreEqual(icon.sprite.pivot, frame.sprite.pivot);
                    Assert.AreEqual(icon.sprite.rect, frame.sprite.rect);
                    Assert.AreEqual(icon.sprite.pixelsPerUnit, frame.sprite.pixelsPerUnit);
                    Assert.Greater(frame.sortingOrder, icon.sortingOrder);
                    Assert.Less(frame.sortingOrder, portrait.sortingOrder);
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(
                        "Assets/AssetRaw/Actor/Robots/Portraits/" + RobotLevelStyle.PortraitForIcon(config.RobotIcon) + ".png"), portrait.sprite);
                    Assert.AreEqual(icon.sprite.pivot, portrait.sprite.pivot);
                    Assert.AreEqual(icon.sprite.rect, portrait.sprite.rect);
                    Assert.AreEqual(icon.sprite.pixelsPerUnit, portrait.sprite.pixelsPerUnit);
                    Assert.AreEqual(icon.transform.localPosition, portrait.transform.localPosition);
                    Assert.AreEqual(icon.transform.localScale, portrait.transform.localScale);
                    Assert.Greater(portrait.sortingOrder, icon.sortingOrder);
                    var stars = instance.transform.Find("LevelStars").GetComponentsInChildren<SpriteRenderer>(true);
                    Assert.AreEqual(5, stars.Length);
                    Assert.AreEqual(1, stars.Count(star => star.enabled));
                    CollectionAssert.AreEquivalent(new[] { icon, frame, portrait, stars[0] },
                        instance.GetComponentsInChildren<SpriteRenderer>(true).Where(renderer => renderer.enabled));
                    Assert.AreEqual(RobotLevelStyle.ColorForLevel(1), icon.color);
                    Assert.AreEqual(Color.white, portrait.color);
                    Assert.IsTrue(robot.TryUpgrade());
                    view.Render(robot);
                    Assert.AreEqual(2, stars.Count(star => star.enabled));
                    Assert.AreEqual(RobotLevelStyle.ColorForLevel(2), icon.color);
                    Assert.AreEqual(Color.white, portrait.color);
                    view.SetMergeCandidate(true);
                    Assert.AreEqual(Color.Lerp(RobotLevelStyle.ColorForLevel(2), Color.cyan, 0.65f), icon.color);
                    Assert.AreEqual(Color.white, portrait.color);
                    view.SetMergeCandidate(false);
                    var originalStarSize = stars[0].transform.localScale;
                    for (var level = 3; level <= 5; level++)
                    {
                        Assert.IsTrue(robot.TryUpgrade());
                        view.Render(robot);
                        Assert.AreEqual(RobotLevelStyle.ColorForLevel(level), icon.color);
                        Assert.AreEqual(Color.white, portrait.color, "星级升级时立绘必须保持原色。");
                        Assert.IsTrue(frame.enabled);
                        Assert.AreEqual(RobotLevelStyle.FrameColorForLevel(level), frame.color);
                        Assert.AreEqual(level, stars.Count(star => star.enabled));
                        var expected = RobotLevelStyle.StarAnchor(config.ShapeId_Ref.CellOffsets) * MapGridMath.REFERENCE_CELL_SIZE;
                        var center = (stars[0].transform.localPosition + stars[level - 1].transform.localPosition) * 0.5f;
                        Assert.That(Vector2.Distance(expected, center), Is.LessThan(0.00001f));
                        for (var i = 1; i < level; i++)
                            Assert.That(stars[i].transform.localPosition.x - stars[i - 1].transform.localPosition.x,
                                Is.EqualTo(MapGridMath.REFERENCE_CELL_SIZE * RobotLevelStyle.StarSpacingForCells(config.ShapeId_Ref.CellOffsets)).Within(0.00001f));
                        foreach (var star in stars)
                        {
                            Assert.AreEqual(Color.white, star.color);
                            Assert.Greater(star.sortingOrder, portrait.sortingOrder);
                            Assert.AreEqual(originalStarSize, star.transform.localScale, "同一机器人升星不能改变星星尺寸。");
                            if (!star.enabled) continue;
                            var bottom = config.ShapeId_Ref.CellOffsets.Min(cell => cell[1]);
                            var bottomCells = config.ShapeId_Ref.CellOffsets.Where(cell => cell[1] == bottom).ToArray();
                            var left = (bottomCells.Min(cell => cell[0]) - 0.4f) * MapGridMath.REFERENCE_CELL_SIZE;
                            var right = (bottomCells.Max(cell => cell[0]) + 0.4f) * MapGridMath.REFERENCE_CELL_SIZE;
                            var halfSize = star.sprite.bounds.size.x * star.transform.localScale.x * 0.5f;
                            Assert.That(star.transform.localPosition.x - halfSize, Is.GreaterThanOrEqualTo(left - 0.00001f));
                            Assert.That(star.transform.localPosition.x + halfSize, Is.LessThanOrEqualTo(right + 0.00001f));
                            Assert.That(star.transform.localPosition.y - halfSize,
                                Is.GreaterThanOrEqualTo((bottom - 0.4f) * MapGridMath.REFERENCE_CELL_SIZE - 0.00001f), "星星底边必须在底板内。");
                        }
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
                    Assert.AreEqual(5, stars.Count(star => star.enabled));
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
                var frame = icon.transform.Find("m_img_LevelFrame").GetComponent<UnityEngine.UI.Image>();
                Assert.IsFalse(frame.raycastTarget);
                Assert.IsTrue(frame.preserveAspect);
                Assert.Less(frame.transform.GetSiblingIndex(), portrait.transform.GetSiblingIndex());
                Assert.AreEqual(Vector2.zero, frame.rectTransform.offsetMin);
                Assert.AreEqual(Vector2.zero, frame.rectTransform.offsetMax);
                Assert.AreEqual(Color.white, portrait.color);
                Assert.IsFalse(portrait.raycastTarget, "立绘不能阻挡原有商店和拖拽输入。");
                Assert.AreEqual(Vector2.zero, portrait.rectTransform.anchorMin);
                Assert.AreEqual(Vector2.one, portrait.rectTransform.anchorMax);
                Assert.AreEqual(Vector2.zero, portrait.rectTransform.offsetMin);
                Assert.AreEqual(Vector2.zero, portrait.rectTransform.offsetMax);
                Assert.IsTrue(icon.preserveAspect);
                Assert.IsTrue(portrait.preserveAspect);
                var stars = icon.transform.Find("m_rect_LevelStars").GetComponentsInChildren<UnityEngine.UI.Image>(true);
                Assert.AreEqual(5, stars.Length);
                foreach (var star in stars)
                {
                    Assert.IsNotNull(star.sprite);
                    Assert.IsFalse(star.raycastTarget, "星星不能阻挡商店和拖拽输入。");
                }
            }
        }

        [TestCase("BattleShopUI")]
        [TestCase("ShopRobotItem")]
        [TestCase("StorageRobotSlot")]
        [TestCase("RobotDragGhost")]
        public void CachedShopIconsCenterTheSameStarSpacingInsideTheDrawnSprite(string prefabName)
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/UI/Shop/" + prefabName + ".prefab"));
            try
            {
                foreach (var icon in instance.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(image => image.name == "m_img_RobotIcon"))
                {
                    var portrait = icon.transform.Find("m_img_RobotPortrait").GetComponent<UnityEngine.UI.Image>();
                    var frame = icon.transform.Find("m_img_LevelFrame").GetComponent<UnityEngine.UI.Image>();
                    var stars = icon.transform.Find("m_rect_LevelStars").GetComponentsInChildren<UnityEngine.UI.Image>(true);
                    using (var visual = new RobotIconPresentation(icon))
                    {
                        foreach (var config in tables.TbRobot.DataList)
                        {
                            // 验证已完成加载后的真实刷新路径；本测试不启动游戏资源下载器。
                            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Robots/Icons/" + config.RobotIcon + ".png");
                            portrait.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Robots/Portraits/" + RobotLevelStyle.PortraitForIcon(config.RobotIcon) + ".png");
                            frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/AssetRaw/Actor/Robots/LevelFrames/" + RobotLevelStyle.FrameForIcon(config.RobotIcon) + ".png");
                            typeof(RobotIconPresentation).GetField("_location", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(visual, config.RobotIcon);
                            typeof(RobotIconPresentation).GetField("_cells", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(visual, config.ShapeId_Ref.CellOffsets);
                            for (var level = 1; level <= 5; level++)
                            {
                                visual.Render(config.RobotIcon, level, true);
                                Assert.AreEqual(level, stars.Count(star => star.enabled));
                                Assert.AreEqual(Color.white, portrait.color);
                                Assert.AreEqual(RobotLevelStyle.ColorForLevel(level), icon.color);
                                Assert.IsTrue(frame.enabled);
                                Assert.AreEqual(RobotLevelStyle.FrameColorForLevel(level), frame.color);
                                var cellPixels = icon.sprite.pixelsPerUnit * MapGridMath.REFERENCE_CELL_SIZE;
                                var pixelScale = Mathf.Min(icon.rectTransform.rect.width / icon.sprite.rect.width,
                                    icon.rectTransform.rect.height / icon.sprite.rect.height);
                                var drawnSize = icon.sprite.rect.size * pixelScale;
                                var drawnOrigin = icon.rectTransform.rect.center - drawnSize * 0.5f;
                                var anchor = RobotLevelStyle.StarAnchor(config.ShapeId_Ref.CellOffsets);
                                var expected = drawnOrigin + (icon.sprite.pivot + anchor * cellPixels) * pixelScale;
                                var bottom = config.ShapeId_Ref.CellOffsets.Min(cell => cell[1]);
                                var bottomCells = config.ShapeId_Ref.CellOffsets.Where(cell => cell[1] == bottom).ToArray();
                                var leftColumn = bottomCells.Min(cell => cell[0]);
                                var rightColumn = bottomCells.Max(cell => cell[0]);
                                var shapeWidth = rightColumn - leftColumn + 1;
                                var normalizedSize = 0.46f * (shapeWidth == 1 ? 0.8f : 1f);
                                var displayedSize = normalizedSize * cellPixels * pixelScale;
                                var expectedSpacing = Mathf.Min(0.16f, (shapeWidth - 0.2f - normalizedSize) / 4f) * cellPixels * pixelScale;
                                var center = (stars[0].rectTransform.anchoredPosition + stars[level - 1].rectTransform.anchoredPosition) * 0.5f;
                                Assert.That(Vector2.Distance(expected, center), Is.LessThan(0.01f), config.RobotIcon);
                                for (var i = 1; i < level; i++)
                                    Assert.That(stars[i].rectTransform.anchoredPosition.x - stars[i - 1].rectTransform.anchoredPosition.x,
                                        Is.EqualTo(expectedSpacing).Within(0.001f));
                                foreach (var star in stars)
                                {
                                    Assert.That(star.rectTransform.sizeDelta.x, Is.EqualTo(displayedSize).Within(0.001f));
                                    Assert.That(star.rectTransform.sizeDelta.y, Is.EqualTo(star.rectTransform.sizeDelta.x));
                                    Assert.IsFalse(star.raycastTarget);
                                    if (!star.enabled) continue;
                                    var start = drawnOrigin + (icon.sprite.pivot + new Vector2(leftColumn - 0.4f, bottom - 0.4f) * cellPixels) * pixelScale;
                                    var end = drawnOrigin + (icon.sprite.pivot + new Vector2(rightColumn + 0.4f, bottom + 0.4f) * cellPixels) * pixelScale;
                                    var halfSize = star.rectTransform.sizeDelta.x * 0.5f;
                                    var position = star.rectTransform.anchoredPosition;
                                    Assert.That(position.x - halfSize, Is.GreaterThanOrEqualTo(start.x - 0.01f));
                                    Assert.That(position.x + halfSize, Is.LessThanOrEqualTo(end.x + 0.01f), "窄底板的五星不能覆盖相邻机器人。");
                                    Assert.That(position.y - halfSize, Is.GreaterThanOrEqualTo(start.y - 0.01f));
                                    Assert.That(position.y + halfSize, Is.LessThanOrEqualTo(end.y + 0.01f));
                                }
                            }
                            visual.Render(config.RobotIcon, 5, false);
                            Assert.IsFalse(stars.Any(star => star.enabled), "拿起或隐藏时不能留下星星。");
                            Assert.IsFalse(frame.enabled, "隐藏时不能留下边框。");
                        }
                    }
                    Assert.IsFalse(stars.Any(star => star.enabled), "图标释放后必须隐藏星星。");
                    Assert.IsFalse(frame.enabled, "图标释放后必须隐藏边框。");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void RobotLevelUsesStarsWithoutWorldOrShopLevelText()
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
        public void LevelsUseApprovedBaseAndFrameColors()
        {
            var style = typeof(RobotView).Assembly.GetType("GameLogic.RobotLevelStyle");
            Assert.IsNotNull(style);
            var method = style.GetMethod("ColorForLevel", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            var bases = new[] { new Color32(238,243,247,255), new Color32(173,221,186,255),
                new Color32(176,213,247,255), new Color32(212,185,239,255), new Color32(244,211,132,255) };
            var frames = new[] { new Color32(167,181,195,255), new Color32(52,134,81,255),
                new Color32(40,108,176,255), new Color32(128,80,180,255), new Color32(181,125,25,255) };
            for (var level = 1; level <= 5; level++)
            {
                Assert.AreEqual((Color)bases[level - 1], method.Invoke(null, new object[] { level }));
                Assert.AreEqual((Color)frames[level - 1], RobotLevelStyle.FrameColorForLevel(level));
            }
            Assert.AreEqual((Color)bases[0], RobotLevelStyle.ColorForLevel(0));
            Assert.AreEqual((Color)bases[4], RobotLevelStyle.ColorForLevel(6));
        }
    }
}
