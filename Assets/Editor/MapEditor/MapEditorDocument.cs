using System;
using System.Collections.Generic;
using GameLogic;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>仅驻留编辑器内存的文档；整次手势通过窗口登记一次完整对象Undo。</summary>
    public sealed class MapEditorDocument : ScriptableObject
    {
        [SerializeField] private MapData _data;

        public MapData Data => _data;

        public void Replace(MapData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            _data = data.Clone();
        }

        public bool Paint(BoardCoordinate coordinate, TerrainType terrain)
        {
            if (!_data.IsInside(coordinate) || _data.GetTerrain(coordinate) == terrain) return false;
            _data.SetTerrain(coordinate, terrain);
            return true;
        }

        public void PaintLine(BoardCoordinate start, BoardCoordinate end, TerrainType terrain)
        {
            foreach (var coordinate in RasterizeLine(start, end)) Paint(coordinate, terrain);
        }

        public void FillRectangle(BoardCoordinate start, BoardCoordinate end, TerrainType terrain)
        {
            var left = Math.Max(0, Math.Min(start.Column, end.Column));
            var right = Math.Min(_data.Columns - 1, Math.Max(start.Column, end.Column));
            var bottom = Math.Max(0, Math.Min(start.Row, end.Row));
            var top = Math.Min(_data.Rows - 1, Math.Max(start.Row, end.Row));
            for (var row = bottom; row <= top; row++)
                for (var column = left; column <= right; column++)
                    Paint(new BoardCoordinate(column, row), terrain);
        }

        public void Fill(TerrainType terrain)
        {
            FillRectangle(new BoardCoordinate(0, 0), new BoardCoordinate(_data.Columns - 1, _data.Rows - 1), terrain);
        }

        public void Resize(int columns, int rows)
        {
            _data = _data.Resize(columns, rows);
        }

        /// <summary>整数Bresenham，包含两端，覆盖快速拖动时跳过的格子。</summary>
        public static IEnumerable<BoardCoordinate> RasterizeLine(BoardCoordinate start, BoardCoordinate end)
        {
            var column = start.Column;
            var row = start.Row;
            var dx = Math.Abs(end.Column - column);
            var dy = -Math.Abs(end.Row - row);
            var stepX = column < end.Column ? 1 : -1;
            var stepY = row < end.Row ? 1 : -1;
            var error = dx + dy;
            while (true)
            {
                yield return new BoardCoordinate(column, row);
                if (column == end.Column && row == end.Row) yield break;
                var twiceError = 2 * error;
                if (twiceError >= dy) { error += dy; column += stepX; }
                if (twiceError <= dx) { error += dx; row += stepY; }
            }
        }

        public static bool ContentEquals(MapData left, MapData right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Version != right.Version || left.Rows != right.Rows ||
                left.Columns != right.Columns || !left.CellSize.Equals(right.CellSize) ||
                left.NormalGlassId != right.NormalGlassId || left.ColoredGlassId != right.ColoredGlassId)
                return false;
            if (ReferenceEquals(left.Terrains, right.Terrains)) return true;
            if (left.Terrains == null || right.Terrains == null || left.Terrains.Length != right.Terrains.Length) return false;
            for (var index = 0; index < left.Terrains.Length; index++)
                if (left.Terrains[index] != right.Terrains[index]) return false;
            return true;
        }
    }
}
