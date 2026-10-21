using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class MapDataTests
    {
        [Test]
        public void RobotAuthoredCellSpacingAndColliderMatchChangedMapCellSizeWithoutAccumulation()
        {
            var boardRoot = new GameObject("scaled-board");
            var robotRoot = new GameObject("robot");
            try
            {
                var board = boardRoot.AddComponent<PlacementBoardView>();
                boardRoot.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
                typeof(PlacementBoardView).GetField("_cellSize", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(board, 0.7f);
                robotRoot.transform.SetParent(boardRoot.transform, false);
                var view = robotRoot.AddComponent<RobotView>();
                var cell = new GameObject("cell");
                cell.transform.SetParent(robotRoot.transform, false);
                cell.transform.localPosition = Vector3.right * 0.35f;
                var collider = cell.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one * 0.35f;
                view.ApplyBoardScale(board);
                view.ApplyBoardScale(board);
                Physics2D.SyncTransforms();
                Assert.AreEqual(0.385f, Vector3.Distance(cell.transform.position, robotRoot.transform.position), 0.0001f);
                Assert.AreEqual(0.385f, collider.bounds.size.x, 0.0001f);
                typeof(PlacementBoardView).GetField("_cellSize", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(board, 0.35f);
                view.ApplyBoardScale(board);
                Assert.AreEqual(Vector3.one, robotRoot.transform.localScale);
            }
            finally { UnityEngine.Object.DestroyImmediate(robotRoot); UnityEngine.Object.DestroyImmediate(boardRoot); }
        }

        [Test]
        public void BinaryV2RoundTripPreservesMetadataAndAllSixRowMajorCombinations()
        {
            var map = CreateSixCombinationMap(7, 5, 0.6f);
            map.NormalGlassId = 11; map.ColoredGlassId = 12;
            var bytes = MapBinarySerializer.Serialize(map);
            Assert.AreEqual(2, MapData.CURRENT_VERSION);
            Assert.AreEqual(30 + 2 * 35, bytes.Length);
            CollectionAssert.AreEqual(new byte[] { 70, 71, 77, 80, 2, 0, 7, 0, 0, 0 }, bytes.Take(10));
            CollectionAssert.AreEqual(new byte[] { 1, 0, 2, 0, 1, 1, 2, 1, 1, 2, 2, 2 }, bytes.Skip(30).Take(12),
                "V2每格依次写基础地形和障碍，不序列化结构体内存。");
            ushort sourceVersion;
            var restored = MapBinarySerializer.Deserialize(bytes, out sourceVersion);
            Assert.AreEqual(2, sourceVersion);
            Assert.AreEqual(2, restored.Version);
            Assert.AreEqual(map.Rows, restored.Rows); Assert.AreEqual(map.Columns, restored.Columns);
            Assert.AreEqual(map.CellSize, restored.CellSize);
            Assert.AreEqual(11, restored.NormalGlassId); Assert.AreEqual(12, restored.ColoredGlassId);
            CollectionAssert.AreEqual(map.Cells, restored.Cells);
            Assert.AreEqual(map.Cells[9].BaseTerrain, restored.GetBaseTerrain(new BoardCoordinate(2, 1)));
            Assert.AreEqual(map.Cells[9].Obstacle, restored.GetObstacle(new BoardCoordinate(2, 1)));
            CollectionAssert.AreEqual(map.Cells, MapBinarySerializer.Deserialize(bytes).Cells,
                "保留不带out参数的入口。");
            CollectionAssert.AreEqual(bytes, MapBinarySerializer.Serialize(restored));
        }

        [TestCase(BaseTerrainType.Track, ObstacleType.None, false, true)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.None, true, true)]
        [TestCase(BaseTerrainType.Track, ObstacleType.GlassNormal, false, false)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassNormal, false, false)]
        [TestCase(BaseTerrainType.Track, ObstacleType.GlassColored, false, false)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassColored, false, false)]
        public void LayerCombinationsDeterminePlacementAndBallPermissions(BaseTerrainType terrain, ObstacleType obstacle,
            bool canPlace, bool canPass)
        {
            var map = MapData.Create(1, 1);
            var cell = new BoardCoordinate(0, 0);
            map.SetBaseTerrain(cell, terrain);
            map.SetObstacle(cell, obstacle);
            Assert.AreEqual(canPlace, map.CanPlaceUnit(cell));
            Assert.AreEqual(canPass, map.CanEnergyBallPass(cell));
            var board = new BoardModel(map);
            Assert.AreEqual(canPlace, board.CanPlaceUnit(cell));
            Assert.AreEqual(canPlace, board.IsOpen(cell));
            Assert.AreEqual(canPass, board.CanEnergyBallPass(cell));
        }

        [TestCase(0, 0)] [TestCase(4, 0)] [TestCase(4, 3)] [TestCase(5, 1)]
        [TestCase(6, 0)] [TestCase(6, 65)] [TestCase(10, 0)] [TestCase(10, 65)]
        [TestCase(18, 0)] [TestCase(22, 0)] [TestCase(22, 1)] [TestCase(26, 1)]
        [TestCase(30, 0)] [TestCase(30, 3)] [TestCase(30, 255)]
        [TestCase(31, 3)] [TestCase(31, 255)]
        public void CorruptedBinaryIsRejected(int offset, byte value)
        {
            var bytes = MapBinarySerializer.Serialize(MapData.Create());
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes));
        }

        [Test]
        public void TruncatedAndTrailingPayloadsAreRejected()
        {
            var bytes = MapBinarySerializer.Serialize(MapData.Create());
            foreach (var length in new[] { 0, 5, 29, bytes.Length - 1 })
                Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes.Take(length).ToArray()));
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes.Concat(new byte[] { 0 }).ToArray()));
        }

        [Test]
        public void V1FourTerrainCodesMigrateInMemoryAndOnlyExplicitSerializationProducesV2()
        {
            // 独立构造历史格式，避免用当前serializer伪造V1而掩盖兼容问题。
            var bytes = CreateV1Bytes();
            var original = (byte[])bytes.Clone();
            ushort sourceVersion;
            var migrated = MapBinarySerializer.Deserialize(bytes, out sourceVersion);
            Assert.AreEqual(1, sourceVersion);
            Assert.AreEqual(2, migrated.Version);
            Assert.AreEqual(2, migrated.Columns); Assert.AreEqual(2, migrated.Rows);
            Assert.AreEqual(0.6f, migrated.CellSize);
            Assert.AreEqual(11, migrated.NormalGlassId); Assert.AreEqual(12, migrated.ColoredGlassId);
            var expectedBases = new[] { BaseTerrainType.Track, BaseTerrainType.Empty, BaseTerrainType.Empty, BaseTerrainType.Empty };
            var expectedObstacles = new[] { ObstacleType.None, ObstacleType.None, ObstacleType.GlassNormal, ObstacleType.GlassColored };
            for (var i = 0; i < 4; i++)
            {
                var cell = new BoardCoordinate(i % 2, i / 2);
                Assert.AreEqual(expectedBases[i], migrated.GetBaseTerrain(cell), "V1编号" + (i + 1));
                Assert.AreEqual(expectedObstacles[i], migrated.GetObstacle(cell), "V1编号" + (i + 1));
            }
            CollectionAssert.AreEqual(original, bytes, "反序列化不改写输入数组。");
            CollectionAssert.AreEqual(migrated.Cells, MapBinarySerializer.Deserialize(bytes).Cells);
            var saved = MapBinarySerializer.Serialize(migrated);
            Assert.AreEqual(38, saved.Length);
            CollectionAssert.AreEqual(new byte[] { 2, 0 }, saved.Skip(4).Take(2));
            CollectionAssert.AreEqual(new byte[] { 1, 0, 2, 0, 2, 1, 2, 2 }, saved.Skip(30));
            CollectionAssert.AreEqual(migrated.Cells, MapBinarySerializer.Deserialize(saved).Cells);
        }

        [TestCase(0, 0)] [TestCase(4, 0)] [TestCase(4, 3)] [TestCase(5, 1)]
        [TestCase(6, 0)] [TestCase(6, 65)] [TestCase(10, 0)] [TestCase(10, 65)]
        [TestCase(18, 0)] [TestCase(22, 0)] [TestCase(22, 11)] [TestCase(26, 3)]
        [TestCase(30, 0)] [TestCase(31, 5)] [TestCase(32, 255)] [TestCase(33, 0)]
        public void InvalidV1HeaderAndLegacyTerrainCodesAreRejected(int offset, byte value)
        {
            var bytes = CreateV1Bytes();
            bytes[offset] = value;
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes));
        }

        [Test]
        public void V1RejectsTruncatedTrailingAndV2SizedPayloads()
        {
            var bytes = CreateV1Bytes();
            foreach (var length in new[] { 0, 5, 29, 30, bytes.Length - 1 })
                Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes.Take(length).ToArray()));
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes.Concat(new byte[] { 0 }).ToArray()));
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(bytes.Concat(new byte[4]).ToArray()));
            var v2 = MapBinarySerializer.Serialize(MapData.Create(2, 2));
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Deserialize(v2.Take(34).ToArray()),
                "V2不能按V1的一字节单格长度读取。");
        }

        [TestCase(0, 9)] [TestCase(11, 0)] [TestCase(65, 9)] [TestCase(11, 65)]
        public void InvalidDimensionsCannotAllocateMap(int columns, int rows) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => MapData.Create(columns, rows));

        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(0f)] [TestCase(-1f)]
        public void InvalidCellSizeRejected(float size) => Assert.Throws<ArgumentOutOfRangeException>(() => MapData.Create(11, 9, size));

        [Test]
        public void CreateAndCloneKeepIndependentValueCellsAndMetadata()
        {
            var map = MapData.Create();
            Assert.AreEqual(11, map.Columns); Assert.AreEqual(9, map.Rows);
            Assert.AreEqual(0.35f, map.CellSize); Assert.AreEqual(2, map.Version);
            Assert.AreEqual(99, map.Cells.Length);
            Assert.IsTrue(map.Cells.All(cell => cell.BaseTerrain == BaseTerrainType.Empty && cell.Obstacle == ObstacleType.None));
            map.NormalGlassId = 11; map.ColoredGlassId = 12;
            var coordinate = new BoardCoordinate(2, 1);
            map.SetBaseTerrain(coordinate, BaseTerrainType.Track);
            map.SetObstacle(coordinate, ObstacleType.GlassColored);
            var clone = map.Clone();
            Assert.AreNotSame(map.Cells, clone.Cells);
            CollectionAssert.AreEqual(map.Cells, clone.Cells);
            Assert.AreEqual(map.Version, clone.Version);
            Assert.AreEqual(map.Rows, clone.Rows); Assert.AreEqual(map.Columns, clone.Columns);
            Assert.AreEqual(map.CellSize, clone.CellSize);
            Assert.AreEqual(11, clone.NormalGlassId); Assert.AreEqual(12, clone.ColoredGlassId);
            var valueCopy = clone.Cells[13];
            valueCopy.BaseTerrain = BaseTerrainType.Empty;
            valueCopy.Obstacle = ObstacleType.None;
            Assert.AreEqual(BaseTerrainType.Track, clone.GetBaseTerrain(coordinate), "单格是值类型。");
            Assert.AreEqual(ObstacleType.GlassColored, clone.GetObstacle(coordinate));
            clone.Cells[13] = valueCopy;
            Assert.AreEqual(BaseTerrainType.Empty, clone.GetBaseTerrain(coordinate));
            Assert.AreEqual(ObstacleType.None, clone.GetObstacle(coordinate));
            Assert.AreEqual(BaseTerrainType.Track, map.GetBaseTerrain(coordinate));
            Assert.AreEqual(ObstacleType.GlassColored, map.GetObstacle(coordinate));
        }

        [Test]
        public void ResizePreservesLowerLeftAndDoesNotMutateSource()
        {
            var map = CreateSixCombinationMap(3, 2, 0.6f);
            map.NormalGlassId = 11; map.ColoredGlassId = 12;
            var original = map.Clone();
            var enlarged = map.Resize(5, 4);
            Assert.AreEqual(map.CellSize, enlarged.CellSize);
            Assert.AreEqual(11, enlarged.NormalGlassId); Assert.AreEqual(12, enlarged.ColoredGlassId);
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 5; column++)
                {
                    var cell = new BoardCoordinate(column, row);
                    Assert.AreEqual(column < 3 && row < 2 ? map.GetBaseTerrain(cell) : BaseTerrainType.Empty, enlarged.GetBaseTerrain(cell));
                    Assert.AreEqual(column < 3 && row < 2 ? map.GetObstacle(cell) : ObstacleType.None, enlarged.GetObstacle(cell));
                }
            var cropped = enlarged.Resize(2, 1);
            Assert.AreEqual(2, cropped.Cells.Length);
            CollectionAssert.AreEqual(map.Cells.Take(2), cropped.Cells);
            enlarged.SetBaseTerrain(new BoardCoordinate(0, 0), BaseTerrainType.Empty);
            enlarged.SetObstacle(new BoardCoordinate(0, 0), ObstacleType.GlassColored);
            CollectionAssert.AreEqual(original.Cells, map.Cells);
            CollectionAssert.AreEqual(original.Cells.Take(2), cropped.Cells);
            Assert.AreEqual(3, map.Columns); Assert.AreEqual(2, map.Rows);
        }

        [Test]
        public void LayerSettersOnlyChangeTheirOwnLayerAndCell()
        {
            var map = MapData.Create(2, 1);
            var cell = new BoardCoordinate(0, 0);
            var neighbor = new BoardCoordinate(1, 0);
            map.SetObstacle(cell, ObstacleType.GlassNormal);
            map.SetBaseTerrain(cell, BaseTerrainType.Track);
            Assert.AreEqual(ObstacleType.GlassNormal, map.GetObstacle(cell));
            map.SetObstacle(cell, ObstacleType.GlassColored);
            Assert.AreEqual(BaseTerrainType.Track, map.GetBaseTerrain(cell));
            map.SetBaseTerrain(cell, BaseTerrainType.Empty);
            Assert.AreEqual(ObstacleType.GlassColored, map.GetObstacle(cell));
            map.SetBaseTerrain(cell, BaseTerrainType.Track);
            map.SetObstacle(cell, ObstacleType.None);
            Assert.AreEqual(BaseTerrainType.Track, map.GetBaseTerrain(cell), "擦除障碍保留轨道。");
            Assert.AreEqual(BaseTerrainType.Empty, map.GetBaseTerrain(neighbor));
            Assert.AreEqual(ObstacleType.None, map.GetObstacle(neighbor));
        }

        [TestCase(0)] [TestCase(3)] [TestCase(255)]
        public void InvalidBaseTerrainSetterThrowsWithoutChangingEitherLayer(int value)
        {
            var map = CreateSixCombinationMap(3, 2);
            var original = map.Clone();
            Assert.Throws<ArgumentOutOfRangeException>(() => map.SetBaseTerrain(new BoardCoordinate(2, 1), (BaseTerrainType)value));
            CollectionAssert.AreEqual(original.Cells, map.Cells);
        }

        [TestCase(3)] [TestCase(4)] [TestCase(255)]
        public void InvalidObstacleSetterThrowsWithoutChangingEitherLayer(int value)
        {
            var map = CreateSixCombinationMap(3, 2);
            var original = map.Clone();
            Assert.Throws<ArgumentOutOfRangeException>(() => map.SetObstacle(new BoardCoordinate(0, 1), (ObstacleType)value));
            CollectionAssert.AreEqual(original.Cells, map.Cells);
        }

        [TestCase(true, 0)] [TestCase(true, 3)] [TestCase(true, 255)]
        [TestCase(false, 3)] [TestCase(false, 255)]
        public void ValidatorAndSerializerRejectInvalidRawLayerValues(bool baseLayer, int value)
        {
            var map = MapData.Create(1, 1);
            if (baseLayer) map.Cells[0].BaseTerrain = (BaseTerrainType)value;
            else map.Cells[0].Obstacle = (ObstacleType)value;
            Assert.IsTrue(MapValidator.Validate(map).Any(issue => issue.Severity == MapValidationSeverity.Error));
            Assert.Throws<InvalidDataException>(() => MapBinarySerializer.Serialize(map));
            Assert.IsFalse(map.CanPlaceUnit(new BoardCoordinate(0, 0)));
            Assert.IsFalse(map.CanEnergyBallPass(new BoardCoordinate(0, 0)));
        }

        [TestCase(BaseTerrainType.Track, ObstacleType.None)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.None)]
        [TestCase(BaseTerrainType.Track, ObstacleType.GlassNormal)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassNormal)]
        [TestCase(BaseTerrainType.Track, ObstacleType.GlassColored)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassColored)]
        public void MapBoardSetOpenOnlyAcknowledgesCurrentPermissionAndNeverEditsLayers(BaseTerrainType terrain, ObstacleType obstacle)
        {
            var map = MapData.Create(1, 1);
            var cell = new BoardCoordinate(0, 0);
            map.SetBaseTerrain(cell, terrain); map.SetObstacle(cell, obstacle);
            var board = new BoardModel(map);
            var canPlace = terrain == BaseTerrainType.Empty && obstacle == ObstacleType.None;
            foreach (var request in new[] { true, false, true, false })
            {
                Assert.AreEqual(request == canPlace, board.SetOpen(cell, request));
                Assert.AreEqual(terrain, board.GetBaseTerrain(cell));
                Assert.AreEqual(obstacle, board.GetObstacle(cell));
                Assert.AreEqual(canPlace, board.CanPlaceUnit(cell));
            }
        }

        [TestCase(BaseTerrainType.Track, ObstacleType.GlassNormal)]
        [TestCase(BaseTerrainType.Track, ObstacleType.GlassColored)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassNormal)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassColored)]
        public void RemovingGlassPreservesBaseAndIsolatesBothBoardsTemplateLayoutAndNewGame(BaseTerrainType terrain, ObstacleType obstacle)
        {
            var map = MapData.Create(4, 4);
            var cell = new BoardCoordinate(1, 1);
            var otherCell = new BoardCoordinate(2, 2);
            map.SetBaseTerrain(cell, terrain); map.SetObstacle(cell, obstacle);
            map.SetBaseTerrain(otherCell, terrain); map.SetObstacle(otherCell, obstacle);
            var original = MapBinarySerializer.Serialize(map);
            var layout = new BoardLayoutRules(map);
            var player = new BoardModel(map); var opponent = new BoardModel(map);
            Assert.IsFalse(player.CanPlaceUnit(cell)); Assert.IsFalse(player.CanEnergyBallPass(cell));
            Assert.IsFalse(player.SetOpen(cell, true), "SetOpen不能模拟玻璃破碎。");
            Assert.AreEqual(obstacle, player.GetObstacle(cell));
            Assert.IsTrue(TryRemoveObstacle(player, cell), "轨道上的障碍移除也必须报告成功。");
            Assert.IsFalse(TryRemoveObstacle(player, cell), "同一障碍重复移除返回false。");
            Assert.AreEqual(terrain, player.GetBaseTerrain(cell));
            Assert.AreEqual(ObstacleType.None, player.GetObstacle(cell));
            Assert.IsTrue(player.CanEnergyBallPass(cell));
            Assert.AreEqual(terrain == BaseTerrainType.Empty, player.CanPlaceUnit(cell));
            Assert.AreEqual(obstacle, opponent.GetObstacle(cell));
            Assert.IsFalse(opponent.CanPlaceUnit(cell)); Assert.IsFalse(opponent.CanEnergyBallPass(cell));
            Assert.IsTrue(TryRemoveObstacle(opponent, otherCell));
            Assert.AreEqual(obstacle, player.GetObstacle(otherCell), "敌方破碎同样不能修改本方。");
            Assert.AreEqual(terrain, opponent.GetBaseTerrain(otherCell));
            Assert.AreEqual(ObstacleType.None, opponent.GetObstacle(otherCell));
            Assert.AreEqual(terrain, map.GetBaseTerrain(cell)); Assert.AreEqual(obstacle, map.GetObstacle(cell));
            Assert.AreEqual(terrain, layout.GetInitialBaseTerrain(cell));
            Assert.AreEqual(obstacle, layout.GetInitialObstacle(cell)); Assert.IsTrue(layout.HasInitialGlass(cell));
            CollectionAssert.AreEqual(original, MapBinarySerializer.Serialize(map), "破碎不改写共享模板。");
            var restarted = new BoardModel(layout.CreateMap());
            Assert.AreEqual(terrain, restarted.GetBaseTerrain(cell)); Assert.AreEqual(obstacle, restarted.GetObstacle(cell));
            Assert.AreEqual(obstacle, restarted.GetObstacle(otherCell));
            var snapshot = player.CaptureMap();
            snapshot.SetBaseTerrain(cell, terrain == BaseTerrainType.Track ? BaseTerrainType.Empty : BaseTerrainType.Track);
            snapshot.SetObstacle(cell, obstacle);
            Assert.AreEqual(terrain, player.GetBaseTerrain(cell)); Assert.AreEqual(ObstacleType.None, player.GetObstacle(cell));
        }

        [TestCase(BaseTerrainType.Track, ObstacleType.GlassNormal)]
        [TestCase(BaseTerrainType.Track, ObstacleType.GlassColored)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassNormal)]
        [TestCase(BaseTerrainType.Empty, ObstacleType.GlassColored)]
        public void RemovingGlassStillChecksOccupancyCompleteFootprintAndBounds(BaseTerrainType terrain, ObstacleType obstacle)
        {
            var map = MapData.Create(4, 4);
            var anchor = new BoardCoordinate(1, 1);
            map.SetBaseTerrain(anchor, terrain); map.SetObstacle(anchor, obstacle);
            map.SetBaseTerrain(new BoardCoordinate(2, 2), BaseTerrainType.Track);
            var board = new BoardModel(map);
            var shape = new PlaceableDefinition("L", new[]
                { new BoardCoordinate(0, 0), new BoardCoordinate(1, 0), new BoardCoordinate(0, 1) });
            Assert.IsFalse(board.Evaluate(anchor, shape).IsValid);
            Assert.IsTrue(TryRemoveObstacle(board, anchor));
            var canPlace = terrain == BaseTerrainType.Empty;
            Assert.AreEqual(canPlace, board.Evaluate(anchor, shape).IsValid,
                "L形孔内轨道不应计入真实占地，底层轨道仍禁止部署。");
            Assert.IsFalse(board.Evaluate(anchor, new PlaceableDefinition("square", 2, 2)).IsValid,
                "完整矩形包含轨道，不能只检查锚点。");
            Assert.IsFalse(board.Evaluate(new BoardCoordinate(3, 3), shape).IsValid);
            Assert.IsFalse(board.TryPlace(new BoardCoordinate(3, 3), shape));
            var neighbor = new BoardCoordinate(2, 1);
            Assert.IsTrue(board.TryPlace(neighbor, new PlaceableDefinition("blocker", 1, 1)));
            Assert.IsFalse(board.Evaluate(anchor, shape).IsValid, "形状中非锚点占用也阻止部署。");
            Assert.IsFalse(board.TryPlace(anchor, shape));
            Assert.IsFalse(board.IsOccupied(anchor), "失败部署不留下部分占用。");
            Assert.IsTrue(board.Release("blocker"));
            Assert.AreEqual(canPlace, board.TryPlace(anchor, shape));
            if (!canPlace)
            {
                Assert.IsFalse(board.IsOccupied(anchor));
                Assert.IsFalse(board.TryPlace(anchor, new PlaceableDefinition("single", 1, 1)));
                return;
            }
            Assert.IsTrue(board.IsOccupied(anchor)); Assert.IsTrue(board.IsOccupied(neighbor));
            Assert.IsTrue(board.IsOccupied(new BoardCoordinate(1, 2)));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(2, 2)), "L形孔不应占用。");
            Assert.IsFalse(board.TryPlace(anchor, new PlaceableDefinition("other", 1, 1)));
            Assert.IsTrue(board.CanPlaceUnit(anchor), "地形许可不混入机器人占用。");
            Assert.IsTrue(board.CanEnergyBallPass(anchor));
            Assert.IsFalse(board.SetOpen(anchor, false)); Assert.IsTrue(board.SetOpen(anchor, true));
            Assert.AreEqual("L", board.GetOccupant(anchor));
            Assert.IsTrue(board.Release("L")); Assert.IsTrue(board.Evaluate(anchor, shape).IsValid);
        }

        [Test]
        public void BoardAndLayoutCopyBothLayersBeforeSourceEdits()
        {
            var map = MapData.Create(1, 1);
            var cell = new BoardCoordinate(0, 0);
            map.SetBaseTerrain(cell, BaseTerrainType.Track); map.SetObstacle(cell, ObstacleType.GlassColored);
            var board = new BoardModel(map); var layout = new BoardLayoutRules(map);
            map.SetBaseTerrain(cell, BaseTerrainType.Empty); map.SetObstacle(cell, ObstacleType.None);
            Assert.AreEqual(BaseTerrainType.Track, board.GetBaseTerrain(cell));
            Assert.AreEqual(ObstacleType.GlassColored, board.GetObstacle(cell));
            Assert.AreEqual(BaseTerrainType.Track, layout.GetInitialBaseTerrain(cell));
            Assert.AreEqual(ObstacleType.GlassColored, layout.GetInitialObstacle(cell));
            var layoutSnapshot = layout.CreateMap();
            layoutSnapshot.SetBaseTerrain(cell, BaseTerrainType.Empty); layoutSnapshot.SetObstacle(cell, ObstacleType.None);
            Assert.AreEqual(BaseTerrainType.Track, layout.GetInitialBaseTerrain(cell));
            Assert.AreEqual(ObstacleType.GlassColored, layout.GetInitialObstacle(cell));
        }

        [Test]
        public void MissingObstacleAndOutOfBoundsRemovalReturnFalse()
        {
            var map = MapData.Create(2, 1);
            map.SetBaseTerrain(new BoardCoordinate(0, 0), BaseTerrainType.Track);
            var board = new BoardModel(map);
            foreach (var cell in new[] { new BoardCoordinate(0, 0), new BoardCoordinate(1, 0),
                new BoardCoordinate(-1, 0), new BoardCoordinate(2, 0), new BoardCoordinate(0, 1) })
                Assert.IsFalse(TryRemoveObstacle(board, cell));
            CollectionAssert.AreEqual(map.Cells, board.CaptureMap().Cells);
        }

        [Test]
        public void LegacyBoardRejectsLayerReadsAndRetainsSetOpenAndReservedBorderBehavior()
        {
            var board = new BoardModel(5, 5, null, 1);
            var interior = new BoardCoordinate(2, 2);
            var border = new BoardCoordinate(0, 0);
            Assert.IsFalse(board.IsTerrainMap);
            Assert.Throws<InvalidOperationException>(() => board.GetBaseTerrain(interior));
            Assert.Throws<InvalidOperationException>(() => board.GetObstacle(interior));
            Assert.IsTrue(board.SetOpen(interior, false)); Assert.IsFalse(board.IsOpen(interior));
            Assert.IsTrue(board.SetOpen(interior, true)); Assert.IsTrue(board.IsOpen(interior));
            Assert.IsFalse(board.SetOpen(border, true)); Assert.IsFalse(board.CanPlaceUnit(border));
            Assert.IsFalse(board.CanEnergyBallPass(border));
            Assert.IsTrue(board.TryPlace(interior, new PlaceableDefinition("legacy", 1, 1)));
            Assert.IsFalse(board.SetOpen(interior, false)); Assert.IsTrue(board.IsOccupied(interior));
            Assert.IsTrue(board.Release("legacy")); Assert.IsTrue(board.SetOpen(interior, false));
        }

        [Test]
        public void BoundsNeighborsAndCoordinateRoundTripMatchExistingConvention()
        {
            var map = MapData.Create(11, 9);
            Assert.AreEqual(2, map.GetNeighbors(new BoardCoordinate(0, 0)).Count());
            Assert.AreEqual(4, map.GetNeighbors(new BoardCoordinate(5, 4)).Count());
            Assert.AreEqual(0, map.GetNeighbors(new BoardCoordinate(-1, 0)).Count());
            Assert.IsFalse(map.CanPlaceUnit(new BoardCoordinate(-1, 0)));
            Assert.IsFalse(map.CanEnergyBallPass(new BoardCoordinate(11, 0)));
            var board = new BoardModel(map);
            foreach (var outside in new[] { new BoardCoordinate(-1, 0), new BoardCoordinate(0, -1),
                new BoardCoordinate(11, 0), new BoardCoordinate(0, 9) })
            {
                Assert.IsFalse(map.CanPlaceUnit(outside)); Assert.IsFalse(map.CanEnergyBallPass(outside));
                Assert.Throws<ArgumentOutOfRangeException>(() => map.GetBaseTerrain(outside));
                Assert.Throws<ArgumentOutOfRangeException>(() => map.GetObstacle(outside));
                Assert.Throws<ArgumentOutOfRangeException>(() => map.SetBaseTerrain(outside, BaseTerrainType.Track));
                Assert.Throws<ArgumentOutOfRangeException>(() => map.SetObstacle(outside, ObstacleType.GlassNormal));
                Assert.Throws<ArgumentOutOfRangeException>(() => board.GetBaseTerrain(outside));
                Assert.Throws<ArgumentOutOfRangeException>(() => board.GetObstacle(outside));
                Assert.IsFalse(board.SetOpen(outside, true)); Assert.IsFalse(board.SetOpen(outside, false));
                Assert.IsFalse(board.CanPlaceUnit(outside)); Assert.IsFalse(board.CanEnergyBallPass(outside));
            }
            Assert.IsTrue(board.CanPlaceUnit(new BoardCoordinate(0, 0)), "新图外圈Empty可部署。");
            for (var row = 0; row < 9; row++)
                for (var column = 0; column < 11; column++)
                {
                    var cell = new BoardCoordinate(column, row);
                    Assert.AreEqual(cell, MapGridMath.GetCoordinate(MapGridMath.GetLocalPosition(cell, 11, 9, 0.35f), 11, 9, 0.35f));
                }
            Assert.That(MapGridMath.GetLocalPosition(new BoardCoordinate(5, 4), 11, 9, 0.35f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void DisconnectedAreasAreWarningsNotSerializationErrors()
        {
            var map = MapData.Create(3, 3);
            for (var row = 0; row < 3; row++) map.SetObstacle(new BoardCoordinate(1, row), ObstacleType.GlassNormal);
            var issues = MapValidator.Validate(map);
            Assert.IsTrue(issues.Any(i => i.Severity == MapValidationSeverity.Warning && i.Coordinate.HasValue));
            Assert.DoesNotThrow(() => MapBinarySerializer.Serialize(map));
        }

        [TestCase(5, 5)] [TestCase(4, 4)] [TestCase(5, 4)]
        public void CenterClearanceIncludesOddAndEvenGridGeometry(int columns, int rows)
        {
            var map = MapData.Create(columns, rows, 1f);
            Assert.IsFalse(MapValidator.ValidateBallClearance(map, 0.2f).Any(i => i.Severity == MapValidationSeverity.Error));
            map.SetObstacle(new BoardCoordinate(columns / 2, rows / 2), ObstacleType.GlassNormal);
            Assert.IsTrue(MapValidator.ValidateBallClearance(map, 0.2f).Any(i => i.Severity == MapValidationSeverity.Error));
        }

        [Test]
        public void NarrowBoardCannotContainSpawnCircle() => Assert.IsTrue(
            MapValidator.ValidateBallClearance(MapData.Create(1, 1), 0.2f).Any(i => i.Severity == MapValidationSeverity.Error));

        [Test]
        public void LayoutRecognizesZeroAndMultipleColoredGlassWithoutInventingTrackGlass()
        {
            var map = MapData.Create();
            var track = new BoardCoordinate(0, 0);
            map.SetBaseTerrain(track, BaseTerrainType.Track);
            Assert.IsFalse(new BoardLayoutRules(map).HasInitialGlass(track));
            map.SetObstacle(new BoardCoordinate(1, 0), ObstacleType.GlassColored);
            map.SetObstacle(new BoardCoordinate(2, 0), ObstacleType.GlassColored);
            var layout = new BoardLayoutRules(map);
            Assert.AreEqual(0, layout.ReservedBorderWidth);
            Assert.AreEqual(2, layout.GetGlassConfigId(new BoardCoordinate(1, 0)));
            Assert.AreEqual(2, layout.GetGlassConfigId(new BoardCoordinate(2, 0)));
            Assert.IsTrue(layout.HasInitialGlass(new BoardCoordinate(2, 0)));
        }

        [Test]
        public void LayoutQueriesBothLayersAndGlassConfigForAllSixCombinations()
        {
            var map = CreateSixCombinationMap(3, 2);
            map.NormalGlassId = 11; map.ColoredGlassId = 12;
            var layout = new BoardLayoutRules(map);
            Assert.AreEqual(0, layout.ReservedBorderWidth);
            for (var i = 0; i < map.Cells.Length; i++)
            {
                var cell = new BoardCoordinate(i % 3, i / 3);
                var obstacle = map.Cells[i].Obstacle;
                Assert.AreEqual(map.Cells[i].BaseTerrain, layout.GetInitialBaseTerrain(cell));
                Assert.AreEqual(obstacle, layout.GetInitialObstacle(cell));
                Assert.AreEqual(obstacle != ObstacleType.None, layout.HasInitialGlass(cell));
                Assert.AreEqual(map.CanPlaceUnit(cell), layout.OpenCells.Contains(cell));
                if (obstacle != ObstacleType.None)
                    Assert.AreEqual(obstacle == ObstacleType.GlassNormal ? 11 : 12, layout.GetGlassConfigId(cell));
            }
            Assert.IsFalse(layout.HasInitialGlass(new BoardCoordinate(-1, 0)));
            Assert.IsFalse(layout.HasInitialGlass(new BoardCoordinate(3, 0)));
        }

        [UnityTest]
        public IEnumerator Maximum64By64LocalFileLoaderUsesV2ValidationAndCancellation()
        {
            return UniTask.ToCoroutine(async () => await CheckMaximumLocalFileAsync());
        }

        [UnityTest]
        public IEnumerator V1LocalFileLoaderMigratesWithoutWritingBack()
        {
            return UniTask.ToCoroutine(async () => await CheckV1LocalFileAsync());
        }

        private static async UniTask CheckMaximumLocalFileAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "Map_Test_" + Guid.NewGuid().ToString("N") + ".bytes");
            try
            {
                var map = CreateSixCombinationMap(64, 64, 0.6f);
                map.NormalGlassId = 11; map.ColoredGlassId = 12;
                var bytes = MapBinarySerializer.Serialize(map);
                Assert.AreEqual(30 + 2 * 64 * 64, bytes.Length);
                File.WriteAllBytes(path, bytes);
                var restored = await MapLoader.LoadFileAsync(path).AsTask();
                Assert.AreEqual(64, restored.Columns); Assert.AreEqual(64, restored.Rows);
                Assert.AreEqual(2, restored.Version); Assert.AreEqual(0.6f, restored.CellSize);
                Assert.AreEqual(11, restored.NormalGlassId); Assert.AreEqual(12, restored.ColoredGlassId);
                CollectionAssert.AreEqual(map.Cells, restored.Cells);
                CollectionAssert.AreEqual(bytes, MapBinarySerializer.Serialize(restored));
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    try { await MapLoader.LoadFileAsync(path, cancellation.Token).AsTask(); Assert.Fail("应取消文件读取"); }
                    catch (OperationCanceledException) { }
                }
                var corrupted = (byte[])bytes.Clone();
                corrupted[corrupted.Length - 1] = 255;
                File.WriteAllBytes(path, corrupted);
                try { await MapLoader.LoadFileAsync(path).AsTask(); Assert.Fail("异步加载也必须拒绝非法障碍编号。"); }
                catch (InvalidDataException) { }
                File.WriteAllBytes(path, bytes.Take(bytes.Length - 1).ToArray());
                try { await MapLoader.LoadFileAsync(path).AsTask(); Assert.Fail("异步加载必须拒绝截断的V2文件。"); }
                catch (InvalidDataException) { }
                File.WriteAllBytes(path, bytes.Concat(new byte[] { 0 }).ToArray());
                try { await MapLoader.LoadFileAsync(path).AsTask(); Assert.Fail("异步加载必须拒绝超长文件。"); }
                catch (InvalidDataException) { }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static async UniTask CheckV1LocalFileAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "Map_V1_Test_" + Guid.NewGuid().ToString("N") + ".bytes");
            try
            {
                var bytes = CreateV1Bytes();
                File.WriteAllBytes(path, bytes);
                var lastWrite = File.GetLastWriteTimeUtc(path);
                var restored = await MapLoader.LoadFileAsync(path).AsTask();
                Assert.AreEqual(2, restored.Version);
                CollectionAssert.AreEqual(new[] { BaseTerrainType.Track, BaseTerrainType.Empty, BaseTerrainType.Empty, BaseTerrainType.Empty },
                    restored.Cells.Select(cell => cell.BaseTerrain));
                CollectionAssert.AreEqual(new[] { ObstacleType.None, ObstacleType.None, ObstacleType.GlassNormal, ObstacleType.GlassColored },
                    restored.Cells.Select(cell => cell.Obstacle));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path), "加载V1只迁移内存，不自动保存V2。");
                Assert.AreEqual(lastWrite, File.GetLastWriteTimeUtc(path));
                var invalid = (byte[])bytes.Clone();
                invalid[33] = 5;
                File.WriteAllBytes(path, invalid);
                try { await MapLoader.LoadFileAsync(path).AsTask(); Assert.Fail("异步加载必须拒绝V1未知类型。"); }
                catch (InvalidDataException) { }
                CollectionAssert.AreEqual(invalid, File.ReadAllBytes(path), "失败加载也不能改写原文件。");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static MapData CreateSixCombinationMap(int columns, int rows, float cellSize = 0.35f)
        {
            var map = MapData.Create(columns, rows, cellSize);
            for (var i = 0; i < map.Cells.Length; i++)
                map.Cells[i] = new MapCellData
                {
                    BaseTerrain = i % 2 == 0 ? BaseTerrainType.Track : BaseTerrainType.Empty,
                    Obstacle = (ObstacleType)(i / 2 % 3)
                };
            return map;
        }

        private static byte[] CreateV1Bytes()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 70, 71, 77, 80 });
                writer.Write((ushort)1);
                writer.Write(2); writer.Write(2); writer.Write(0.6f);
                writer.Write(11); writer.Write(12); writer.Write(4);
                writer.Write(new byte[] { 1, 2, 3, 4 });
                return stream.ToArray();
            }
        }

        private static bool TryRemoveObstacle(BoardModel board, BoardCoordinate coordinate)
        {
            // 测试位于独立程序集，保持运行时方法为internal而不扩大生产接口。
            var method = typeof(BoardModel).GetMethod("TryRemoveObstacle", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(BoardCoordinate) }, null);
            Assert.IsNotNull(method, "BoardModel需提供internal bool TryRemoveObstacle(BoardCoordinate)。");
            return (bool)method.Invoke(board, new object[] { coordinate });
        }
    }
}
