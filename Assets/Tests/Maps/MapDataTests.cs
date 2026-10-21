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
        public void BinaryRoundTripPreservesMetadataAndRowMajorTerrain()
        {
            var map = MapData.Create(7, 5, 0.6f);
            map.NormalGlassId = 11; map.ColoredGlassId = 12;
            for (var i = 0; i < map.Terrains.Length; i++) map.Terrains[i] = (TerrainType)(i % 4 + 1);
            var bytes = MapBinarySerializer.Serialize(map);
            Assert.AreEqual(30 + 35, bytes.Length);
            CollectionAssert.AreEqual(new byte[] { 70, 71, 77, 80, 1, 0, 7, 0, 0, 0 }, bytes.Take(10));
            var restored = MapBinarySerializer.Deserialize(bytes);
            Assert.AreEqual(map.Rows, restored.Rows); Assert.AreEqual(map.Columns, restored.Columns);
            Assert.AreEqual(map.CellSize, restored.CellSize);
            Assert.AreEqual(11, restored.NormalGlassId); Assert.AreEqual(12, restored.ColoredGlassId);
            CollectionAssert.AreEqual(map.Terrains, restored.Terrains);
            Assert.AreEqual(map.Terrains[9], restored.GetTerrain(new BoardCoordinate(2, 1)));
        }

        [TestCase(0, 0)] [TestCase(4, 2)] [TestCase(6, 0)] [TestCase(10, 65)]
        [TestCase(18, 0)] [TestCase(26, 1)] [TestCase(30, 0)] [TestCase(30, 5)]
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

        [TestCase(0, 9)] [TestCase(11, 0)] [TestCase(65, 9)] [TestCase(11, 65)]
        public void InvalidDimensionsCannotAllocateMap(int columns, int rows) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => MapData.Create(columns, rows));

        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(0f)] [TestCase(-1f)]
        public void InvalidCellSizeRejected(float size) => Assert.Throws<ArgumentOutOfRangeException>(() => MapData.Create(11, 9, size));

        [Test]
        public void ResizePreservesLowerLeftAndDoesNotMutateSource()
        {
            var map = MapData.Create(3, 2);
            map.SetTerrain(new BoardCoordinate(2, 1), TerrainType.GlassColored);
            map.SetTerrain(new BoardCoordinate(0, 0), TerrainType.Track);
            var enlarged = map.Resize(5, 4);
            Assert.AreEqual(TerrainType.GlassColored, enlarged.GetTerrain(new BoardCoordinate(2, 1)));
            Assert.AreEqual(TerrainType.Empty, enlarged.GetTerrain(new BoardCoordinate(4, 3)));
            var cropped = enlarged.Resize(2, 1);
            Assert.AreEqual(2, cropped.Terrains.Length);
            Assert.AreEqual(TerrainType.Track, cropped.GetTerrain(new BoardCoordinate(0, 0)));
            Assert.AreEqual(3, map.Columns); Assert.AreEqual(2, map.Rows);
        }

        [Test]
        public void BoardClonesTerrainAndChecksFootprintOccupancyAndTrack()
        {
            var map = MapData.Create(4, 4);
            var track = new BoardCoordinate(1, 0);
            var normal = new BoardCoordinate(0, 1);
            var colored = new BoardCoordinate(1, 1);
            map.SetTerrain(track, TerrainType.Track); map.SetTerrain(normal, TerrainType.GlassNormal);
            map.SetTerrain(colored, TerrainType.GlassColored);
            var player = new BoardModel(map); var opponent = new BoardModel(map);
            Assert.IsTrue(player.CanPlaceUnit(new BoardCoordinate(0, 0)), "新图外圈Empty可部署");
            Assert.IsTrue(player.CanEnergyBallPass(track)); Assert.IsFalse(player.CanPlaceUnit(track));
            Assert.IsFalse(player.SetOpen(track, true), "旧开放接口不能把轨道改为空位");
            Assert.IsFalse(player.Evaluate(new BoardCoordinate(0, 0), new PlaceableDefinition("wide", 2, 1)).IsValid);
            Assert.IsTrue(player.SetOpen(normal, true)); Assert.IsTrue(player.SetOpen(colored, true));
            Assert.AreEqual(TerrainType.Empty, player.GetTerrain(normal));
            Assert.IsTrue(player.CanEnergyBallPass(colored));
            Assert.AreEqual(TerrainType.GlassNormal, opponent.GetTerrain(normal));
            Assert.AreEqual(TerrainType.GlassNormal, map.GetTerrain(normal));
            Assert.IsTrue(player.TryPlace(normal, new PlaceableDefinition("robot", 1, 1)));
            Assert.IsFalse(player.TryPlace(normal, new PlaceableDefinition("other", 1, 1)));
            Assert.IsTrue(player.CanPlaceUnit(normal), "地形查询不混入机器人占用");
            player.CaptureMap().SetTerrain(track, TerrainType.Empty);
            Assert.IsFalse(player.IsOpen(track), "快照不能修改局内地图");
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
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTerrain(new BoardCoordinate(11, 0)));
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
            for (var row = 0; row < 3; row++) map.SetTerrain(new BoardCoordinate(1, row), TerrainType.GlassNormal);
            var issues = MapValidator.Validate(map);
            Assert.IsTrue(issues.Any(i => i.Severity == MapValidationSeverity.Warning && i.Coordinate.HasValue));
            Assert.DoesNotThrow(() => MapBinarySerializer.Serialize(map));
        }

        [TestCase(5, 5)] [TestCase(4, 4)] [TestCase(5, 4)]
        public void CenterClearanceIncludesOddAndEvenGridGeometry(int columns, int rows)
        {
            var map = MapData.Create(columns, rows, 1f);
            Assert.IsFalse(MapValidator.ValidateBallClearance(map, 0.2f).Any(i => i.Severity == MapValidationSeverity.Error));
            map.SetTerrain(new BoardCoordinate(columns / 2, rows / 2), TerrainType.GlassNormal);
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
            map.SetTerrain(track, TerrainType.Track);
            Assert.IsFalse(new BoardLayoutRules(map).HasInitialGlass(track));
            map.SetTerrain(new BoardCoordinate(1, 0), TerrainType.GlassColored);
            map.SetTerrain(new BoardCoordinate(2, 0), TerrainType.GlassColored);
            var layout = new BoardLayoutRules(map);
            Assert.AreEqual(0, layout.ReservedBorderWidth);
            Assert.AreEqual(2, layout.GetGlassConfigId(new BoardCoordinate(1, 0)));
            Assert.AreEqual(2, layout.GetGlassConfigId(new BoardCoordinate(2, 0)));
            Assert.IsTrue(layout.HasInitialGlass(new BoardCoordinate(2, 0)));
        }

        [UnityTest]
        public IEnumerator LocalFileLoaderUsesSameBinaryValidationAndCancellation()
        {
            return UniTask.ToCoroutine(async () => await CheckLocalFileAsync());
        }

        private static async UniTask CheckLocalFileAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "Map_Test_" + Guid.NewGuid().ToString("N") + ".bytes");
            try
            {
                File.WriteAllBytes(path, MapBinarySerializer.Serialize(MapData.Create(6, 7)));
                var restored = await MapLoader.LoadFileAsync(path).AsTask();
                Assert.AreEqual(6, restored.Columns); Assert.AreEqual(7, restored.Rows);
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    try { await MapLoader.LoadFileAsync(path, cancellation.Token).AsTask(); Assert.Fail("应取消文件读取"); }
                    catch (OperationCanceledException) { }
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
