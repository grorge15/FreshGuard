using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    public enum BoardSlotState { Normal, Occupied, Valid, Invalid, Highlighted }

    [DisallowMultipleComponent]
    public sealed class PlacementBoardView : MonoBehaviour
    {
        [SerializeField] private int _columns = 11;
        [SerializeField] private int _rows = 9;
        [SerializeField] private float _cellSize = 0.35f;
        [SerializeField] private float _wallThickness = 0.25f;
        [SerializeField] private SpriteRenderer _slotPrefab;
        [SerializeField] private PhysicalCollisionBoundary _boundary;
        private SpriteRenderer[,] _slots;
        private BoardModel _model;
        private bool _initialized;
        private readonly Dictionary<BoardCoordinate, BoardSlotState> _previewStates = new Dictionary<BoardCoordinate, BoardSlotState>();

        private static readonly Color NormalColor = new Color(0.11f, 0.15f, 0.20f, 0.94f);
        private static readonly Color OccupiedColor = new Color(0.30f, 0.45f, 0.65f, 0.98f);
        private static readonly Color ValidColor = new Color(0.20f, 0.80f, 0.35f, 0.98f);
        private static readonly Color InvalidColor = new Color(0.85f, 0.22f, 0.22f, 0.98f);
        private static readonly Color HighlightedColor = new Color(1f, 0.72f, 0.15f, 0.98f);

        public BoardModel Model => _model;
        public bool IsInitialized => _initialized;
        public Vector2 Size => new Vector2(_columns * _cellSize, _rows * _cellSize);
        public PhysicalCollisionBoundary Boundary => _boundary;

        public float CellSize => _cellSize;

        /// <summary>仅在新局、尚无部署实体时重置；同局初始化不恢复破碎格。</summary>
        public bool ResetForBattle(BoardLayoutRules layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            ClearPreview();
            if (_initialized && (_columns != layout.Columns || _rows != layout.Rows || _cellSize != layout.CellSize))
            {
                var root = transform.Find("Slots");
                if (root != null)
                    foreach (Transform child in root) child.gameObject.SetActive(false);
                _initialized = false;
            }
            _columns = layout.Columns;
            _rows = layout.Rows;
            _cellSize = layout.CellSize;
            if (!Initialize()) return false;
            _model = new BoardModel(_columns, _rows, layout.OpenCells, layout.ReservedBorderWidth);
            RefreshAllCells();
            return true;
        }

        public bool Initialize()
        {
            if (_initialized)
            {
                _boundary.SetWallsEnabled(true);
                return true;
            }
            try
            {
                if (_columns <= 0 || _rows <= 0 || float.IsNaN(_cellSize) || float.IsInfinity(_cellSize) || _cellSize <= 0f)
                    throw new InvalidOperationException("棋盘尺寸与格距必须为正数。");
                if (_slotPrefab == null || _slotPrefab.sprite == null)
                    throw new InvalidOperationException("世界 Slot Prefab 或 Sprite 引用缺失。");
                var slotSize = _slotPrefab.sprite.bounds.size;
                if (slotSize.x <= 0f || slotSize.y <= 0f || _slotPrefab.GetComponent<Collider2D>() != null || _slotPrefab.GetComponent<Rigidbody2D>() != null)
                    throw new InvalidOperationException("Slot 必须是无物理组件的有效 Sprite。");

                var slotRoot = transform.Find("Slots");
                if (slotRoot == null)
                {
                    slotRoot = new GameObject("Slots").transform;
                    slotRoot.SetParent(transform, false);
                }
                // The outer ring stays visible and addressable, but cannot hold robots.
                _model = new BoardModel(_columns, _rows, null, reservedBorderWidth: 1);
                _slots = new SpriteRenderer[_columns, _rows];
                for (var column = 0; column < _columns; column++)
                {
                    for (var row = 0; row < _rows; row++)
                    {
                        var slotName = "Slot_" + column + "_" + row;
                        var existing = slotRoot.Find(slotName);
                        // Serialized prefab is already loaded as a scene dependency; no resource IO here.
                        var slot = existing != null ? existing.GetComponent<SpriteRenderer>() : Instantiate(_slotPrefab, slotRoot);
                        if (slot == null) throw new InvalidOperationException("格子缺少 SpriteRenderer：" + slotName);
                        if (slot.GetComponent<Collider2D>() != null || slot.GetComponent<Rigidbody2D>() != null)
                            throw new InvalidOperationException("格子不能包含碰撞或刚体组件：" + slotName);
                        slot.name = slotName;
                        slot.transform.localPosition = GetLocalPosition(new BoardCoordinate(column, row)) + Vector3.forward;
                        slot.transform.localRotation = Quaternion.identity;
                        slot.transform.localScale = new Vector3(_cellSize * 0.94f / slotSize.x, _cellSize * 0.94f / slotSize.y, 1f);
                        slot.sortingLayerName = "Default";
                        slot.sortingOrder = -10;
                        slot.color = NormalColor;
                        slot.gameObject.SetActive(true);
                        _slots[column, row] = slot;
                    }
                }
                var boundaryRoot = transform.Find("Boundary");
                if (boundaryRoot == null)
                {
                    boundaryRoot = new GameObject("Boundary").transform;
                    boundaryRoot.SetParent(transform, false);
                }
                boundaryRoot.localPosition = Vector3.zero;
                boundaryRoot.localRotation = Quaternion.identity;
                boundaryRoot.localScale = Vector3.one;
                _boundary = boundaryRoot.GetComponent<PhysicalCollisionBoundary>();
                if (_boundary == null) _boundary = boundaryRoot.gameObject.AddComponent<PhysicalCollisionBoundary>();
                _boundary.Configure(Size, _wallThickness);
                _initialized = true;
                return true;
            }
            catch (Exception exception)
            {
                _initialized = false;
                DisableBoundary();
                Debug.LogError("棋盘初始化失败，修复后调用 Initialize 重试：" + exception.Message, this);
                return false;
            }
        }

        public void DisableBoundary()
        {
            if (_boundary != null) _boundary.SetWallsEnabled(false);
            var root = transform.Find("Boundary");
            if (root != null)
                foreach (var collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        }

        public bool TryGetCoordinate(Vector3 worldPosition, out BoardCoordinate coordinate)
        {
            coordinate = default(BoardCoordinate);
            if (!_initialized) return false;
            var point = transform.InverseTransformPoint(worldPosition);
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y)) return false;
            coordinate = new BoardCoordinate(Mathf.FloorToInt((point.x + Size.x * 0.5f) / _cellSize),
                Mathf.FloorToInt((point.y + Size.y * 0.5f) / _cellSize));
            return _model.IsInside(coordinate);
        }

        public Vector3 GetCellWorldPosition(BoardCoordinate coordinate)
        {
            return transform.TransformPoint(GetLocalPosition(coordinate));
        }

        public Vector3 GetCellLocalPosition(BoardCoordinate coordinate) => GetLocalPosition(coordinate);

        public void ShowPreview(BoardPlacementResult result)
        {
            ClearPreview();
            if (!_initialized || result == null) return;
            foreach (var coordinate in result.Cells)
            {
                if (!_model.IsInside(coordinate)) continue;
                _previewStates[coordinate] = result.IsValid ? BoardSlotState.Valid : BoardSlotState.Invalid;
                Refresh(coordinate);
            }
        }

        public void ClearPreview()
        {
            foreach (var coordinate in _previewStates.Keys) RefreshBase(coordinate);
            _previewStates.Clear();
        }

        public void RefreshOccupied(IReadOnlyList<BoardCoordinate> cells)
        {
            foreach (var coordinate in cells) Refresh(coordinate);
        }

        public void RefreshAllCells()
        {
            if (!_initialized) return;
            for (var column = 0; column < _slots.GetLength(0); column++)
                for (var row = 0; row < _slots.GetLength(1); row++)
                    Refresh(new BoardCoordinate(column, row));
        }

        public void SetHighlighted(BoardCoordinate coordinate, bool highlighted)
        {
            if (!_initialized) return;
            _model.SetHighlighted(coordinate, highlighted);
            Refresh(coordinate);
        }

        private Vector3 GetLocalPosition(BoardCoordinate coordinate)
        {
            return new Vector3((coordinate.Column + 0.5f) * _cellSize - Size.x * 0.5f,
                (coordinate.Row + 0.5f) * _cellSize - Size.y * 0.5f, 0f);
        }

        private void Refresh(BoardCoordinate coordinate)
        {
            if (!_initialized || !_model.IsInside(coordinate)) return;
            BoardSlotState preview;
            if (_previewStates.TryGetValue(coordinate, out preview))
                _slots[coordinate.Column, coordinate.Row].color = preview == BoardSlotState.Valid ? ValidColor : InvalidColor;
            else RefreshBase(coordinate);
        }

        private void RefreshBase(BoardCoordinate coordinate)
        {
            if (!_initialized || !_model.IsInside(coordinate)) return;
            _slots[coordinate.Column, coordinate.Row].color = _model.IsHighlighted(coordinate) ? HighlightedColor :
                _model.IsOccupied(coordinate) ? OccupiedColor : NormalColor;
        }

        private void OnDisable() { ClearPreview(); }
    }
}
