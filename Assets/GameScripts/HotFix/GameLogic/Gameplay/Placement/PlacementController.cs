using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public struct BoardPlacementTarget
    {
        public PlacementBoardView Board { get; }
        public BoardCoordinate Anchor { get; }
        public Transform EntityParent { get; }
        // Robot prefab's logical (0,0) cell center must align to this position.
        public Vector3 AnchorWorldPosition { get; }

        public BoardPlacementTarget(PlacementBoardView board, BoardCoordinate anchor)
        {
            Board = board;
            Anchor = anchor;
            EntityParent = board.transform;
            AnchorWorldPosition = board.GetCellWorldPosition(anchor);
        }
    }

    /// <summary>由机器人拖拽方显式调用；不读取输入、不创建机器人。</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementController : MonoBehaviour
    {
        [SerializeField] private PlacementBoardView _playerBoard;
        [SerializeField] private PlacementBoardView _enemyBoard;
        [SerializeField] private Camera _sceneCamera;
        [SerializeField, Min(0f), Tooltip("机器人形状中心与拖拽目标之间的最大吸附距离，单位为棋盘格。")]
        private float _maxSnapDistanceCells = 2.5f;
        private readonly List<BoardCoordinate> _snapAnchorOffsets = new List<BoardCoordinate>();
        private bool _ready;
        private bool _dragging;
        private PlaceableDefinition _definition;
        private Vector2 _footprintCenter;
        private PlacementBoardView _previewBoard;
        private bool _previewIsValid;
        private BoardCoordinate _anchor;
        private BoardCoordinate _pointerCell;
        private BattleSide? _requiredSide;
        private Rect? _screenInputRect;
        private Vector2Int _inputScreenSize;
        private Rect _inputSafeArea;
        private Camera _playerViewCamera;
        private Camera _opponentViewCamera;
        private Rect _playerScreenRect;
        private Rect _opponentScreenRect;
        private bool _hasScreenViews;
        private bool _screenInputBlocked;

        public bool IsReady => _ready && isActiveAndEnabled && IsScreenInputCurrent &&
            _playerBoard != null && _enemyBoard != null && _playerBoard.isActiveAndEnabled && _enemyBoard.isActiveAndEnabled &&
            _playerBoard.IsInitialized && _enemyBoard.IsInitialized;
        public bool IsDragging => _dragging;
        public Camera SceneCamera => _sceneCamera;

        // 尺寸变化后的 Update 可能早于布局 LateUpdate；此期间拒绝旧相机映射的输入。
        private bool IsScreenInputCurrent => !_screenInputBlocked && (!_screenInputRect.HasValue ||
            (_inputScreenSize == new Vector2Int(Screen.width, Screen.height) && _inputSafeArea == Screen.safeArea));

        public Camera GetSceneCamera(BattleSide side) => !_hasScreenViews ? _sceneCamera :
            side == BattleSide.Player ? _playerViewCamera : side == BattleSide.Opponent ? _opponentViewCamera : null;

        public float GetScreenCellHeight(BattleSide side)
        {
            var board = side == BattleSide.Player ? _playerBoard : side == BattleSide.Opponent ? _enemyBoard : null;
            var camera = GetSceneCamera(side);
            if (board == null || camera == null) return 0f;
            var bottom = camera.WorldToScreenPoint(board.transform.position);
            var top = camera.WorldToScreenPoint(board.transform.TransformPoint(Vector3.up * board.CellSize));
            return Mathf.Abs(top.y - bottom.y);
        }

        public void SetScreenViews(Camera playerCamera, Rect playerRect, Camera opponentCamera, Rect opponentRect)
        {
            SetScreenInputRect(Rect.MinMaxRect(Mathf.Min(playerRect.xMin, opponentRect.xMin), playerRect.yMin,
                Mathf.Max(playerRect.xMax, opponentRect.xMax), opponentRect.yMax));
            _playerViewCamera = playerCamera;
            _opponentViewCamera = opponentCamera;
            _playerScreenRect = playerRect;
            _opponentScreenRect = opponentRect;
            _hasScreenViews = true;
        }

        public void InvalidateScreenViews() { _screenInputBlocked = true; CancelDrag(); }
        public void ClearScreenViews()
        {
            _hasScreenViews = false;
            _screenInputBlocked = false;
            _playerViewCamera = _opponentViewCamera = null;
            _screenInputRect = null;
        }

        public void SetScreenInputRect(Rect screenRect)
        {
            _screenInputRect = screenRect;
            _screenInputBlocked = false;
            _inputScreenSize = new Vector2Int(Screen.width, Screen.height);
            _inputSafeArea = Screen.safeArea;
        }
        public void ClearScreenInputRect() { ClearScreenViews(); }

        public bool Initialize()
        {
            CancelDrag();
            _ready = false;
            if (_playerBoard == null || _enemyBoard == null || _playerBoard == _enemyBoard || _sceneCamera == null ||
                !_playerBoard.isActiveAndEnabled || !_enemyBoard.isActiveAndEnabled)
            {
                DisableBoundaries();
                Debug.LogError("摆放控制器需要两个启用的独立棋盘及场景相机。", this);
                return false;
            }
            var playerReady = _playerBoard.Initialize();
            var enemyReady = _enemyBoard.Initialize();
            if (!playerReady || !enemyReady)
            {
                DisableBoundaries();
                return false;
            }
            _ready = true;
            return true;
        }

        public bool BeginDrag(PlaceableDefinition definition, BoardCoordinate grabbedCell)
        {
            CancelDrag();
            if (!IsReady || definition == null) return false;
            var containsGrabbedCell = false;
            var minColumn = int.MaxValue;
            var minRow = int.MaxValue;
            var maxColumn = int.MinValue;
            var maxRow = int.MinValue;
            foreach (var cell in definition.Cells)
            {
                if (cell.Equals(grabbedCell)) containsGrabbedCell = true;
                minColumn = Mathf.Min(minColumn, cell.Column);
                minRow = Mathf.Min(minRow, cell.Row);
                maxColumn = Mathf.Max(maxColumn, cell.Column);
                maxRow = Mathf.Max(maxRow, cell.Row);
            }
            if (!containsGrabbedCell) return false;
            _definition = definition;
            _footprintCenter = new Vector2((minColumn + maxColumn) * 0.5f, (minRow + maxRow) * 0.5f);
            BuildSnapAnchorOffsets(definition);
            _dragging = true;
            return true;
        }

        private void BuildSnapAnchorOffsets(PlaceableDefinition definition)
        {
            _snapAnchorOffsets.Clear();
            var candidates = new HashSet<BoardCoordinate>();
            var footprint = new HashSet<BoardCoordinate>(definition.Cells);
            // 保留原八格，即使某个候选锚点位于当前形状内部，也允许整体小幅挪动。
            for (var column = -1; column <= 1; column++)
                for (var row = -1; row <= 1; row++)
                    AddSnapAnchorOffset(new BoardCoordinate(column, row), candidates);

            // 每个实际占格向外扩一圈，只排除本形状占格；凹口和空洞仍可产生候选。
            foreach (var cell in definition.Cells)
                for (var column = -1; column <= 1; column++)
                    for (var row = -1; row <= 1; row++)
                    {
                        var offset = new BoardCoordinate(cell.Column + column, cell.Row + row);
                        if (!footprint.Contains(offset)) AddSnapAnchorOffset(offset, candidates);
                    }
        }

        private void AddSnapAnchorOffset(BoardCoordinate offset, HashSet<BoardCoordinate> candidates)
        {
            if ((offset.Column != 0 || offset.Row != 0) && candidates.Add(offset)) _snapAnchorOffsets.Add(offset);
        }

        public bool BeginDrag(PlaceableDefinition definition, BoardCoordinate grabbedCell, BattleSide side)
        {
            if (side != BattleSide.Player && side != BattleSide.Opponent) return false;
            if (!BeginDrag(definition, grabbedCell)) return false;
            _requiredSide = side;
            return true;
        }

        // Reads the target without reserving cells; the shop commits after preparing its view.
        public bool TryGetDragTarget(out BoardPlacementTarget target)
        {
            target = default(BoardPlacementTarget);
            if (!_dragging || !IsReady || _previewBoard == null || !IsAllowedBoard(_previewBoard)) return false;
            target = new BoardPlacementTarget(_previewBoard, _anchor);
            return true;
        }

        public BattleSide GetBoardSide(PlacementBoardView board)
        {
            if (board == _playerBoard) return BattleSide.Player;
            if (board == _enemyBoard) return BattleSide.Opponent;
            throw new System.ArgumentException("棋盘不属于当前摆放控制器。", nameof(board));
        }

        public bool TryGetPointerCell(Vector2 position, out PlacementBoardView board, out BoardCoordinate coordinate)
        {
            board = null;
            coordinate = default(BoardCoordinate);
            Vector3 point;
            if (!TryGetPointerWorldPosition(position, out point)) return false;
            if (_hasScreenViews)
            {
                board = _playerScreenRect.Contains(position) ? _playerBoard : _enemyBoard;
                if (board.TryGetCoordinate(point, out coordinate)) return true;
                board = null;
                return false;
            }
            if (_playerBoard.TryGetCoordinate(point, out coordinate)) board = _playerBoard;
            else if (_enemyBoard.TryGetCoordinate(point, out coordinate)) board = _enemyBoard;
            return board != null;
        }

        public bool TryGetPointerWorldPosition(Vector2 position, out Vector3 worldPosition)
        {
            worldPosition = default(Vector3);
            if (!IsReady || _sceneCamera == null || float.IsNaN(position.x) || float.IsNaN(position.y) ||
                float.IsInfinity(position.x) || float.IsInfinity(position.y)) return false;
            if (_screenInputRect.HasValue && !_screenInputRect.Value.Contains(position)) return false;
            var camera = _sceneCamera;
            if (_hasScreenViews)
            {
                if (_playerScreenRect.Contains(position)) camera = _playerViewCamera;
                else if (_opponentScreenRect.Contains(position)) camera = _opponentViewCamera;
                else return false;
            }
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(position);
            float distance;
            if (!new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out distance)) return false;
            worldPosition = ray.GetPoint(distance);
            return IsFinite(worldPosition);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        public bool TryGetDragOccupant(out int instanceId)
        {
            instanceId = 0;
            return _dragging && _previewBoard != null && IsAllowedBoard(_previewBoard) &&
                int.TryParse(_previewBoard.Model.GetOccupant(_pointerCell), out instanceId);
        }

        public void ClearPlacementPreview()
        {
            _previewIsValid = false;
            if (_previewBoard != null) _previewBoard.ClearPreview();
        }

        public bool TryGetPreviewScreenCenter(out Vector2 center)
        {
            center = default(Vector2);
            if (!_dragging || !IsReady || !_previewIsValid || _previewBoard == null) return false;
            var camera = GetSceneCamera(GetBoardSide(_previewBoard));
            if (camera == null) return false;
            var local = _previewBoard.GetCellLocalPosition(_anchor) + (Vector3)(_footprintCenter * _previewBoard.CellSize);
            center = camera.WorldToScreenPoint(_previewBoard.transform.TransformPoint(local));
            return true;
        }

        private bool IsAllowedBoard(PlacementBoardView board)
        {
            return !_requiredSide.HasValue ||
                (_requiredSide.Value == BattleSide.Player ? board == _playerBoard : board == _enemyBoard);
        }

        public void UpdateDrag(Vector2 screenPosition)
        {
            if (!_dragging) return;
            if (!IsReady) { CancelDrag(); return; }
            ClearPreview();
            Vector3 point;
            if (!TryGetPointerWorldPosition(screenPosition, out point)) return;
            BoardCoordinate coordinate;
            if (!TryGetPointerCell(screenPosition, out _previewBoard, out coordinate)) return;
            _pointerCell = coordinate;
            var localPoint = _previewBoard.transform.InverseTransformPoint(point);
            _anchor = MapGridMath.GetCenteredAnchor(localPoint,
                _previewBoard.Columns, _previewBoard.Rows, _previewBoard.CellSize, _footprintCenter);
            var preview = _previewBoard.Model.Evaluate(_anchor, _definition);
            if (!IsAllowedBoard(_previewBoard)) preview = new BoardPlacementResult(false, preview.Cells);
            else if (!preview.IsValid) preview = FindNearbyPlacement(localPoint, preview);
            _previewIsValid = preview.IsValid;
            _previewBoard.ShowPreview(preview);
        }

        private BoardPlacementResult FindNearbyPlacement(Vector3 localPoint, BoardPlacementResult fallback)
        {
            if (!(_maxSnapDistanceCells > 0f)) return fallback;
            var original = _anchor;
            var nearestDistance = float.PositiveInfinity;
            var maxDistance = _maxSnapDistanceCells * _previewBoard.CellSize;
            var maxDistanceSquared = maxDistance * maxDistance;
            var best = fallback;
            foreach (var offset in _snapAnchorOffsets)
            {
                var column = (long)original.Column + offset.Column;
                var row = (long)original.Row + offset.Row;
                if (column < int.MinValue || column > int.MaxValue || row < int.MinValue || row > int.MaxValue) continue;
                var candidate = new BoardCoordinate((int)column, (int)row);
                var center = _previewBoard.GetCellLocalPosition(candidate) + (Vector3)(_footprintCenter * _previewBoard.CellSize);
                var distance = ((Vector2)(center - localPoint)).sqrMagnitude;
                if (distance > maxDistanceSquared || distance >= nearestDistance) continue;
                var result = _previewBoard.Model.Evaluate(candidate, _definition);
                if (!result.IsValid) continue;
                nearestDistance = distance;
                _anchor = candidate;
                best = result;
            }
            return best;
        }

        public bool TryCommit(out BoardPlacementTarget target)
        {
            target = default(BoardPlacementTarget);
            if (!_dragging || !IsReady || _previewBoard == null || !IsAllowedBoard(_previewBoard)) { CancelDrag(); return false; }
            var board = _previewBoard;
            var anchor = _anchor;
            var result = board.Model.Evaluate(anchor, _definition);
            if (!result.IsValid || !board.Model.TryPlace(anchor, _definition)) { CancelDrag(); return false; }
            target = new BoardPlacementTarget(board, anchor);
            CancelDrag();
            board.RefreshOccupied(result.Cells);
            return true;
        }

        public void CancelDrag()
        {
            ClearPreview();
            _dragging = false;
            _definition = null;
            _snapAnchorOffsets.Clear();
            _requiredSide = null;
        }

        public void SuspendPlacement()
        {
            _ready = false;
            CancelDrag();
            DisableBoundaries();
        }

        public void SetSlotHighlighted(bool player, int column, int row, bool highlighted)
        {
            var board = player ? _playerBoard : _enemyBoard;
            if (board != null) board.SetHighlighted(new BoardCoordinate(column, row), highlighted);
        }

        private void ClearPreview()
        {
            _previewIsValid = false;
            if (_previewBoard != null) _previewBoard.ClearPreview();
            _previewBoard = null;
        }

        private void DisableBoundaries()
        {
            if (_playerBoard != null) _playerBoard.DisableBoundary();
            if (_enemyBoard != null) _enemyBoard.DisableBoundary();
        }

        private void OnDisable() { CancelDrag(); }
        private void OnDestroy() { CancelDrag(); }
    }
}
