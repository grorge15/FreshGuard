using System.IO;
using GameConfig;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class GlassLayoutAndViewTests
    {
        private static Tables ReadTables() => new Tables(name =>
            new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes/" + name + ".bytes"))));

        [Test]
        public void ProductionLayoutContains84GlassCellsAndOneColoredCellPerSide()
        {
            var tables = ReadTables();
            var layout = new BoardLayoutRules(tables.TbBoardLayout.Get(1));
            var glass = 0;
            var border = 0;
            for (var column = 0; column < layout.Columns; column++)
                for (var row = 0; row < layout.Rows; row++)
                {
                    var cell = new BoardCoordinate(column, row);
                    if (!layout.HasInitialGlass(cell)) continue;
                    glass++;
                    if (!layout.IsInterior(cell)) border++;
                }
            Assert.AreEqual(84, glass);
            Assert.AreEqual(36, border);
            Assert.AreEqual(15, layout.OpenCells.Count);
            Assert.AreEqual(new BoardCoordinate(5, 6), layout.ColoredGlassCell);
            Assert.IsTrue(layout.HasInitialGlass(layout.ColoredGlassCell));
            Assert.AreEqual(0, tables.TbGlass.Get(layout.NormalGlassId).Kind);
            Assert.AreEqual(1, tables.TbGlass.Get(layout.ColoredGlassId).Kind);
            Assert.AreEqual(0.25f, tables.TbGlobal.Get(9).Value);
            Assert.AreEqual(10, tables.TbGlobal.Get(27).Value);
            Assert.AreEqual(30, tables.TbGlobal.Get(28).Value);
        }

        [Test]
        public void InteriorBreakOpensPlacementButBorderNeverOpens()
        {
            var layout = new BoardLayoutRules(ReadTables().TbBoardLayout.Get(1));
            var board = new BoardModel(layout.Columns, layout.Rows, layout.OpenCells, layout.ReservedBorderWidth);
            var unit = new PlaceableDefinition("robot", 1, 1);
            Assert.IsTrue(board.Evaluate(new BoardCoordinate(3, 3), unit).IsValid);
            var interior = new BoardCoordinate(2, 3);
            Assert.IsFalse(board.Evaluate(interior, unit).IsValid);
            Assert.IsTrue(board.SetOpen(interior, true));
            Assert.IsTrue(board.Evaluate(interior, unit).IsValid);
            Assert.IsFalse(board.SetOpen(new BoardCoordinate(0, 3), true));
            Assert.IsFalse(board.Evaluate(new BoardCoordinate(0, 3), unit).IsValid);
            // 完整形状中有一格玻璃，即使锚点开放仍然非法。
            Assert.IsFalse(board.Evaluate(new BoardCoordinate(7, 3), new PlaceableDefinition("wide", 2, 1)).IsValid);
        }

        [TestCase(1, 3, 2, 2, 1)]
        [TestCase(2, 5, 4, 3, 1)]
        public void PrefabStagesMatchTableAndBreakingDisablesColliderImmediately(int configId,
            int full, int second, int light, int heavy)
        {
            var tables = ReadTables();
            var config = tables.TbGlass.Get(configId);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Glass/Prefabs/" + config.PrefabLocation + ".prefab");
            Assert.IsNotNull(prefab, "先通过Unity Editor装配玻璃Prefab。");
            var root = Object.Instantiate(prefab);
            var ownerRoot = new GameObject("GlassOwnerTest");
            try
            {
                using (var registry = new BattleEntityRegistry())
                {
                    var owner = ownerRoot.AddComponent<GlassBoardController>();
                    var entity = registry.CreateGlassFromConfig(config, BattleSide.Player, new BoardCoordinate(2, 2), 0.25f);
                    Assert.AreEqual(full, entity.MaxDurability);
                    var view = root.GetComponent<GlassView>();
                    view.Bind(owner, entity, config, 0.35f);
                    Assert.AreEqual(2, view.StageIndex);
                    Assert.AreEqual(config.StageSprites[2], view.CurrentSprite.name);
                    view.ShowDurability(second);
                    Assert.AreEqual(configId == 1 ? 1 : 2, view.StageIndex);
                    view.ShowDurability(light);
                    Assert.AreEqual(1, view.StageIndex);
                    view.ShowDurability(heavy);
                    Assert.AreEqual(0, view.StageIndex);
                    Assert.IsNull(root.GetComponent<Rigidbody2D>());
                    Assert.AreEqual(Vector2.one * 0.35f, root.GetComponent<BoxCollider2D>().size);
                    view.BeginBreak();
                    Assert.IsFalse(root.GetComponent<BoxCollider2D>().enabled);
                    Assert.IsFalse(view.Advance(0f), "冻结时不推进淡出。");
                    Assert.IsTrue(view.Advance(0.15f));
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(ownerRoot); }
        }
    }
}
