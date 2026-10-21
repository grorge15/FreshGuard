using System;
using System.Collections.Generic;
using GameConfig.board;

namespace GameLogic
{
    /// <summary>单局不可变布局；坐标以棋盘左下角为原点，不保存世界位置。</summary>
    public sealed class BoardLayoutRules
    {
        private readonly HashSet<BoardCoordinate> _open = new HashSet<BoardCoordinate>();
        private readonly MapData _map;
        public bool IsTerrainMap => _map != null;
        public int Columns { get; }
        public int Rows { get; }
        public float CellSize { get; }
        public int ReservedBorderWidth { get; }
        public int NormalGlassId { get; }
        public int ColoredGlassId { get; }
        public BoardCoordinate ColoredGlassCell { get; }
        public IReadOnlyCollection<BoardCoordinate> OpenCells => _open;

        public BoardLayoutRules(MapData map)
        {
            MapValidator.ThrowIfInvalid(map);
            _map = map.Clone();
            Columns = map.Columns; Rows = map.Rows; CellSize = map.CellSize;
            NormalGlassId = map.NormalGlassId; ColoredGlassId = map.ColoredGlassId;
            ReservedBorderWidth = 0;
            for (var row = 0; row < Rows; row++)
                for (var column = 0; column < Columns; column++)
                {
                    var cell = new BoardCoordinate(column, row);
                    if (_map.CanPlaceUnit(cell)) _open.Add(cell);
                }
        }

        public MapData CreateMap() => _map?.Clone();
        public BaseTerrainType GetInitialBaseTerrain(BoardCoordinate cell)
        {
            RequireInside(cell);
            return _map != null ? _map.GetBaseTerrain(cell) : BaseTerrainType.Empty;
        }
        public ObstacleType GetInitialObstacle(BoardCoordinate cell)
        {
            RequireInside(cell);
            if (_map != null) return _map.GetObstacle(cell);
            return _open.Contains(cell) ? ObstacleType.None : cell.Equals(ColoredGlassCell) ? ObstacleType.GlassColored : ObstacleType.GlassNormal;
        }
        private void RequireInside(BoardCoordinate cell)
        {
            if (cell.Column < 0 || cell.Column >= Columns || cell.Row < 0 || cell.Row >= Rows)
                throw new ArgumentOutOfRangeException(nameof(cell));
        }
        public int GetGlassConfigId(BoardCoordinate cell)
        {
            var obstacle = GetInitialObstacle(cell);
            if (obstacle == ObstacleType.None) throw new InvalidOperationException("该格没有玻璃障碍。");
            return obstacle == ObstacleType.GlassColored ? ColoredGlassId : NormalGlassId;
        }

        public BoardLayoutRules(BoardLayout config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Columns = config.Columns;
            Rows = config.Rows;
            CellSize = config.CellSize;
            ReservedBorderWidth = config.ReservedBorderWidth;
            NormalGlassId = config.NormalGlassId;
            ColoredGlassId = config.ColoredGlassId;
            if (Columns <= 0 || Rows <= 0 || ReservedBorderWidth <= 0 ||
                ReservedBorderWidth * 2L >= Math.Min(Columns, Rows) ||
                float.IsNaN(CellSize) || float.IsInfinity(CellSize) || CellSize <= 0f ||
                NormalGlassId <= 0 || ColoredGlassId <= 0 || NormalGlassId == ColoredGlassId)
                throw new ArgumentException("棋盘尺寸、格距、保留外圈或玻璃配置无效。", nameof(config));
            if (config.OpenCells == null || config.OpenCells.Length == 0)
                throw new ArgumentException("棋盘必须配置初始开放格。", nameof(config));
            foreach (var pair in config.OpenCells)
            {
                if (pair == null || pair.Length != 2) throw new ArgumentException("开放格必须是列、行二元组。");
                var cell = new BoardCoordinate(pair[0], pair[1]);
                if (!IsInterior(cell) || !_open.Add(cell)) throw new ArgumentException("开放格越界、位于外圈或重复。");
            }
            if (config.ColoredGlassCell == null || config.ColoredGlassCell.Length != 2)
                throw new ArgumentException("必须配置唯一彩色玻璃坐标。");
            ColoredGlassCell = new BoardCoordinate(config.ColoredGlassCell[0], config.ColoredGlassCell[1]);
            if (!IsInterior(ColoredGlassCell) || _open.Contains(ColoredGlassCell))
                throw new ArgumentException("彩色玻璃必须位于内部未开放格。");
        }

        public bool IsInterior(BoardCoordinate cell) =>
            cell.Column >= ReservedBorderWidth && cell.Column < Columns - ReservedBorderWidth &&
            cell.Row >= ReservedBorderWidth && cell.Row < Rows - ReservedBorderWidth;

        public bool HasInitialGlass(BoardCoordinate cell)
        {
            if (cell.Column < 0 || cell.Column >= Columns || cell.Row < 0 || cell.Row >= Rows) return false;
            return GetInitialObstacle(cell) != ObstacleType.None;
        }
    }
}
