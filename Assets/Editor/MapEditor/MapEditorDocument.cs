using System;
using System.Collections.Generic;
using GameLogic;
using UnityEngine;

namespace FreshGuard.Editor
{
    // 保留旧画笔的1～4编号，使已经打开的窗口重载后仍选中原工具。
    public enum MapEditorBrush : byte
    {
        Track = 1, Empty = 2, GlassNormal = 3, GlassColored = 4, RemoveObstacle = 5, ResetCell = 6
    }

    /// <summary>编辑器专用序列化快照；Terrains只接收旧窗口内存，不进入运行模型或地图文件。</summary>
    [Serializable]
    internal sealed class MapEditorMapSnapshot
    {
        internal enum LegacyTerrain : byte { Track = 1, Empty = 2, GlassNormal = 3, GlassColored = 4 }

        public int Version;
        public int Rows;
        public int Columns;
        public float CellSize;
        public int NormalGlassId;
        public int ColoredGlassId;
        public MapCellData[] Cells;
        public LegacyTerrain[] Terrains;

        public static MapEditorMapSnapshot Capture(MapData map)
        {
            if (map == null) return null;
            return new MapEditorMapSnapshot
            {
                Version = map.Version, Rows = map.Rows, Columns = map.Columns, CellSize = map.CellSize,
                NormalGlassId = map.NormalGlassId, ColoredGlassId = map.ColoredGlassId,
                Cells = map.Cells == null ? null : (MapCellData[])map.Cells.Clone()
            };
        }

        public MapData Restore(out bool migrated)
        {
            migrated = false;
            if (Columns < 1 || Columns > 64 || Rows < 1 || Rows > 64)
                throw new InvalidOperationException("窗口内存地图尺寸无效；原快照已保留，未重建空图。");
            var count = Columns * Rows;
            MapCellData[] cells;
            if (Cells != null && Cells.Length == count)
            {
                if (Version != 2) throw new InvalidOperationException("窗口内存分层地图版本无效。");
                cells = (MapCellData[])Cells.Clone();
                foreach (var cell in cells)
                    if ((cell.BaseTerrain != BaseTerrainType.Track && cell.BaseTerrain != BaseTerrainType.Empty) ||
                        (cell.Obstacle != ObstacleType.None && cell.Obstacle != ObstacleType.GlassNormal &&
                         cell.Obstacle != ObstacleType.GlassColored))
                        throw new InvalidOperationException("窗口内存包含无效的基础地形或障碍编号；原快照已保留。");
            }
            else if (Version == 1 && (Cells == null || Cells.Length == 0) && Terrains != null && Terrains.Length == count)
            {
                cells = new MapCellData[count];
                for (var index = 0; index < count; index++)
                {
                    var legacy = Terrains[index];
                    if (legacy < LegacyTerrain.Track || legacy > LegacyTerrain.GlassColored)
                        throw new InvalidOperationException("旧窗口内存包含无效地形编号；原快照已保留。");
                    cells[index] = new MapCellData
                    {
                        BaseTerrain = legacy == LegacyTerrain.Track ? BaseTerrainType.Track : BaseTerrainType.Empty,
                        Obstacle = legacy == LegacyTerrain.GlassNormal ? ObstacleType.GlassNormal :
                            legacy == LegacyTerrain.GlassColored ? ObstacleType.GlassColored : ObstacleType.None
                    };
                }
                migrated = true;
            }
            else throw new InvalidOperationException("窗口内存地图的Cells缺失，且没有完整的旧Terrains可迁移；原快照已保留，禁止覆盖保存。");
            return new MapData
            {
                Version = 2, Columns = Columns, Rows = Rows, CellSize = CellSize,
                NormalGlassId = NormalGlassId, ColoredGlassId = ColoredGlassId, Cells = cells
            };
        }
    }

    /// <summary>仅驻留编辑器内存的文档；整次手势通过窗口登记一次完整对象Undo。</summary>
    public sealed class MapEditorDocument : ScriptableObject, ISerializationCallbackReceiver
    {
        // 同名内联字段接收旧MapData序列化内容，避免运行模型删除Terrains后先丢掉未保存编辑。
        [SerializeField] private MapEditorMapSnapshot _data;
        [SerializeField] private bool _wasLegacyMemoryMigrated;
        [NonSerialized] private MapData _runtimeData;

        public MapData Data
        {
            get
            {
                if (_runtimeData == null && _data != null)
                {
                    _runtimeData = _data.Restore(out var migrated);
                    _wasLegacyMemoryMigrated |= migrated;
                }
                return _runtimeData;
            }
        }

        public bool WasLegacyMemoryMigrated => _wasLegacyMemoryMigrated;

        public void OnBeforeSerialize()
        {
            // 恢复失败时不能覆盖仍可供找回的旧快照；回调不调用任何Unity API。
            if (_runtimeData != null) _data = MapEditorMapSnapshot.Capture(_runtimeData);
        }

        public void OnAfterDeserialize() { _runtimeData = null; }

        public void Replace(MapData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            _runtimeData = data.Clone();
            _data = MapEditorMapSnapshot.Capture(_runtimeData);
            _wasLegacyMemoryMigrated = false;
        }

        public bool Paint(BoardCoordinate coordinate, MapEditorBrush brush)
        {
            var map = Data;
            if (!map.IsInside(coordinate)) return false;
            switch (brush)
            {
                case MapEditorBrush.Track: return Paint(coordinate, BaseTerrainType.Track);
                case MapEditorBrush.Empty: return Paint(coordinate, BaseTerrainType.Empty);
                case MapEditorBrush.GlassNormal: return Paint(coordinate, ObstacleType.GlassNormal);
                case MapEditorBrush.GlassColored: return Paint(coordinate, ObstacleType.GlassColored);
                case MapEditorBrush.RemoveObstacle: return Paint(coordinate, ObstacleType.None);
                case MapEditorBrush.ResetCell:
                    var changedBase = Paint(coordinate, BaseTerrainType.Empty);
                    var changedObstacle = Paint(coordinate, ObstacleType.None);
                    return changedBase || changedObstacle;
                default: throw new ArgumentOutOfRangeException(nameof(brush));
            }
        }

        public bool Paint(BoardCoordinate coordinate, BaseTerrainType terrain)
        {
            var map = Data;
            if (!map.IsInside(coordinate) || map.GetBaseTerrain(coordinate) == terrain) return false;
            map.SetBaseTerrain(coordinate, terrain);
            return true;
        }

        public bool Paint(BoardCoordinate coordinate, ObstacleType obstacle)
        {
            var map = Data;
            if (!map.IsInside(coordinate) || map.GetObstacle(coordinate) == obstacle) return false;
            map.SetObstacle(coordinate, obstacle);
            return true;
        }

        public void PaintLine(BoardCoordinate start, BoardCoordinate end, MapEditorBrush brush)
        {
            foreach (var coordinate in RasterizeLine(start, end)) Paint(coordinate, brush);
        }

        public void FillRectangle(BoardCoordinate start, BoardCoordinate end, MapEditorBrush brush)
        {
            var map = Data;
            var left = Math.Max(0, Math.Min(start.Column, end.Column));
            var right = Math.Min(map.Columns - 1, Math.Max(start.Column, end.Column));
            var bottom = Math.Max(0, Math.Min(start.Row, end.Row));
            var top = Math.Min(map.Rows - 1, Math.Max(start.Row, end.Row));
            for (var row = bottom; row <= top; row++)
                for (var column = left; column <= right; column++)
                    Paint(new BoardCoordinate(column, row), brush);
        }

        public void Fill(MapEditorBrush brush)
        {
            FillRectangle(new BoardCoordinate(0, 0), new BoardCoordinate(Data.Columns - 1, Data.Rows - 1), brush);
        }

        public void Resize(int columns, int rows)
        {
            Replace(Data.Resize(columns, rows));
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
            if (ReferenceEquals(left.Cells, right.Cells)) return true;
            if (left.Cells == null || right.Cells == null || left.Cells.Length != right.Cells.Length) return false;
            for (var index = 0; index < left.Cells.Length; index++)
                if (left.Cells[index].BaseTerrain != right.Cells[index].BaseTerrain ||
                    left.Cells[index].Obstacle != right.Cells[index].Obstacle) return false;
            return true;
        }
    }
}
