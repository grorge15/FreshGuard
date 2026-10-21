using System;
using System.Collections.Generic;

namespace GameLogic
{
    public enum BaseTerrainType : byte { Track = 1, Empty = 2 }
    public enum ObstacleType : byte { None = 0, GlassNormal = 1, GlassColored = 2 }

    [Serializable]
    public struct MapCellData
    {
        public BaseTerrainType BaseTerrain;
        public ObstacleType Obstacle;
        public MapCellData(BaseTerrainType baseTerrain, ObstacleType obstacle = ObstacleType.None)
        { BaseTerrain = baseTerrain; Obstacle = obstacle; }
    }

    /// <summary>地图的初始布局；仅保存数据。对局必须复制，不能修改加载得到的共享模板。</summary>
    [Serializable]
    public sealed class MapData
    {
        public const int CURRENT_VERSION = 2;
        public const int MAX_DIMENSION = 64;
        public int Version = CURRENT_VERSION;
        public int Rows = 9;
        public int Columns = 11;
        public float CellSize = 0.35f;
        public int NormalGlassId = 1;
        public int ColoredGlassId = 2;
        public MapCellData[] Cells;

        public static MapData Create(int columns = 11, int rows = 9, float cellSize = 0.35f)
        {
            if (columns < 1 || columns > MAX_DIMENSION || rows < 1 || rows > MAX_DIMENSION)
                throw new ArgumentOutOfRangeException(nameof(columns), "行列数必须在1～64之间。");
            if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            var map = new MapData { Columns = columns, Rows = rows, CellSize = cellSize,
                Cells = new MapCellData[columns * rows] };
            for (var i = 0; i < map.Cells.Length; i++) map.Cells[i] = new MapCellData(BaseTerrainType.Empty);
            return map;
        }

        public MapData Clone()
        {
            return new MapData { Version = Version, Rows = Rows, Columns = Columns, CellSize = CellSize,
                NormalGlassId = NormalGlassId, ColoredGlassId = ColoredGlassId,
                Cells = Cells == null ? null : (MapCellData[])Cells.Clone() };
        }

        public MapData Resize(int columns, int rows)
        {
            MapValidator.ThrowIfInvalid(this);
            var resized = Create(columns, rows, CellSize);
            resized.NormalGlassId = NormalGlassId;
            resized.ColoredGlassId = ColoredGlassId;
            for (var row = 0; row < Math.Min(Rows, rows); row++)
                Array.Copy(Cells, row * Columns, resized.Cells, row * columns, Math.Min(Columns, columns));
            return resized;
        }

        public bool IsInside(BoardCoordinate cell) => cell.Column >= 0 && cell.Column < Columns && cell.Row >= 0 && cell.Row < Rows;
        private int GetIndex(BoardCoordinate cell)
        {
            if (!IsInside(cell)) throw new ArgumentOutOfRangeException(nameof(cell), "地图坐标越界。");
            return cell.Row * Columns + cell.Column;
        }

        public BaseTerrainType GetBaseTerrain(BoardCoordinate cell) => Cells[GetIndex(cell)].BaseTerrain;
        public ObstacleType GetObstacle(BoardCoordinate cell) => Cells[GetIndex(cell)].Obstacle;

        /// <summary>仅修改基础地形，保留障碍；不重建战斗对象。</summary>
        public void SetBaseTerrain(BoardCoordinate cell, BaseTerrainType terrain)
        {
            var index = GetIndex(cell);
            if (!IsValidBaseTerrain(terrain)) throw new ArgumentOutOfRangeException(nameof(terrain));
            Cells[index].BaseTerrain = terrain;
        }

        /// <summary>仅修改障碍，保留基础地形；战斗中仅由玻璃控制器受控移除。</summary>
        public void SetObstacle(BoardCoordinate cell, ObstacleType obstacle)
        {
            var index = GetIndex(cell);
            if (!IsValidObstacle(obstacle)) throw new ArgumentOutOfRangeException(nameof(obstacle));
            Cells[index].Obstacle = obstacle;
        }

        public bool CanPlaceUnit(BoardCoordinate cell) => IsInside(cell) &&
            GetBaseTerrain(cell) == BaseTerrainType.Empty && GetObstacle(cell) == ObstacleType.None;
        public bool CanEnergyBallPass(BoardCoordinate cell) => IsInside(cell) &&
            IsValidBaseTerrain(GetBaseTerrain(cell)) && GetObstacle(cell) == ObstacleType.None;
        public static bool IsValidBaseTerrain(BaseTerrainType terrain) => terrain == BaseTerrainType.Track || terrain == BaseTerrainType.Empty;
        public static bool IsValidObstacle(ObstacleType obstacle) => obstacle >= ObstacleType.None && obstacle <= ObstacleType.GlassColored;
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
