using UnityEngine;

namespace GameLogic
{
    public static class MapGridMath
    {
        public const float REFERENCE_CELL_SIZE = 0.35f;
        public static float GetFitScale(int columns, int rows, float cellSize, float referenceCellSize = REFERENCE_CELL_SIZE)
        {
            if (columns < 1 || columns > MapData.MAX_DIMENSION || rows < 1 || rows > MapData.MAX_DIMENSION ||
                float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f ||
                float.IsNaN(referenceCellSize) || float.IsInfinity(referenceCellSize) || referenceCellSize <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(cellSize), "网格适配参数无效。");
            var scale = Mathf.Min(11f / columns, 9f / rows) * (referenceCellSize / cellSize);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(cellSize), "网格缩放超出有效数值范围。");
            return scale;
        }
        public static Vector3 GetLocalPosition(BoardCoordinate cell, int columns, int rows, float cellSize) =>
            new Vector3((cell.Column + 0.5f - columns * 0.5f) * cellSize, (cell.Row + 0.5f - rows * 0.5f) * cellSize, 0f);
        public static BoardCoordinate GetCoordinate(Vector3 localPoint, int columns, int rows, float cellSize) =>
            new BoardCoordinate(Mathf.FloorToInt(localPoint.x / cellSize + columns * 0.5f),
                Mathf.FloorToInt(localPoint.y / cellSize + rows * 0.5f));

        // Snap the footprint's bounding-box center, including half-cell centers.
        // Floor(value + .5) consistently resolves exact ties toward the larger coordinate.
        public static BoardCoordinate GetCenteredAnchor(Vector3 localPoint, int columns, int rows,
            float cellSize, Vector2 footprintCenter) =>
            new BoardCoordinate(Mathf.FloorToInt(localPoint.x / cellSize + columns * 0.5f - footprintCenter.x),
                Mathf.FloorToInt(localPoint.y / cellSize + rows * 0.5f - footprintCenter.y));
    }
}
