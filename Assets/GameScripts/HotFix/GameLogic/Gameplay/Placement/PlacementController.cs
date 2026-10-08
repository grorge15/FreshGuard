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
        private bool _ready;
        private bool _dragging;
        private PlaceableDefinition _definition;
        private BoardCoordinate _grabbedCell;
        private PlacementBoardView _previewBoard;
        private BoardCoordinate _anchor;

        public bool IsReady => _ready && isActiveAndEnabled &&
            _playerBoard != null && _enemyBoard != null && _playerBoard.isActiveAndEnabled && _enemyBoard.isActiveAndEnabled &&
            _playerBoard.IsInitialized && _enemyBoard.IsInitialized;
        public bool IsDragging => _dragging;

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
            foreach (var cell in definition.Cells)
                if (cell.Equals(grabbedCell)) { containsGrabbedCell = true; break; }
            if (!containsGrabbedCell) return false;
            _definition = definition;
            _grabbedCell = grabbedCell;
            _dragging = true;
            return true;
        }

        public void UpdateDrag(Vector2 screenPosition)
        {
            if (!_dragging) return;
            if (!IsReady) { CancelDrag(); return; }
            ClearPreview();
            if (_sceneCamera == null || float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) ||
                float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y)) return;
            var ray = _sceneCamera.ScreenPointToRay(screenPosition);
            float distance;
            if (!new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out distance)) return;
            var point = ray.GetPoint(distance);
            BoardCoordinate coordinate;
            if (_playerBoard.TryGetCoordinate(point, out coordinate)) _previewBoard = _playerBoard;
            else if (_enemyBoard.TryGetCoordinate(point, out coordinate)) _previewBoard = _enemyBoard;
            if (_previewBoard == null) return;
            _anchor = new BoardCoordinate(coordinate.Column - _grabbedCell.Column, coordinate.Row - _grabbedCell.Row);
            _previewBoard.ShowPreview(_previewBoard.Model.Evaluate(_anchor, _definition));
        }

        public bool TryCommit(out BoardPlacementTarget target)
        {
            target = default(BoardPlacementTarget);
            if (!_dragging || !IsReady || _previewBoard == null) { CancelDrag(); return false; }
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
