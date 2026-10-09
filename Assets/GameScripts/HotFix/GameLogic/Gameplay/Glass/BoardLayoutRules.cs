using System;
using System.Collections.Generic;
using GameConfig.board;

namespace GameLogic
{
    /// <summary>单局不可变布局；坐标以棋盘左下角为原点，不保存世界位置。</summary>
    public sealed class BoardLayoutRules
    {
        private readonly HashSet<BoardCoordinate> _open = new HashSet<BoardCoordinate>();
        public int Columns { get; }
        public int Rows { get; }
        public float CellSize { get; }
        public int ReservedBorderWidth { get; }
        public int NormalGlassId { get; }
        public int ColoredGlassId { get; }
        public BoardCoordinate ColoredGlassCell { get; }
        public IReadOnlyCollection<BoardCoordinate> OpenCells => _open;

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

        public bool HasInitialGlass(BoardCoordinate cell) =>
            cell.Column >= 0 && cell.Column < Columns && cell.Row >= 0 && cell.Row < Rows && !_open.Contains(cell);
    }
}
