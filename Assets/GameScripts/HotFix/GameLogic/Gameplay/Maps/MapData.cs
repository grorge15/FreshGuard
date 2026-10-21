using System;
using System.Collections.Generic;

namespace GameLogic
{
    public enum TerrainType : byte { Track = 1, Empty = 2, GlassNormal = 3, GlassColored = 4 }

    /// <summary>地图的初始布局；仅保存数据。对局必须复制，不能修改加载得到的共享模板。</summary>
    [Serializable]
    public sealed class MapData
    {
        public const int CURRENT_VERSION = 1;
        public const int MAX_DIMENSION = 64;
        public int Version = CURRENT_VERSION;
        public int Rows = 9;
        public int Columns = 11;
        public float CellSize = 0.35f;
        public int NormalGlassId = 1;
        public int ColoredGlassId = 2;
        public TerrainType[] Terrains;

        public static MapData Create(int columns = 11, int rows = 9, float cellSize = 0.35f)
        {
            if (columns < 1 || columns > MAX_DIMENSION || rows < 1 || rows > MAX_DIMENSION)
                throw new ArgumentOutOfRangeException(nameof(columns), "行列数必须在1～64之间。");
            if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            var map = new MapData { Columns = columns, Rows = rows, CellSize = cellSize,
                Terrains = new TerrainType[columns * rows] };
            for (var i = 0; i < map.Terrains.Length; i++) map.Terrains[i] = TerrainType.Empty;
            return map;
        }

        public MapData Clone()
        {
            return new MapData { Version = Version, Rows = Rows, Columns = Columns, CellSize = CellSize,
                NormalGlassId = NormalGlassId, ColoredGlassId = ColoredGlassId,
                Terrains = Terrains == null ? null : (TerrainType[])Terrains.Clone() };
        }

        public MapData Resize(int columns, int rows)
        {
            MapValidator.ThrowIfInvalid(this);
            var resized = Create(columns, rows, CellSize);
            resized.NormalGlassId = NormalGlassId;
            resized.ColoredGlassId = ColoredGlassId;
            for (var row = 0; row < Math.Min(Rows, rows); row++)
                Array.Copy(Terrains, row * Columns, resized.Terrains, row * columns, Math.Min(Columns, columns));
            return resized;
        }

        public bool IsInside(BoardCoordinate cell) => cell.Column >= 0 && cell.Column < Columns && cell.Row >= 0 && cell.Row < Rows;
        public TerrainType GetTerrain(BoardCoordinate cell)
        {
            if (!IsInside(cell)) throw new ArgumentOutOfRangeException(nameof(cell), "地图坐标越界。");
            return Terrains[cell.Row * Columns + cell.Column];
        }

        /// <summary>仅供地图编辑及局内受控状态变更，不直接重建战斗资源。</summary>
        public void SetTerrain(BoardCoordinate cell, TerrainType terrain)
        {
            if (!IsInside(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
            if (!IsValidTerrain(terrain)) throw new ArgumentOutOfRangeException(nameof(terrain));
            Terrains[cell.Row * Columns + cell.Column] = terrain;
        }

        public bool CanPlaceUnit(BoardCoordinate cell) => IsInside(cell) && GetTerrain(cell) == TerrainType.Empty;
        public bool CanEnergyBallPass(BoardCoordinate cell) => IsInside(cell) &&
            (GetTerrain(cell) == TerrainType.Empty || GetTerrain(cell) == TerrainType.Track);
        public static bool IsValidTerrain(TerrainType terrain) => terrain >= TerrainType.Track && terrain <= TerrainType.GlassColored;
        public IEnumerable<BoardCoordinate> GetNeighbors(BoardCoordinate cell)
        {
            if (!IsInside(cell)) yield break;
            if (cell.Column > 0) yield return new BoardCoordinate(cell.Column - 1, cell.Row);
            if (cell.Column + 1 < Columns) yield return new BoardCoordinate(cell.Column + 1, cell.Row);
            if (cell.Row > 0) yield return new BoardCoordinate(cell.Column, cell.Row - 1);
            if (cell.Row + 1 < Rows) yield return new BoardCoordinate(cell.Column, cell.Row + 1);
        }
    }
}
