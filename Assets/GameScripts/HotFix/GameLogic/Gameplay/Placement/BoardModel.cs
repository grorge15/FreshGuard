using System;
using System.Collections.Generic;

namespace GameLogic
{
    public struct BoardCoordinate : IEquatable<BoardCoordinate>
    {
        public int Column { get; }
        public int Row { get; }

        public BoardCoordinate(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public bool Equals(BoardCoordinate other)
        {
            return Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is BoardCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Column * 397) ^ Row;
        }
    }

    public sealed class PlaceableDefinition
    {
        public string Id { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<BoardCoordinate> Cells { get; }

        public PlaceableDefinition(string id, int width, int height)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("An id is required.", nameof(id));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Id = id;
            Width = width;
            Height = height;
            var cells = new List<BoardCoordinate>();
            for (var column = 0; column < width; column++)
                for (var row = 0; row < height; row++)
                    cells.Add(new BoardCoordinate(column, row));
            Cells = cells.AsReadOnly();
        }

        public PlaceableDefinition(string id, IReadOnlyList<BoardCoordinate> cells)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("An id is required.", nameof(id));
            if (cells == null || cells.Count == 0) throw new ArgumentException("A footprint is required.", nameof(cells));
            var copy = new List<BoardCoordinate>(cells.Count);
            var unique = new HashSet<BoardCoordinate>();
            var width = 0;
            var height = 0;
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (cell.Column < 0 || cell.Row < 0 || cell.Column == int.MaxValue || cell.Row == int.MaxValue || !unique.Add(cell))
                    throw new ArgumentException("Footprint cells must be non-negative and unique.", nameof(cells));
                copy.Add(cell);
                width = Math.Max(width, cell.Column + 1);
                height = Math.Max(height, cell.Row + 1);
            }
            Id = id;
            Width = width;
            Height = height;
            Cells = copy.AsReadOnly();
        }
    }

    public sealed class BoardPlacementResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<BoardCoordinate> Cells { get; }

        public BoardPlacementResult(bool isValid, IReadOnlyList<BoardCoordinate> cells)
        {
            IsValid = isValid;
            Cells = cells;
        }
    }

    public sealed class BoardModel
    {
        private readonly string[,] _occupants;
        private readonly bool[,] _open;
        private readonly int _reservedBorderWidth;
        private readonly HashSet<BoardCoordinate> _highlighted = new HashSet<BoardCoordinate>();

        public int Columns { get; }
        public int Rows { get; }

        public BoardModel(int columns, int rows)
            : this(columns, rows, null)
        {
        }

        /// <summary>null表示开放非保留格；传空集合表示全部关闭。保留外圈不可重新开放。</summary>
        public BoardModel(int columns, int rows, IReadOnlyCollection<BoardCoordinate> openCells, int reservedBorderWidth = 0)
        {
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (reservedBorderWidth < 0) throw new ArgumentOutOfRangeException(nameof(reservedBorderWidth));

            Columns = columns;
            Rows = rows;
            _reservedBorderWidth = reservedBorderWidth;
            _occupants = new string[columns, rows];
            _open = new bool[columns, rows];
            if (openCells == null)
            {
                for (var column = 0; column < columns; column++)
                    for (var row = 0; row < rows; row++) _open[column, row] = true;
            }
            else
            {
                foreach (var coordinate in openCells)
                {
                    if (!IsInside(coordinate)) throw new ArgumentOutOfRangeException(nameof(openCells));
                    _open[coordinate.Column, coordinate.Row] = true;
                }
            }
        }

        public bool IsOpen(BoardCoordinate coordinate)
        {
            return IsInsidePlacementArea(coordinate) && _open[coordinate.Column, coordinate.Row];
        }

        private bool IsInsidePlacementArea(BoardCoordinate coordinate)
        {
            return coordinate.Column >= _reservedBorderWidth && coordinate.Column < Columns - _reservedBorderWidth &&
                coordinate.Row >= _reservedBorderWidth && coordinate.Row < Rows - _reservedBorderWidth;
        }

        /// <summary>占用中的格子不能关闭；高亮状态不改变开放状态。</summary>
        public bool SetOpen(BoardCoordinate coordinate, bool open)
        {
            if (!IsInside(coordinate) || (open && !IsInsidePlacementArea(coordinate)) || (!open && IsOccupied(coordinate))) return false;
            _open[coordinate.Column, coordinate.Row] = open;
            return true;
        }

        public bool Release(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var released = false;
            for (var column = 0; column < Columns; column++)
                for (var row = 0; row < Rows; row++)
                    if (_occupants[column, row] == id)
                    {
                        _occupants[column, row] = null;
                        released = true;
                    }
            return released;
        }

        public bool IsInside(BoardCoordinate coordinate)
        {
            return coordinate.Column >= 0 && coordinate.Column < Columns && coordinate.Row >= 0 && coordinate.Row < Rows;
        }

        public bool IsOccupied(BoardCoordinate coordinate)
        {
            return IsInside(coordinate) && !string.IsNullOrEmpty(_occupants[coordinate.Column, coordinate.Row]);
        }

        public string GetOccupant(BoardCoordinate coordinate)
        {
            return IsInside(coordinate) ? _occupants[coordinate.Column, coordinate.Row] : null;
        }

        public BoardPlacementResult Evaluate(BoardCoordinate anchor, PlaceableDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var cells = new List<BoardCoordinate>(definition.Cells.Count);
            var isValid = true;
            for (var index = 0; index < definition.Cells.Count; index++)
            {
                var offset = definition.Cells[index];
                var column = (long)anchor.Column + offset.Column;
                var row = (long)anchor.Row + offset.Row;
                if (column < 0 || column >= Columns || row < 0 || row >= Rows)
                {
                    isValid = false;
                    continue;
                }
                var coordinate = new BoardCoordinate((int)column, (int)row);
                cells.Add(coordinate);
                if (!IsOpen(coordinate) || IsOccupied(coordinate)) isValid = false;
            }

            return new BoardPlacementResult(isValid, cells);
        }

        public bool TryPlace(BoardCoordinate anchor, PlaceableDefinition definition)
        {
            var result = Evaluate(anchor, definition);
            if (!result.IsValid) return false;

            Occupy(result, definition.Id);
            return true;
        }

        private void Occupy(BoardPlacementResult result, string id)
        {
            for (var index = 0; index < result.Cells.Count; index++)
            {
                var coordinate = result.Cells[index];
                _occupants[coordinate.Column, coordinate.Row] = id;
            }
        }

        public void SetHighlighted(BoardCoordinate coordinate, bool highlighted)
        {
            if (!IsInside(coordinate)) return;
            if (highlighted) _highlighted.Add(coordinate);
            else _highlighted.Remove(coordinate);
        }

        public bool IsHighlighted(BoardCoordinate coordinate)
        {
            return _highlighted.Contains(coordinate);
        }
    }
}
