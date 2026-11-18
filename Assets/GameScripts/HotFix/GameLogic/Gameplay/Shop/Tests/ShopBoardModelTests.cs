using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public class ShopBoardModelTests
    {
        private static bool RemoveObstacle(BoardModel board, BoardCoordinate coordinate)
            => (bool)typeof(BoardModel).GetMethod("TryRemoveObstacle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(board, new object[] { coordinate });

        [Test]
        public void ClonePreservesLegacyRulesOccupantsAndHighlightsWithoutSharingWrites()
        {
            var source = new BoardModel(5, 4, null, reservedBorderWidth: 1);
            var closed = new BoardCoordinate(3, 2);
            var border = new BoardCoordinate(0, 0);
            var anchor = new BoardCoordinate(1, 1);
            source.SetOpen(closed, false);
            source.SetHighlighted(border, true);
            Assert.IsTrue(source.TryPlace(anchor, new PlaceableDefinition("L", new[] {
                new BoardCoordinate(0, 0), new BoardCoordinate(1, 0), new BoardCoordinate(0, 1) })));

            var copy = source.Clone();

            Assert.AreEqual(5, copy.Columns);
            Assert.AreEqual(4, copy.Rows);
            Assert.IsFalse(copy.IsTerrainMap);
            Assert.IsFalse(copy.IsOpen(closed));
            Assert.IsFalse(copy.SetOpen(border, true), "复制后仍须保留不可重开的外圈");
            Assert.IsTrue(copy.IsHighlighted(border));
            Assert.AreEqual("L", copy.GetOccupant(new BoardCoordinate(2, 1)));
            Assert.AreEqual("L", copy.GetOccupant(new BoardCoordinate(1, 2)));
            Assert.IsFalse(copy.IsOccupied(new BoardCoordinate(2, 2)), "稀疏形状的空洞不能被填满");
            Assert.IsTrue(copy.Release("L"));
            Assert.IsTrue(copy.SetOpen(closed, true));
            copy.SetHighlighted(border, false);
            Assert.IsTrue(source.IsOccupied(anchor));
            Assert.IsFalse(source.IsOpen(closed));
            Assert.IsTrue(source.IsHighlighted(border));
            Assert.IsTrue(source.SetOpen(new BoardCoordinate(2, 2), false));
            Assert.IsTrue(copy.IsOpen(new BoardCoordinate(2, 2)), "源与副本的开放数组必须双向隔离");
        }

        [Test]
        public void ClonePreservesTerrainMetadataAndIndependentObstacleState()
        {
            var map = MapData.Create(4, 3, 0.75f);
            map.NormalGlassId = 7;
            map.ColoredGlassId = 9;
            var track = new BoardCoordinate(0, 1);
            var normal = new BoardCoordinate(1, 1);
            var coloredTrack = new BoardCoordinate(2, 1);
            map.SetBaseTerrain(track, BaseTerrainType.Track);
            map.SetObstacle(normal, ObstacleType.GlassNormal);
            map.SetBaseTerrain(coloredTrack, BaseTerrainType.Track);
            map.SetObstacle(coloredTrack, ObstacleType.GlassColored);
            var source = new BoardModel(map);
            source.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition("wide", 2, 1));

            var copy = source.Clone();

            Assert.IsTrue(copy.IsTerrainMap);
            Assert.AreEqual(BaseTerrainType.Track, copy.GetBaseTerrain(track));
            Assert.IsTrue(copy.CanEnergyBallPass(track));
            Assert.IsFalse(copy.CanPlaceUnit(track));
            Assert.AreEqual(ObstacleType.GlassNormal, copy.GetObstacle(normal));
            Assert.AreEqual(ObstacleType.GlassColored, copy.GetObstacle(coloredTrack));
            var captured = copy.CaptureMap();
            Assert.AreEqual(MapData.CURRENT_VERSION, captured.Version);
            Assert.AreEqual(0.75f, captured.CellSize);
            Assert.AreEqual(7, captured.NormalGlassId);
            Assert.AreEqual(9, captured.ColoredGlassId);
            captured.SetBaseTerrain(track, BaseTerrainType.Empty);
            Assert.AreEqual(BaseTerrainType.Track, copy.GetBaseTerrain(track));
            Assert.IsTrue(RemoveObstacle(copy, coloredTrack));
            Assert.IsTrue(copy.CanEnergyBallPass(coloredTrack));
            Assert.IsFalse(copy.CanPlaceUnit(coloredTrack), "移除玻璃不能改变轨道的部署规则");
            Assert.AreEqual(ObstacleType.GlassColored, source.GetObstacle(coloredTrack));
            Assert.IsTrue(RemoveObstacle(source, normal));
            Assert.AreEqual(ObstacleType.GlassNormal, copy.GetObstacle(normal));
            Assert.IsTrue(copy.Release("wide"));
            Assert.AreEqual("wide", source.GetOccupant(new BoardCoordinate(1, 0)));
        }

        [Test]
        public void AiSnapshotPreservesReservedBorderAndIsolatesEveryPublicBoard()
        {
            var source = new BoardModel(5, 5, null, reservedBorderWidth: 1);
            var anchor = new BoardCoordinate(1, 1);
            var closed = new BoardCoordinate(3, 3);
            source.TryPlace(anchor, new PlaceableDefinition("wide", 2, 1));
            source.SetOpen(closed, false);
            source.SetHighlighted(anchor, true);
            var snapshot = Snapshot(source);
            source.Release("wide");
            source.SetOpen(closed, true);

            var first = snapshot.Board;
            Assert.IsFalse(first.SetOpen(new BoardCoordinate(0, 0), true));
            Assert.IsFalse(first.IsOpen(closed));
            Assert.AreEqual("wide", first.GetOccupant(new BoardCoordinate(2, 1)));
            Assert.IsTrue(first.IsHighlighted(anchor));
            first.Release("wide");
            first.SetOpen(closed, true);
            first.SetHighlighted(anchor, false);
            var second = snapshot.Board;
            Assert.AreNotSame(first, second);
            Assert.AreEqual("wide", second.GetOccupant(anchor));
            Assert.IsFalse(second.IsOpen(closed));
            Assert.IsTrue(second.IsHighlighted(anchor));
        }

        [Test]
        public void WithWorldSharesCapturedDataWithoutLeakingMutableBoards()
        {
            var map = MapData.Create(3, 2);
            var glass = new BoardCoordinate(2, 1);
            map.SetObstacle(glass, ObstacleType.GlassNormal);
            var source = new BoardModel(map);
            var anchor = new BoardCoordinate(0, 0);
            source.TryPlace(anchor, new PlaceableDefinition("unit", 1, 1));
            var snapshot = Snapshot(source);
            var world = new OpponentAiWorldSnapshot(default(OpponentAiBallState), null,
                boardRevision: 17, phase: OpponentAiStrategyPhase.ComboControl);

            var updated = snapshot.WithWorld(world);

            Assert.AreSame(world, updated.World);
            Assert.IsNull(snapshot.World);
            Assert.AreEqual(17, updated.BoardRevision);
            Assert.AreEqual(OpponentAiStrategyPhase.ComboControl, updated.StrategyPhase);
            Assert.AreEqual(BattleSide.Opponent, updated.Side);
            Assert.AreEqual(23, updated.EnergyCoins);
            Assert.AreEqual(7, updated.RefreshPrice);
            Assert.AreEqual(123, updated.BattleId);
            Assert.IsTrue(updated.HasPurchasedShopRobots);
            Assert.AreSame(snapshot.Offers, updated.Offers);
            Assert.AreSame(snapshot.Robots, updated.Robots);
            Assert.AreSame(snapshot.CandidateProfiles, updated.CandidateProfiles);
            source.Release("unit");
            RemoveObstacle(source, glass);
            var exposed = updated.Board;
            Assert.IsTrue(exposed.Release("unit"));
            Assert.IsTrue(RemoveObstacle(exposed, glass));
            foreach (var independent in new[] { snapshot.Board, updated.Board, updated.WithWorld(null).Board })
            {
                Assert.AreEqual("unit", independent.GetOccupant(anchor));
                Assert.AreEqual(ObstacleType.GlassNormal, independent.GetObstacle(glass));
            }
        }

        [Test]
        public void DenseSnapshotBoardCopiesDoNotAllocatePerOccupiedCellDefinitions()
        {
            var source = new BoardModel(MapData.Create(64, 64));
            source.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition("full", 64, 64));
            var snapshot = Snapshot(source);
            var allocatedBytes = AllocationCounter();
            var copy = snapshot.Board; // 预热，不计入复制预算。
            var before = allocatedBytes();
            for (var i = 0; i < 8; i++) copy = snapshot.Board;
            var allocated = allocatedBytes() - before;

            Assert.Less(allocated, 1024L * 1024L, "八个64×64副本只需独立数组，不应逐格重建定义和放置临时集合");
            Assert.AreEqual("full", copy.GetOccupant(new BoardCoordinate(63, 63)));
        }

        [Test]
        public void WithWorldDoesNotAllocateAnotherBoardOrCapturedLists()
        {
            var snapshot = Snapshot(new BoardModel(MapData.Create(64, 64)));
            var allocatedBytes = AllocationCounter();
            var updated = snapshot.WithWorld(null);
            var before = allocatedBytes();
            for (var i = 0; i < 64; i++) updated = updated.WithWorld(null);
            var allocated = allocatedBytes() - before;

            Assert.Less(allocated, 16L * 1024L, "只替换World时分配应限于快照壳，不能随地图面积增长");
            Assert.AreEqual(64, updated.Board.Columns);
        }

        private static OpponentAiSnapshot Snapshot(BoardModel board)
            => new OpponentAiSnapshot(BattleSide.Opponent, 23, board,
                Array.Empty<OpponentAiOfferSnapshot>(), Array.Empty<OpponentAiRobotSnapshot>(),
                Array.Empty<OpponentAiRobotProfile>(), refreshPrice: 7, battleId: 123, hasPurchasedShopRobots: true);

        private static Func<long> AllocationCounter()
        {
            // 兼容缺少此计数器的旧运行时；避免用受GC时机影响的进程总堆大小断言。
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            if (method == null) { Assert.Ignore("当前运行时没有线程分配计数器。"); return null; }
            return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        }

        [Test]
        public void ClosedCellsRejectPlacementWithoutWritingAndReleasePreservesOthers()
        {
            var board = new BoardModel(3, 2, new[] { new BoardCoordinate(0, 0), new BoardCoordinate(1, 0) });
            Assert.IsTrue(board.IsOpen(new BoardCoordinate(0, 0)));
            Assert.IsFalse(board.IsOpen(new BoardCoordinate(2, 0)));
            Assert.IsFalse(board.IsOpen(new BoardCoordinate(-1, 0)));
            Assert.IsFalse(board.TryPlace(new BoardCoordinate(1, 0), new PlaceableDefinition("wide", 2, 1)));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(1, 0)));
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition("one", 1, 1)));
            Assert.IsFalse(board.SetOpen(new BoardCoordinate(0, 0), false));
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(1, 0), new PlaceableDefinition("other", 1, 1)));
            Assert.IsTrue(board.Release("one"));
            Assert.IsFalse(board.Release("one"));
            Assert.IsFalse(board.Release(null));
            Assert.AreEqual("other", board.GetOccupant(new BoardCoordinate(1, 0)));
            Assert.IsTrue(board.SetOpen(new BoardCoordinate(0, 0), false));
            Assert.IsFalse(board.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition("new", 1, 1)));
        }

        [Test]
        public void ReleaseClearsEveryCellOfOnlyTheRequestedInstance()
        {
            var board = new BoardModel(4, 1);
            var shape = new PlaceableDefinition("1", 2, 1);
            board.TryPlace(new BoardCoordinate(0, 0), shape);
            board.TryPlace(new BoardCoordinate(2, 0), new PlaceableDefinition("2", 2, 1));
            Assert.IsFalse(board.Evaluate(new BoardCoordinate(1, 0), shape).IsValid);
            Assert.AreEqual("1", board.GetOccupant(new BoardCoordinate(1, 0)));
            Assert.IsTrue(board.Release("1"));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 0)));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(1, 0)));
            Assert.AreEqual("2", board.GetOccupant(new BoardCoordinate(2, 0)));
            Assert.AreEqual("2", board.GetOccupant(new BoardCoordinate(3, 0)));
        }
    }
}
