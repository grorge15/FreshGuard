using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    /// <summary>V3 EditMode 测试；所有对象均为临时对象，不读取配置或场景资源。</summary>
    public sealed class BattleMapLayoutControllerTests
    {
        private const float Epsilon = 0.0001f;
        private const float ReferenceCellSize = 0.35f;
        private GameObject _root;
        private BattleMapLayoutController _layout;
        private Side _player, _opponent;
        private Camera _camera;
        private RectTransform _shop;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MapLayoutV3Tests");
            _layout = _root.AddComponent<BattleMapLayoutController>();
            _player = CreateSide(true);
            _opponent = CreateSide(false);
            _camera = Child("Camera", _root.transform).gameObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.transform.position = new Vector3(2f, 3f, -20f);
            _camera.transform.rotation = Quaternion.Euler(3f, 7f, 11f);
            _camera.orthographicSize = 7f;
            _camera.rect = new Rect(0.05f, 0.1f, 0.9f, 0.85f);
            _shop = new GameObject("Shop", typeof(RectTransform)).GetComponent<RectTransform>();
            _shop.SetParent(_root.transform, false);
            _shop.anchorMin = new Vector2(0.1f, 0.05f);
            _shop.anchorMax = new Vector2(0.9f, 0.3f);
            _shop.anchoredPosition = new Vector2(13f, -17f);
            _shop.sizeDelta = new Vector2(200f, 75f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [TestCase(11, 9, 1f)]
        [TestCase(22, 18, 0.5f)]
        [TestCase(13, 13, 9f / 13f)]
        [TestCase(1, 1, 9f)]
        [TestCase(64, 64, 9f / 64f)]
        public void KnownSizesApplyAndRestoreFromOriginalScale(int columns, int rows, float expected)
        {
            Configure();
            SetSize(_player.Board, columns, rows, ReferenceCellSize);
            SetSize(_opponent.Board, columns, rows, ReferenceCellSize);
            _layout.Apply();
            AssertScale(_player, expected, expected);
            AssertScale(_opponent, expected, expected);
            _layout.Restore();
            _layout.Restore();
            AssertScale(_player, 1f, 1f);
            AssertScale(_opponent, 1f, 1f);
        }

        // 三种格距，每个用例同时验证 n×1 与 1×n；n 完整覆盖 1..64。
        private static IEnumerable<TestCaseData> ExtremeMaps()
        {
            foreach (var cellSize in new[] { 0.1f, 0.35f, 0.8f })
                for (var span = 1; span <= 64; span++)
                    yield return new TestCaseData(span, cellSize);
        }

        [TestCaseSource(nameof(ExtremeMaps))]
        public void ExtremeSpansFitAndStayCenteredForDifferentCellSizes(int span, float cellSize)
        {
            var playerPose = new Pose(_player.Board.transform);
            var opponentPose = new Pose(_opponent.Board.transform);
            Configure();
            SetSize(_player.Board, span, 1, cellSize);
            SetSize(_opponent.Board, 1, span, cellSize);
            _layout.Apply();
            var playerFit = ExpectedFit(span, 1, cellSize);
            var opponentFit = ExpectedFit(1, span, cellSize);
            AssertScale(_player, playerFit, playerFit * cellSize / ReferenceCellSize);
            AssertScale(_opponent, opponentFit, opponentFit * cellSize / ReferenceCellSize);
            playerPose.AssertPositionRotationParent();
            opponentPose.AssertPositionRotationParent();
            AssertCenteredFit(_player, span, 1, cellSize);
            AssertCenteredFit(_opponent, 1, span, cellSize);
            _layout.Restore();
            AssertScale(_player, 1f, 1f);
            AssertScale(_opponent, 1f, 1f);
        }

        [TestCase(13, 13)]
        [TestCase(64, 1)]
        [TestCase(1, 64)]
        [TestCase(6, 4)]
        [TestCase(22, 18)]
        public void BoardCenterRotationAndZStayFixedWithSymmetricLetterboxing(int columns, int rows)
        {
            var playerPose = new Pose(_player.Board.transform);
            var opponentPose = new Pose(_opponent.Board.transform);
            Configure();
            SetBothSizes(columns, rows, ReferenceCellSize);
            _layout.Apply();
            playerPose.AssertPositionRotationParent();
            opponentPose.AssertPositionRotationParent();
            AssertCenteredFit(_player, columns, rows, ReferenceCellSize);
            AssertCenteredFit(_opponent, columns, rows, ReferenceCellSize);
            _layout.Restore();
            playerPose.AssertUnchanged();
            opponentPose.AssertUnchanged();
        }

        [Test]
        public void DifferentOriginalUniformScalesAndParentScalesArePreservedPerSide()
        {
            Assert.That(_player.WorldScale.x, Is.Not.EqualTo(_opponent.WorldScale.x));
            var roots = Capture(new[] { _player.Root, _opponent.Root });
            Configure();
            SetSize(_player.Board, 22, 18, 0.35f);
            SetSize(_opponent.Board, 13, 13, 0.7f);
            _layout.Apply();
            AssertScale(_player, 0.5f, 0.5f);
            AssertScale(_opponent, 9f / 26f, 9f / 13f);
            AssertStates(roots);
            _layout.Restore();
            AssertScale(_player, 1f, 1f);
            AssertScale(_opponent, 1f, 1f);
            AssertStates(roots);
        }

        [Test]
        public void ReferenceIsUnavailableBeforeConfigureAndForUnboundBoard()
        {
            Vector3 scale;
            float cellSize;
            Assert.That(_layout.TryGetReference(_player.Board, out scale, out cellSize), Is.False);
            Configure();
            var unbound = Child("UnboundBoard", _root.transform).gameObject.AddComponent<PlacementBoardView>();
            Assert.That(_layout.TryGetReference(unbound, out scale, out cellSize), Is.False);
        }

        [Test]
        public void MapSwitchAndRepeatedConfigureNeverRecaptureScaledReferences()
        {
            Configure();
            for (var iteration = 0; iteration < 8; iteration++)
            {
                AssertReference(_player);
                AssertReference(_opponent);
                SetSize(_player.Board, 22, 18, 0.7f);
                SetSize(_opponent.Board, 13, 13, 0.175f);
                _layout.Apply();
                AssertScale(_player, 0.25f, 0.5f);
                AssertScale(_opponent, 18f / 13f, 9f / 13f);
                Configure();
                AssertReference(_player);
                AssertReference(_opponent);
                _layout.Apply();
                AssertScale(_player, 0.25f, 0.5f);
                AssertScale(_opponent, 18f / 13f, 9f / 13f);
                SetBothSizes(11, 9, ReferenceCellSize);
                _layout.Apply();
                AssertScale(_player, 1f, 1f);
                AssertScale(_opponent, 1f, 1f);
                _layout.Restore();
                AssertReference(_player);
                AssertReference(_opponent);
            }
        }

        [Test]
        public void RepeatedApplyAndRestartRestoreDoNotAccumulateScaleOrMoveBalls()
        {
            var original = Capture(AllTransforms());
            Configure();
            for (var restart = 0; restart < 10; restart++)
            {
                SetBothSizes(22, 18, ReferenceCellSize);
                for (var apply = 0; apply < 3; apply++)
                {
                    _layout.Apply();
                    AssertScale(_player, 0.5f, 0.5f);
                    AssertScale(_opponent, 0.5f, 0.5f);
                }
                _layout.Restore();
                _layout.Restore();
                AssertStates(original);
                AssertScale(_player, 1f, 1f);
                AssertScale(_opponent, 1f, 1f);
                Configure();
            }
        }

        [Test]
        public void CameraPositionRotationSizeAndRectNeverChangeDuringApplyAndRestore()
        {
            var pose = new Pose(_camera.transform);
            var size = _camera.orthographicSize;
            var rect = _camera.rect;
            Configure();
            foreach (var map in new[] { new Vector2Int(64, 1), new Vector2Int(1, 64), new Vector2Int(13, 13) })
            {
                SetBothSizes(map.x, map.y, 0.7f);
                _layout.Apply();
                AssertCamera(pose, size, rect);
                _layout.Restore();
                AssertCamera(pose, size, rect);
            }
        }

        [Test]
        public void RestorePreservesCameraChangesMadeByAnotherSystemAfterApply()
        {
            Configure();
            SetBothSizes(22, 18, ReferenceCellSize);
            _layout.Apply();
            _camera.transform.position = new Vector3(-8f, 12f, -30f);
            _camera.transform.rotation = Quaternion.Euler(9f, 17f, 23f);
            _camera.orthographicSize = 13.5f;
            _camera.rect = new Rect(0.17f, 0.23f, 0.65f, 0.6f);
            var pose = new Pose(_camera.transform);
            var size = _camera.orthographicSize;
            var rect = _camera.rect;
            _layout.Restore();
            _layout.Restore();
            AssertCamera(pose, size, rect);
            SetBothSizes(13, 13, ReferenceCellSize);
            Configure();
            _layout.Apply();
            AssertCamera(pose, size, rect);
            _layout.Restore();
            AssertCamera(pose, size, rect);
        }

        [Test]
        public void BackgroundBeltTowerRouteActorWorldRootsAndShopKeepTheirSceneTransforms()
        {
            var original = Capture(UnmanagedTransforms());
            var anchorMin = _shop.anchorMin;
            var anchorMax = _shop.anchorMax;
            var size = _shop.sizeDelta;
            var position = _shop.anchoredPosition;
            Configure();
            SetSize(_player.Board, 64, 1, 0.7f);
            SetSize(_opponent.Board, 1, 64, 0.1f);
            _layout.Apply();
            AssertStates(original);
            _layout.Restore();
            AssertStates(original);
            Assert.That(_shop.anchorMin, Is.EqualTo(anchorMin));
            Assert.That(_shop.anchorMax, Is.EqualTo(anchorMax));
            Assert.That(_shop.sizeDelta, Is.EqualTo(size));
            Assert.That(_shop.anchoredPosition, Is.EqualTo(position));
        }

        [Test]
        public void RestoreDoesNotRollBackExternalBackgroundTowerRouteOrUiEdits()
        {
            Configure();
            SetBothSizes(22, 18, ReferenceCellSize);
            _layout.Apply();
            _player.Background.localScale = new Vector3(3f, 8f, 1f);
            _opponent.Tower.position += new Vector3(2f, -3f, 0f);
            _player.Route[0].localRotation = Quaternion.Euler(0f, 0f, 71f);
            _shop.anchoredPosition += new Vector2(21f, 30f);
            var changed = Capture(UnmanagedTransforms());
            _layout.Restore();
            AssertStates(changed);
        }

        [TestCase(22, 18, 0.35f, 0.5f, 0.5f)]
        [TestCase(22, 18, 0.7f, 0.25f, 0.5f)]
        [TestCase(13, 13, 0.175f, 18f / 13f, 9f / 13f)]
        public void BallSizeAndEffectiveSpeedShareCellScaleWhileConfigurationStaysUnchanged(
            int columns, int rows, float cellSize, float fit, float cell)
        {
            var playerPose = new Pose(_player.Ball.transform);
            var opponentPose = new Pose(_opponent.Ball.transform);
            var playerSettings = new BallSettings(_player.Ball);
            var opponentSettings = new BallSettings(_opponent.Ball);
            var playerRadius = _player.Ball.WorldRadius;
            var opponentRadius = _opponent.Ball.WorldRadius;
            Configure();
            SetBothSizes(columns, rows, cellSize);
            _layout.Apply();
            AssertScale(_player, fit, cell);
            AssertScale(_opponent, fit, cell);
            Assert.That(_player.Ball.WorldRadius, Is.EqualTo(playerRadius * cell).Within(Epsilon));
            Assert.That(_opponent.Ball.WorldRadius, Is.EqualTo(opponentRadius * cell).Within(Epsilon));
            playerPose.AssertPositionRotationParent();
            opponentPose.AssertPositionRotationParent();
            playerSettings.AssertUnchanged();
            opponentSettings.AssertUnchanged();
            _layout.Restore();
            AssertScale(_player, 1f, 1f);
            AssertScale(_opponent, 1f, 1f);
            playerPose.AssertUnchanged();
            opponentPose.AssertUnchanged();
            playerSettings.AssertUnchanged();
            opponentSettings.AssertUnchanged();
        }

        [Test]
        public void MapScaleComposesWithDraggingAndBerserkWithoutMutatingBaseSpeed()
        {
            Configure();
            SetBothSizes(22, 18, ReferenceCellSize);
            _layout.Apply();
            var ball = _player.Ball;
            ball.SetDragging(true);
            Assert.That(ball.MapScale, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed * 0.5f * 0.25f).Within(Epsilon));
            ball.SetBerserk(true);
            Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed * 0.5f).Within(Epsilon));
            ball.SetBerserk(false);
            Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed * 0.5f * 0.25f).Within(Epsilon));
            ball.SetDragging(false);
            Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed * 0.5f).Within(Epsilon));
            Assert.That(ball.Speed, Is.EqualTo(_player.Speed));
            _layout.Restore();
            Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed).Within(Epsilon));
            Assert.That(ball.MapScale, Is.EqualTo(1f).Within(Epsilon));
        }

        [TestCase(0.5f)]
        [TestCase(1.5f)]
        public void SetMapScaleUsesOriginalXyAndPreservesZPoseParentAndConfiguration(float multiplier)
        {
            var ball = _player.Ball;
            var pose = new Pose(ball.transform);
            var settings = new BallSettings(ball);
            for (var iteration = 0; iteration < 4; iteration++)
            {
                ball.SetMapScale(multiplier);
                AssertVector(ScaleXy(_player.BallScale, multiplier), ball.transform.localScale);
                Assert.That(ball.MapScale, Is.EqualTo(multiplier).Within(Epsilon));
                Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed * multiplier).Within(Epsilon));
                pose.AssertPositionRotationParent();
                settings.AssertUnchanged();
            }
            ball.SetMapScale(1f);
            pose.AssertUnchanged();
            Assert.That(ball.EffectiveSpeed, Is.EqualTo(_player.Speed).Within(Epsilon));
            settings.AssertUnchanged();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ConfigureRejectsMissingBoardOrBall(int missing)
        {
            AssertBindingRejected(() => _layout.Configure(
                missing == 0 ? null : _player.Board, missing == 1 ? null : _opponent.Board,
                missing == 2 ? null : _player.Ball, missing == 3 ? null : _opponent.Ball));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ConfigureRejectsSharedBoardOrSharedBall(bool shareBoard)
        {
            AssertBindingRejected(() => _layout.Configure(_player.Board,
                shareBoard ? _player.Board : _opponent.Board, _player.Ball,
                shareBoard ? _opponent.Ball : _player.Ball));
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(0, 2)]
        [TestCase(2, 0)]
        [TestCase(0, 3)]
        [TestCase(3, 0)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        [TestCase(1, 3)]
        [TestCase(3, 1)]
        [TestCase(2, 3)]
        [TestCase(3, 2)]
        public void ConfigureRejectsEveryBoardBallNestingDirection(int parentIndex, int childIndex)
        {
            // 排除非等比缩放干扰，单独验证层级冲突。
            _player.Ball.transform.localScale = Vector3.one;
            _opponent.Ball.transform.localScale = Vector3.one;
            var nodes = new[] { _player.Board.transform, _opponent.Board.transform,
                _player.Ball.transform, _opponent.Ball.transform };
            nodes[childIndex].SetParent(nodes[parentIndex], false);
            AssertBindingRejected(Configure);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ConfigureRejectsBoardAndBallOnTheSameTransform(bool player)
        {
            var side = player ? _player : _opponent;
            var ball = AddBall(side.Board.gameObject, side.Speed);
            AssertBindingRejected(() => _layout.Configure(_player.Board, _opponent.Board,
                player ? ball : _player.Ball, player ? _opponent.Ball : ball));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ConfigureRejectsNonUniformWorldXyOnEitherBoardOrItsParent(bool player, bool parent)
        {
            var side = player ? _player : _opponent;
            var target = parent ? side.Root : side.Board.transform;
            target.localScale = new Vector3(1.4f, 0.8f, 1f);
            var original = Capture(AllTransforms());
            Assert.Throws<InvalidOperationException>(Configure);
            AssertStates(original);
            Assert.That(_player.Ball.MapScale, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(_opponent.Ball.MapScale, Is.EqualTo(1f).Within(Epsilon));
        }

        private void Configure() => _layout.Configure(_player.Board, _opponent.Board, _player.Ball, _opponent.Ball);

        [Test]
        public void AuthoredTowerAndRouteUnderBoardKeepWorldLayoutAndRestoreHierarchy()
        {
            _player.Tower.SetParent(_player.Board.transform, true);
            _player.RouteRoot.SetParent(_player.Board.transform, true);
            var towerPosition = _player.Tower.position;
            var routePosition = _player.Route[0].position;
            var towerScale = _player.Tower.lossyScale;
            var routeRotation = _player.Route[0].rotation;
            var towerIndex = _player.Tower.GetSiblingIndex();
            Configure();
            SetBothSizes(13, 13, 0.7f);
            for (var iteration = 0; iteration < 3; iteration++)
            {
                _layout.Apply();
                AssertVector(towerPosition, _player.Tower.position);
                AssertVector(routePosition, _player.Route[0].position);
                AssertVector(towerScale, _player.Tower.lossyScale);
                Assert.That(Quaternion.Angle(routeRotation, _player.Route[0].rotation), Is.LessThan(Epsilon));
                _layout.Restore();
                Assert.That(_player.Tower.parent, Is.EqualTo(_player.Board.transform));
                Assert.That(_player.RouteRoot.parent, Is.EqualTo(_player.Board.transform));
                Assert.That(_player.Tower.GetSiblingIndex(), Is.EqualTo(towerIndex));
                AssertVector(towerPosition, _player.Tower.position);
                AssertVector(routePosition, _player.Route[0].position);
            }
        }

        [Test]
        public void RestoreBeforeApplyHidesPartiallyInitializedContentAndWalls()
        {
            Configure();
            var slots = Child("Slots", _player.Board.transform);
            var boundary = Child("Boundary", _player.Board.transform).gameObject.AddComponent<PhysicalCollisionBoundary>();
            boundary.Configure(new Vector2(7.7f, 6.3f), 0.5f);
            SetField(_player.Board, "_boundary", boundary);
            _layout.Restore();
            Assert.That(slots.gameObject.activeSelf, Is.False);
            foreach (var collider in boundary.GetComponentsInChildren<Collider2D>()) Assert.That(collider.enabled, Is.False);
            AssertScale(_player, 1f, 1f);
        }

        [Test]
        public void RestoreHidesDeployedRobotBeforeParentScaleReturnsToOriginal()
        {
            Configure();
            SetBothSizes(22, 18, ReferenceCellSize);
            var robot = Child("Robot", _player.Board.transform).gameObject.AddComponent<RobotView>();
            var collider = robot.gameObject.AddComponent<BoxCollider2D>();
            _layout.Apply();
            _layout.Restore();
            Assert.That(robot.gameObject.activeInHierarchy, Is.False);
            Assert.That(collider.isActiveAndEnabled, Is.False);
            AssertScale(_player, 1f, 1f);
        }
        private void SetBothSizes(int columns, int rows, float cellSize)
        {
            SetSize(_player.Board, columns, rows, cellSize);
            SetSize(_opponent.Board, columns, rows, cellSize);
        }

        // 独立计算期望值，避免用被测 GetFitScale 同时作为断言期望值。
        private static float ExpectedFit(int columns, int rows, float cellSize) =>
            Mathf.Min(11f * ReferenceCellSize / (columns * cellSize),
                9f * ReferenceCellSize / (rows * cellSize));

        private void AssertReference(Side side)
        {
            Vector3 scale;
            float cellSize;
            Assert.That(_layout.TryGetReference(side.Board, out scale, out cellSize), Is.True);
            AssertVector(side.BoardScale, scale);
            Assert.That(cellSize, Is.EqualTo(ReferenceCellSize));
        }

        private void AssertCamera(Pose pose, float size, Rect rect)
        {
            pose.AssertUnchanged();
            Assert.That(_camera.orthographic, Is.True);
            Assert.That(_camera.orthographicSize, Is.EqualTo(size));
            Assert.That(_camera.rect, Is.EqualTo(rect));
        }

        private void AssertBindingRejected(TestDelegate configure)
        {
            var original = Capture(AllTransforms());
            var exception = Assert.Catch(configure);
            Assert.That(exception, Is.InstanceOf<ArgumentException>().Or.InstanceOf<InvalidOperationException>());
            AssertStates(original);
            Assert.That(_player.Ball.MapScale, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(_opponent.Ball.MapScale, Is.EqualTo(1f).Within(Epsilon));
        }

        private static void AssertCenteredFit(Side side, int columns, int rows, float cellSize)
        {
            var board = side.Board;
            var first = board.GetCellWorldPosition(new BoardCoordinate(0, 0));
            var last = board.GetCellWorldPosition(new BoardCoordinate(columns - 1, rows - 1));
            AssertVector(board.transform.position, (first + last) * 0.5f);
            var cellRight = board.transform.TransformVector(new Vector3(cellSize, 0f, 0f));
            var cellUp = board.transform.TransformVector(new Vector3(0f, cellSize, 0f));
            var left = first - cellRight * 0.5f;
            var right = board.GetCellWorldPosition(new BoardCoordinate(columns - 1, 0)) + cellRight * 0.5f;
            var bottom = first - cellUp * 0.5f;
            var top = board.GetCellWorldPosition(new BoardCoordinate(0, rows - 1)) + cellUp * 0.5f;
            var width = Vector3.Distance(left, right);
            var height = Vector3.Distance(bottom, top);
            var referenceWidth = 11f * ReferenceCellSize * side.WorldScale.x;
            var referenceHeight = 9f * ReferenceCellSize * side.WorldScale.y;
            Assert.That(width, Is.LessThanOrEqualTo(referenceWidth + Epsilon));
            Assert.That(height, Is.LessThanOrEqualTo(referenceHeight + Epsilon));
            var horizontalGap = (referenceWidth - width) * 0.5f;
            var verticalGap = (referenceHeight - height) * 0.5f;
            Assert.That(Mathf.Min(horizontalGap, verticalGap), Is.EqualTo(0f).Within(Epsilon));
            // 对比沿棋盘自身轴的距离，旋转棋盘也应左右、上下居中。
            var leftDistance = Vector3.Dot(board.transform.position - left, cellRight.normalized);
            var rightDistance = Vector3.Dot(right - board.transform.position, cellRight.normalized);
            var bottomDistance = Vector3.Dot(board.transform.position - bottom, cellUp.normalized);
            var topDistance = Vector3.Dot(top - board.transform.position, cellUp.normalized);
            Assert.That(leftDistance, Is.EqualTo(rightDistance).Within(Epsilon));
            Assert.That(bottomDistance, Is.EqualTo(topDistance).Within(Epsilon));
            if (columns * 9 != rows * 11)
                Assert.That(Mathf.Max(horizontalGap, verticalGap), Is.GreaterThan(Epsilon));
        }

        private Transform[] UnmanagedTransforms() => new[] { _player.Root, _opponent.Root,
            _player.Background, _opponent.Background, _player.Belt, _opponent.Belt,
            _player.Tower, _opponent.Tower, _player.RouteRoot, _opponent.RouteRoot,
            _player.Route[0], _player.Route[1], _opponent.Route[0], _opponent.Route[1],
            _player.Actor, _opponent.Actor, _shop };

        private Transform[] AllTransforms()
        {
            var result = new List<Transform>(UnmanagedTransforms()) { _camera.transform,
                _player.Board.transform, _opponent.Board.transform, _player.Ball.transform, _opponent.Ball.transform };
            return result.ToArray();
        }

        private static List<Pose> Capture(IEnumerable<Transform> transforms)
        {
            var result = new List<Pose>();
            foreach (var target in transforms) result.Add(new Pose(target));
            return result;
        }

        private static void AssertStates(IEnumerable<Pose> snapshots)
        {
            foreach (var snapshot in snapshots) snapshot.AssertUnchanged();
        }

        private sealed class Pose
        {
            private readonly Transform _target, _parent;
            private readonly Vector3 _localPosition, _worldPosition, _localScale, _worldScale;
            private readonly Quaternion _localRotation, _worldRotation;

            public Pose(Transform target)
            {
                _target = target;
                _parent = target.parent;
                _localPosition = target.localPosition;
                _worldPosition = target.position;
                _localScale = target.localScale;
                _worldScale = target.lossyScale;
                _localRotation = target.localRotation;
                _worldRotation = target.rotation;
            }

            public void AssertPositionRotationParent()
            {
                Assert.That(_target.parent, Is.SameAs(_parent), _target.name + " 不应重新挂接父节点");
                AssertVector(_localPosition, _target.localPosition);
                AssertVector(_worldPosition, _target.position);
                Assert.That(Quaternion.Angle(_localRotation, _target.localRotation), Is.LessThan(0.01f));
                Assert.That(Quaternion.Angle(_worldRotation, _target.rotation), Is.LessThan(0.01f));
            }

            public void AssertUnchanged()
            {
                AssertPositionRotationParent();
                AssertVector(_localScale, _target.localScale);
                AssertVector(_worldScale, _target.lossyScale);
            }
        }

        private sealed class BallSettings
        {
            private readonly PhysicalCollisionBall _ball;
            private readonly Dictionary<string, object> _settings = new Dictionary<string, object>();
            private readonly Func<int, float> _provider;
            private readonly float _speed;

            public BallSettings(PhysicalCollisionBall ball)
            {
                _ball = ball;
                _speed = ball.Speed;
                _provider = ball.GlobalValueProvider;
                foreach (var field in new[] { "speed", "radius", "initialAngleMin", "initialAngleMax", "spawnPosition", "launchOnEnable", "globalConfig" })
                    _settings.Add(field, Field(ball, field).GetValue(ball));
            }

            public void AssertUnchanged()
            {
                Assert.That(_ball.Speed, Is.EqualTo(_speed));
                Assert.That(_ball.GlobalValueProvider, Is.SameAs(_provider));
                foreach (var entry in _settings)
                    Assert.That(Field(_ball, entry.Key).GetValue(_ball), Is.EqualTo(entry.Value), entry.Key);
            }
        }

        private static void SetSize(PlacementBoardView board, int columns, int rows, float cellSize)
        {
            SetField(board, "_columns", columns);
            SetField(board, "_rows", rows);
            SetField(board, "_cellSize", cellSize);
        }
        private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
        private static FieldInfo Field(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "测试依赖字段：" + name);
            return field;
        }
        private static Vector3 ScaleXy(Vector3 scale, float factor) => new Vector3(scale.x * factor, scale.y * factor, scale.z);
        private static void AssertVector(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Epsilon), expected + " != " + actual);
        private static void AssertScale(Side side, float fit, float cell)
        {
            AssertVector(ScaleXy(side.BoardScale, fit), side.Board.transform.localScale);
            AssertVector(ScaleXy(side.BallScale, cell), side.Ball.transform.localScale);
            Assert.That(side.Ball.MapScale, Is.EqualTo(cell).Within(Epsilon));
            Assert.That(side.Ball.Speed, Is.EqualTo(side.Speed));
            Assert.That(side.Ball.EffectiveSpeed, Is.EqualTo(side.Speed * cell).Within(Epsilon));
            Assert.That(side.Board.ReferenceCellSize, Is.EqualTo(ReferenceCellSize));
        }
        private Side CreateSide(bool player)
        {
            var root = Child(player ? "PlayerBoardWorldRoot" : "EnemyBoardWorldRoot", _root.transform);
            root.localPosition = player ? new Vector3(-3f, -4f, 0.2f) : new Vector3(4f, 5f, -0.3f);
            root.localRotation = Quaternion.Euler(0f, 0f, player ? 7f : -11f);
            root.localScale = player ? new Vector3(1.25f, 1.25f, 1.1f) : new Vector3(0.8f, 0.8f, 1.3f);
            var board = Child("Board", root).gameObject.AddComponent<PlacementBoardView>();
            board.transform.localPosition = new Vector3(0.7f, 0.2f, 0.1f);
            board.transform.localRotation = Quaternion.Euler(0f, 0f, player ? 13f : -17f);
            board.transform.localScale = player ? new Vector3(1.2f, 1.2f, 0.9f) : new Vector3(0.7f, 0.7f, 1.6f);
            SetSize(board, 11, 9, ReferenceCellSize);
            var ballObject = Child("EnergyBall", root).gameObject;
            ballObject.transform.localPosition = new Vector3(-0.4f, 0.6f, -0.1f);
            ballObject.transform.localRotation = Quaternion.Euler(0f, 0f, 23f);
            ballObject.transform.localScale = player ? new Vector3(0.4f, 0.65f, 0.7f) : new Vector3(0.3f, 0.45f, 1.4f);
            var speed = player ? 5f : 7.25f;
            var ball = AddBall(ballObject, speed);
            var background = Decoration(player ? "Battle_playerBG" : "Battle_enemyBG", root, new Vector3(0.4f, 0.8f, 2f));
            background.localScale = new Vector3(6f, 5f, 1f);
            var belt = Decoration(player ? "Battle_playerBelt" : "Battle_enemyBelt", root, new Vector3(0.1f, -0.3f, 1f));
            belt.localScale = new Vector3(6.5f, 5.5f, 1f);
            var tower = Decoration("Tower", player ? root : _root.transform, new Vector3(2.6f, 2.4f, 0f));
            tower.gameObject.AddComponent<TowerView>();
            var routeRoot = Child("Route", player ? root : _root.transform);
            var route = new[] { Child("P0", routeRoot), Child("P1", routeRoot) };
            route[0].localPosition = new Vector3(-3.8f, 2.2f, 0f);
            route[1].localPosition = new Vector3(-3.8f, -2.2f, 0f);
            route[0].localScale = new Vector3(0.7f, 0.9f, 1f);
            var actor = Decoration("RobotSprite", root, new Vector3(1f, 1f, 0f));
            actor.localScale = new Vector3(0.37f, 0.42f, 1f);
            return new Side { Root = root, Board = board, Ball = ball, Speed = speed,
                BoardScale = board.transform.localScale, WorldScale = board.transform.lossyScale,
                BallScale = ball.transform.localScale, Background = background, Belt = belt,
                Tower = tower, RouteRoot = routeRoot, Route = route, Actor = actor };
        }
        private static PhysicalCollisionBall AddBall(GameObject target, float speed)
        {
            // AddComponent 前停用，避免默认自动发射读取配置表。
            target.SetActive(false);
            var ball = target.AddComponent<PhysicalCollisionBall>();
            SetField(ball, "launchOnEnable", false);
            SetField(ball, "speed", speed);
            ball.GlobalValueProvider = GlobalValue;
            target.SetActive(true);
            ball.GlobalValueProvider = GlobalValue; // Awake 可能重置 provider。
            return ball;
        }
        private static float GlobalValue(int key) => key == 17 ? 0.25f : 0f;
        private static Transform Decoration(string name, Transform parent, Vector3 position)
        {
            var target = Child(name, parent);
            target.localPosition = position;
            target.localRotation = Quaternion.Euler(0f, 0f, 19f);
            target.gameObject.AddComponent<SpriteRenderer>();
            return target;
        }
        private static Transform Child(string name, Transform parent)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
        private sealed class Side
        {
            public Transform Root, Background, Belt, Tower, RouteRoot, Actor;
            public Transform[] Route;
            public PlacementBoardView Board;
            public PhysicalCollisionBall Ball;
            public Vector3 BoardScale, WorldScale, BallScale;
            public float Speed;
        }
    }
}
