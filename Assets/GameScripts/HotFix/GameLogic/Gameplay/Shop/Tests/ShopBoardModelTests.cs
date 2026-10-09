using NUnit.Framework;

namespace GameLogic.Tests
{
    public class ShopBoardModelTests
    {
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
