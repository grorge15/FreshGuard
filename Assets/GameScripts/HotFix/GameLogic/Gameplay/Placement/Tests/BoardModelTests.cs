using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public class BoardModelTests
    {
        [Test]
        public void WorldBoardReservesOuterRingAndCannotReopenIt()
        {
            using (var fixture = new BoardFixture(Vector3.zero))
            {
                var board = fixture.View.Model;
                var one = new PlaceableDefinition("One", 1, 1);
                var allowedCount = 0;
                for (var column = 0; column < 11; column++)
                    for (var row = 0; row < 9; row++)
                    {
                        var coordinate = new BoardCoordinate(column, row);
                        Assert.IsTrue(board.IsInside(coordinate));
                        var border = column == 0 || column == 10 || row == 0 || row == 8;
                        if (border)
                        {
                            Assert.IsFalse(board.IsOpen(coordinate));
                            Assert.IsFalse(board.SetOpen(coordinate, true));
                            Assert.IsFalse(board.TryPlace(coordinate, one));
                            Assert.IsFalse(board.IsOccupied(coordinate));
                        }
                        else
                        {
                            Assert.IsTrue(board.Evaluate(coordinate, one).IsValid);
                            allowedCount++;
                        }
                    }
                Assert.AreEqual(63, allowedCount);
                Assert.IsTrue(board.TryPlace(new BoardCoordinate(1, 1), one));
                Assert.IsTrue(board.TryPlace(new BoardCoordinate(9, 7), one));
            }
        }

        [TestCase(0, 4)]
        [TestCase(10, 4)]
        [TestCase(5, 0)]
        [TestCase(5, 8)]
        [TestCase(0, 0)]
        [TestCase(10, 8)]
        public void BorderDragShowsRedAndCannotCommit(int column, int row)
        {
            using (var fixture = new DragFixture())
            {
                var slot = fixture.Player.Slot(column, row);
                var normal = slot.color;
                Assert.IsTrue(fixture.Controller.BeginDrag(new PlaceableDefinition("One", 1, 1), new BoardCoordinate(0, 0)));
                fixture.UpdateAt(fixture.Player, column, row);
                Assert.Greater(slot.color.r, slot.color.g);
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(column, row)));
                Assert.AreEqual(normal, slot.color);
            }
        }

        [Test]
        public void FootprintTouchingBorderRejectsEveryCellAtomically()
        {
            using (var fixture = new DragFixture())
            {
                var shape = new PlaceableDefinition("Square", 2, 2);
                fixture.Controller.BeginDrag(shape, new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 9, 6);
                foreach (var cell in new[] { new BoardCoordinate(9, 6), new BoardCoordinate(10, 6), new BoardCoordinate(9, 7), new BoardCoordinate(10, 7) })
                {
                    var color = fixture.Player.Slot(cell.Column, cell.Row).color;
                    Assert.Greater(color.r, color.g);
                }
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(9, 6)));
                fixture.Controller.BeginDrag(shape, new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 8, 6);
                Assert.IsTrue(fixture.Controller.TryCommit(out target));
                Assert.IsTrue(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(9, 7)));
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(10, 7)));
            }
        }

        private static PlaceableDefinition LShape()
        {
            return new PlaceableDefinition("L", new[] { new BoardCoordinate(0, 0), new BoardCoordinate(0, 1), new BoardCoordinate(1, 0) });
        }

        [Test]
        public void SparseShapeOnlyOccupiesSpecifiedCells()
        {
            var board = new BoardModel(11, 9);
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(2, 2), LShape()));
            Assert.IsTrue(board.IsOccupied(new BoardCoordinate(2, 2)));
            Assert.IsTrue(board.IsOccupied(new BoardCoordinate(2, 3)));
            Assert.IsTrue(board.IsOccupied(new BoardCoordinate(3, 2)));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(3, 3)));
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(3, 3), new PlaceableDefinition("Hole", 1, 1)));
        }

        [Test]
        public void TShapeIgnoresBoundingRectangleHoles()
        {
            var shape = new PlaceableDefinition("T", new[] { new BoardCoordinate(0, 1), new BoardCoordinate(1, 1), new BoardCoordinate(2, 1), new BoardCoordinate(1, 0) });
            var board = new BoardModel(11, 9);
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 0), new PlaceableDefinition("Hole", 1, 1)));
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 0), shape));
            Assert.AreEqual("Hole", board.GetOccupant(new BoardCoordinate(0, 0)));
            Assert.AreEqual("T", board.GetOccupant(new BoardCoordinate(2, 1)));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(2, 0)));
        }

        [Test]
        public void FootprintValidationAndCopyPreventLaterShapeMutation()
        {
            Assert.Throws<ArgumentException>(() => new PlaceableDefinition("Empty", new BoardCoordinate[0]));
            Assert.Throws<ArgumentException>(() => new PlaceableDefinition("Negative", new[] { new BoardCoordinate(-1, 0) }));
            Assert.Throws<ArgumentException>(() => new PlaceableDefinition("Duplicate", new[] { new BoardCoordinate(0, 0), new BoardCoordinate(0, 0) }));
            var cells = new[] { new BoardCoordinate(0, 0), new BoardCoordinate(0, 1) };
            var shape = new PlaceableDefinition("Copy", cells);
            cells[1] = new BoardCoordinate(10, 10);
            Assert.IsTrue(new BoardModel(11, 9).TryPlace(new BoardCoordinate(0, 0), shape));
        }

        [Test]
        public void OneByThreeOccupiesThreeVerticalCellsWithoutRotation()
        {
            var board = new BoardModel(11, 9);
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(4, 2), new PlaceableDefinition("Three", 1, 3)));
            Assert.AreEqual("Three", board.GetOccupant(new BoardCoordinate(4, 4)));
            Assert.IsNull(board.GetOccupant(new BoardCoordinate(5, 2)));
        }

        [Test]
        public void InvalidPlacementDoesNotWriteAnyOccupancy()
        {
            var board = new BoardModel(11, 9);
            var definition = new PlaceableDefinition("Three", 1, 3);
            var anchor = new BoardCoordinate(10, 7);
            Assert.AreEqual(2, board.Evaluate(anchor, definition).Cells.Count);
            Assert.IsFalse(board.TryPlace(anchor, definition));
            Assert.IsFalse(board.IsOccupied(anchor));
            Assert.IsTrue(board.TryPlace(new BoardCoordinate(0, 1), new PlaceableDefinition("Block", 1, 1)));
            Assert.IsFalse(board.TryPlace(new BoardCoordinate(0, 0), definition));
            Assert.IsFalse(board.IsOccupied(new BoardCoordinate(0, 0)));
            Assert.AreEqual("Block", board.GetOccupant(new BoardCoordinate(0, 1)));
        }

        [Test]
        public void WorldBoardIsPassiveMonoBehaviour()
        {
            using (var fixture = new BoardFixture(Vector3.zero))
            {
                Assert.AreEqual(0, fixture.Root.transform.Find("Slots").GetComponentsInChildren<Collider2D>().Length);
                Assert.AreEqual(0, fixture.Root.transform.Find("Slots").GetComponentsInChildren<Rigidbody2D>().Length);
                Assert.AreEqual(0, fixture.Root.GetComponentsInChildren<Canvas>().Length);
                Assert.AreEqual(0, fixture.Root.transform.Find("Slots").GetComponentsInChildren<MonoBehaviour>().Length);
            }
        }

        [Test]
        public void ReinitializationReusesSlotsWallsAndOccupancy()
        {
            using (var fixture = new BoardFixture(Vector3.zero))
            {
                var slot = fixture.Slot(0, 0);
                var top = fixture.Root.transform.Find("Boundary/Top");
                Assert.IsTrue(fixture.View.Model.TryPlace(new BoardCoordinate(1, 1), new PlaceableDefinition("Keep", 1, 1)));
                fixture.View.DisableBoundary();
                Assert.IsTrue(fixture.View.Initialize());
                Assert.AreSame(slot, fixture.Slot(0, 0));
                Assert.AreSame(top, fixture.Root.transform.Find("Boundary/Top"));
                Assert.AreEqual("Keep", fixture.View.Model.GetOccupant(new BoardCoordinate(1, 1)));
                Assert.AreEqual(99, fixture.Root.transform.Find("Slots").childCount);
                Assert.AreEqual(4, fixture.Root.transform.Find("Boundary").childCount);
                Assert.IsTrue(top.GetComponent<BoxCollider2D>().enabled);
            }
        }

        [Test]
        public void CoordinateConversionUsesWorldAnchorAndGridBounds()
        {
            using (var fixture = new BoardFixture(new Vector3(0f, -2f, 0f)))
            {
                var point = fixture.View.GetCellWorldPosition(new BoardCoordinate(10, 8));
                Assert.That(point.x, Is.EqualTo(1.75f).Within(0.0001f));
                Assert.That(point.y, Is.EqualTo(-0.6f).Within(0.0001f));
                BoardCoordinate coordinate;
                Assert.IsTrue(fixture.View.TryGetCoordinate(point, out coordinate));
                Assert.AreEqual(new BoardCoordinate(10, 8), coordinate);
                Assert.IsFalse(fixture.View.TryGetCoordinate(new Vector3(1.93f, -2f, 0f), out coordinate));
                Assert.That(fixture.Slot(10, 8).transform.position.z, Is.EqualTo(1f));
            }
        }

        [Test]
        public void WallsLieOutsideExactBoardEdgesAndConfigureIsIdempotent()
        {
            using (var fixture = new BoardFixture(Vector3.zero))
            {
                var boundary = fixture.View.Boundary;
                boundary.Configure(new Vector2(3.85f, 3.15f), 0.25f);
                var top = boundary.transform.Find("Top").GetComponent<BoxCollider2D>();
                var right = boundary.transform.Find("Right").GetComponent<BoxCollider2D>();
                Assert.That(top.transform.localPosition.y - top.size.y / 2f, Is.EqualTo(1.575f).Within(0.0001f));
                Assert.That(right.transform.localPosition.x - right.size.x / 2f, Is.EqualTo(1.925f).Within(0.0001f));
                Assert.AreEqual(4, boundary.transform.childCount);
                Assert.IsFalse(top.isTrigger);
            }
        }

        [Test]
        public void PreviewRestoresHighlightedOccupiedAndNormalColors()
        {
            using (var fixture = new BoardFixture(Vector3.zero))
            {
                var cell = new BoardCoordinate(1, 1);
                var normal = fixture.Slot(1, 1).color;
                var one = new PlaceableDefinition("One", 1, 1);
                fixture.View.ShowPreview(fixture.View.Model.Evaluate(cell, one));
                Assert.Greater(fixture.Slot(1, 1).color.g, fixture.Slot(1, 1).color.r);
                fixture.View.ClearPreview();
                Assert.AreEqual(normal, fixture.Slot(1, 1).color);
                Assert.IsTrue(fixture.View.Model.TryPlace(cell, one));
                fixture.View.RefreshOccupied(new[] { cell });
                var occupied = fixture.Slot(1, 1).color;
                fixture.View.SetHighlighted(cell, true);
                var highlighted = fixture.Slot(1, 1).color;
                Assert.AreNotEqual(occupied, highlighted);
                fixture.View.ShowPreview(fixture.View.Model.Evaluate(cell, one));
                Assert.Greater(fixture.Slot(1, 1).color.r, fixture.Slot(1, 1).color.g);
                fixture.View.ClearPreview();
                Assert.AreEqual(highlighted, fixture.Slot(1, 1).color);
                fixture.View.SetHighlighted(cell, false);
                Assert.AreEqual(occupied, fixture.Slot(1, 1).color);
                Assert.IsTrue(fixture.View.Model.IsOccupied(cell));
            }
        }

        [Test]
        public void GrabbedCellOffsetsAnchorAndCommitCannotRepeat()
        {
            using (var fixture = new DragFixture())
            {
                Assert.IsFalse(fixture.Controller.BeginDrag(LShape(), new BoardCoordinate(1, 1)));
                Assert.IsTrue(fixture.Controller.BeginDrag(LShape(), new BoardCoordinate(0, 1)));
                fixture.UpdateAt(fixture.Player, 2, 3);
                BoardPlacementTarget target;
                Assert.IsTrue(fixture.Controller.TryCommit(out target));
                Assert.AreEqual(new BoardCoordinate(2, 2), target.Anchor);
                Assert.AreSame(fixture.Player.Root.transform, target.EntityParent);
                Assert.That(target.AnchorWorldPosition.x, Is.EqualTo(-1.05f).Within(0.0001f));
                Assert.That(target.AnchorWorldPosition.y, Is.EqualTo(-2.7f).Within(0.0001f));
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(3, 3)));
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
            }
        }

        [Test]
        public void ShopDragRejectsOpponentBoardBeforeOccupancyChanges()
        {
            using (var fixture = new DragFixture())
            {
                var begin = typeof(PlacementController).GetMethod("BeginDrag", new[]
                {
                    typeof(PlaceableDefinition), typeof(BoardCoordinate), typeof(BattleSide)
                });
                Assert.IsNotNull(begin, "商店拖拽必须显式约束所属方，不能先占敌方格再回滚。");
                Assert.IsTrue((bool)begin.Invoke(fixture.Controller, new object[]
                {
                    new PlaceableDefinition("ShopRobot", 1, 1), new BoardCoordinate(0, 0), BattleSide.Player
                }));
                fixture.UpdateAt(fixture.Enemy, 0, 0);
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.IsFalse(fixture.Enemy.View.Model.IsOccupied(new BoardCoordinate(0, 0)));
            }
        }

        [Test]
        public void ShopTargetInspectionDoesNotReserveCells()
        {
            using (var fixture = new DragFixture())
            {
                var inspect = typeof(PlacementController).GetMethod("TryGetDragTarget");
                Assert.IsNotNull(inspect, "准备异步视图前读取目标不能写入棋盘。");
                fixture.Controller.BeginDrag(new PlaceableDefinition("ShopRobot", 1, 1), new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 0, 0);
                var arguments = new object[] { null };
                Assert.IsTrue((bool)inspect.Invoke(fixture.Controller, arguments));
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(0, 0)));
            }
        }

        [Test]
        public void CrossingBoardsLeavingAndCancellationRestorePreview()
        {
            using (var fixture = new DragFixture())
            {
                var normal = fixture.Player.Slot(0, 0).color;
                Assert.IsTrue(fixture.Controller.BeginDrag(new PlaceableDefinition("One", 1, 1), new BoardCoordinate(0, 0)));
                fixture.UpdateAt(fixture.Player, 0, 0);
                Assert.AreNotEqual(normal, fixture.Player.Slot(0, 0).color);
                fixture.UpdateAt(fixture.Enemy, 0, 0);
                Assert.AreEqual(normal, fixture.Player.Slot(0, 0).color);
                Assert.AreNotEqual(normal, fixture.Enemy.Slot(0, 0).color);
                fixture.Controller.UpdateDrag(fixture.Camera.WorldToScreenPoint(new Vector3(10f, 10f, 0f)));
                Assert.AreEqual(normal, fixture.Enemy.Slot(0, 0).color);
                Assert.IsTrue(fixture.Controller.IsDragging);
                fixture.Controller.CancelDrag();
                Assert.IsFalse(fixture.Controller.IsDragging);
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
            }
        }

        [Test]
        public void InvalidDropAndChangedOccupancyCannotCommit()
        {
            using (var fixture = new DragFixture())
            {
                var normal = fixture.Player.Slot(10, 8).color;
                fixture.Controller.BeginDrag(new PlaceableDefinition("Three", 1, 3), new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 10, 8);
                Assert.Greater(fixture.Player.Slot(10, 8).color.r, fixture.Player.Slot(10, 8).color.g);
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.AreEqual(normal, fixture.Player.Slot(10, 8).color);
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(10, 8)));
                var one = new PlaceableDefinition("One", 1, 1);
                fixture.Controller.BeginDrag(one, new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 1, 1);
                Assert.IsTrue(fixture.Player.View.Model.TryPlace(new BoardCoordinate(1, 1), new PlaceableDefinition("Other", 1, 1)));
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.AreEqual("Other", fixture.Player.View.Model.GetOccupant(new BoardCoordinate(1, 1)));
            }
        }

        [Test]
        public void DisabledControllerCannotCommitOrKeepPreview()
        {
            using (var fixture = new DragFixture())
            {
                var normal = fixture.Player.Slot(0, 0).color;
                fixture.Controller.BeginDrag(new PlaceableDefinition("One", 1, 1), new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 0, 0);
                fixture.Controller.enabled = false;
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.IsFalse(fixture.Controller.IsDragging);
                Assert.AreEqual(normal, fixture.Player.Slot(0, 0).color);
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(0, 0)));
            }
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        [Test]
        public void MissingBallKeepsPlacementAndWallsDisabled()
        {
            using (var fixture = new DragFixture())
            {
                var root = new GameObject("BootstrapTest");
                try
                {
                    var bootstrap = root.AddComponent<PlacementBoardsBootstrap>();
                    SetField(bootstrap, "_controller", fixture.Controller);
                    UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                        new System.Text.RegularExpressions.Regex(".*缺少小球引用.*"));
                    Assert.IsFalse(bootstrap.Initialize());
                    Assert.IsFalse(fixture.Controller.IsReady);
                    Assert.IsFalse(fixture.Player.View.Boundary.transform.Find("Top").GetComponent<BoxCollider2D>().enabled);
                    Assert.IsFalse(fixture.Enemy.View.Boundary.transform.Find("Top").GetComponent<BoxCollider2D>().enabled);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void FailedBoardInitializationDisablesWallsAndCanRetry()
        {
            using (var fixture = new BoardFixture(Vector3.zero))
            {
                var root = new GameObject("RetryBoard");
                try
                {
                    var boundaryObject = new GameObject("Boundary");
                    boundaryObject.transform.SetParent(root.transform, false);
                    var boundary = boundaryObject.AddComponent<PhysicalCollisionBoundary>();
                    boundary.Configure(new Vector2(3.85f, 3.15f), 0.25f);
                    var board = root.AddComponent<PlacementBoardView>();
                    UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                        new System.Text.RegularExpressions.Regex(".*棋盘初始化失败.*"));
                    Assert.IsFalse(board.Initialize());
                    Assert.IsFalse(boundary.transform.Find("Top").GetComponent<BoxCollider2D>().enabled);
                    var template = typeof(PlacementBoardView).GetField("_slotPrefab", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(fixture.View);
                    SetField(board, "_slotPrefab", template);
                    Assert.IsTrue(board.Initialize());
                    Assert.AreEqual(99, root.transform.Find("Slots").childCount);
                    Assert.AreEqual(4, boundary.transform.childCount);
                    Assert.IsTrue(boundary.transform.Find("Top").GetComponent<BoxCollider2D>().enabled);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void InactiveBoardRejectsCommitAndInitializationUntilReenabled()
        {
            using (var fixture = new DragFixture())
            {
                fixture.Controller.BeginDrag(new PlaceableDefinition("One", 1, 1), new BoardCoordinate(0, 0));
                fixture.UpdateAt(fixture.Player, 0, 0);
                fixture.Player.Root.SetActive(false);
                Assert.IsFalse(fixture.Controller.IsReady);
                BoardPlacementTarget target;
                Assert.IsFalse(fixture.Controller.TryCommit(out target));
                Assert.IsFalse(fixture.Player.View.Model.IsOccupied(new BoardCoordinate(0, 0)));
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex(".*摆放控制器.*"));
                Assert.IsFalse(fixture.Controller.Initialize());
                Assert.IsFalse(fixture.Enemy.View.Boundary.transform.Find("Top").GetComponent<BoxCollider2D>().enabled);
                fixture.Player.Root.SetActive(true);
                Assert.IsTrue(fixture.Controller.Initialize());
                Assert.IsTrue(fixture.Controller.IsReady);
            }
        }

        private sealed class BoardFixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly PlacementBoardView View;
            private readonly GameObject _template;
            private readonly Sprite _sprite;
            private readonly Texture2D _texture;
            public BoardFixture(Vector3 position)
            {
                _texture = new Texture2D(1, 1);
                _texture.SetPixel(0, 0, Color.white);
                _texture.Apply();
                _sprite = Sprite.Create(_texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                _template = new GameObject("Template", typeof(SpriteRenderer));
                var renderer = _template.GetComponent<SpriteRenderer>();
                renderer.sprite = _sprite;
                Root = new GameObject("WorldBoard");
                Root.transform.position = position;
                View = Root.AddComponent<PlacementBoardView>();
                SetField(View, "_slotPrefab", renderer);
                Assert.IsTrue(View.Initialize());
            }
            public SpriteRenderer Slot(int column, int row) => Root.transform.Find("Slots/Slot_" + column + "_" + row).GetComponent<SpriteRenderer>();
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
                UnityEngine.Object.DestroyImmediate(_template);
                UnityEngine.Object.DestroyImmediate(_sprite);
                UnityEngine.Object.DestroyImmediate(_texture);
            }
        }

        private sealed class DragFixture : IDisposable
        {
            public readonly BoardFixture Player = new BoardFixture(new Vector3(0f, -2f, 0f));
            public readonly BoardFixture Enemy = new BoardFixture(new Vector3(0f, 2f, 0f));
            public readonly PlacementController Controller;
            public readonly Camera Camera;
            private readonly GameObject _host = new GameObject("DragTest");
            private readonly GameObject _camera = new GameObject("DragCamera");
            public DragFixture()
            {
                Camera = _camera.AddComponent<Camera>();
                Camera.orthographic = true;
                Camera.orthographicSize = 5f;
                Camera.transform.position = new Vector3(0f, 0f, -10f);
                Camera.pixelRect = new Rect(0f, 0f, 800f, 800f);
                Controller = _host.AddComponent<PlacementController>();
                SetField(Controller, "_playerBoard", Player.View);
                SetField(Controller, "_enemyBoard", Enemy.View);
                SetField(Controller, "_sceneCamera", Camera);
                Assert.IsTrue(Controller.Initialize());
            }
            public void UpdateAt(BoardFixture board, int column, int row)
            {
                Controller.UpdateDrag(Camera.WorldToScreenPoint(board.View.GetCellWorldPosition(new BoardCoordinate(column, row))));
            }
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_host);
                UnityEngine.Object.DestroyImmediate(_camera);
                Player.Dispose();
                Enemy.Dispose();
            }
        }
    }
}
