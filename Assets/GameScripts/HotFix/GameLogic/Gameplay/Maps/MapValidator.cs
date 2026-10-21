using System;
using System.Collections.Generic;
using System.IO;

namespace GameLogic
{
    public enum MapValidationSeverity { Error, Warning }
    public sealed class MapValidationIssue
    {
        public MapValidationSeverity Severity { get; }
        public string Message { get; }
        public BoardCoordinate? Coordinate { get; }
        public MapValidationIssue(MapValidationSeverity severity, string message, BoardCoordinate? coordinate = null)
        { Severity = severity; Message = message; Coordinate = coordinate; }
        public override string ToString() => Coordinate.HasValue
            ? $"{Message} ({Coordinate.Value.Column},{Coordinate.Value.Row})" : Message;
    }

    public static class MapValidator
    {
        public static void ThrowIfInvalid(MapData map)
        {
            var issues = ValidateStructure(map);
            if (issues.Count != 0) throw new InvalidDataException(issues[0].ToString());
        }

        private static List<MapValidationIssue> ValidateStructure(MapData map)
        {
            var issues = new List<MapValidationIssue>();
            if (map == null) { issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "地图为空。")); return issues; }
            if (map.Version != MapData.CURRENT_VERSION) issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "不支持的地图版本：" + map.Version));
            if (map.Columns < 1 || map.Columns > MapData.MAX_DIMENSION || map.Rows < 1 || map.Rows > MapData.MAX_DIMENSION)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "行列数必须在1～64之间。"));
            if (float.IsNaN(map.CellSize) || float.IsInfinity(map.CellSize) || map.CellSize <= 0f ||
                float.IsInfinity(map.CellSize * Math.Max(map.Columns, map.Rows)))
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "格距和地图物理尺寸必须为有限正数。"));
            if (map.NormalGlassId <= 0 || map.ColoredGlassId <= 0 || map.NormalGlassId == map.ColoredGlassId)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "两种玻璃配置ID必须为不同的正整数。"));
            if (map.Cells == null || map.Cells.Length != (long)map.Rows * map.Columns)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "格子数量与行列数不一致。"));
            if (issues.Count != 0) return issues;
            for (var i = 0; i < map.Cells.Length; i++)
            {
                var cell = new BoardCoordinate(i % map.Columns, i / map.Columns);
                if (!MapData.IsValidBaseTerrain(map.Cells[i].BaseTerrain))
                    issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "未知基础地形编号：" + (byte)map.Cells[i].BaseTerrain, cell));
                if (!MapData.IsValidObstacle(map.Cells[i].Obstacle))
                    issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "未知障碍编号：" + (byte)map.Cells[i].Obstacle, cell));
            }
            return issues;
        }

        public static List<MapValidationIssue> Validate(MapData map)
        {
            var issues = ValidateStructure(map);
            if (issues.Count != 0) return issues;
            var empty = 0;
            var visited = new bool[map.Cells.Length];
            var queue = new Queue<BoardCoordinate>();
            var components = 0;
            for (var i = 0; i < map.Cells.Length; i++)
            {
                var cell = new BoardCoordinate(i % map.Columns, i / map.Columns);
                if (map.CanPlaceUnit(cell)) empty++;
                if (visited[i] || !map.CanEnergyBallPass(cell)) continue;
                components++;
                if (components > 1) issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "此通行区域与其他区域不连通。", cell));
                visited[i] = true;
                queue.Enqueue(cell);
                while (queue.Count != 0)
                    foreach (var neighbor in map.GetNeighbors(queue.Dequeue()))
                    {
                        var index = neighbor.Row * map.Columns + neighbor.Column;
                        if (visited[index] || !map.CanEnergyBallPass(neighbor)) continue;
                        visited[index] = true;
                        queue.Enqueue(neighbor);
                    }
            }
            if (empty == 0) issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "地图没有可部署空位。"));
            if (components == 0) issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "地图没有能源球通行区域。"));
            return issues;
        }

        /// <summary>按当前球的物理尺寸检查几何中心；偶数尺寸的中心可能横跨多个格子。</summary>
        public static List<MapValidationIssue> ValidateBallClearance(MapData map, float radiusInBoardUnits)
        {
            ThrowIfInvalid(map);
            if (radiusInBoardUnits <= 0f || float.IsNaN(radiusInBoardUnits) || float.IsInfinity(radiusInBoardUnits))
                throw new ArgumentOutOfRangeException(nameof(radiusInBoardUnits));
            var issues = new List<MapValidationIssue>();
            if (map.Columns * map.CellSize * 0.5f <= radiusInBoardUnits || map.Rows * map.CellSize * 0.5f <= radiusInBoardUnits)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "地图中心无法容纳能源球：与物理外墙重叠。"));
            for (var row = 0; row < map.Rows; row++)
                for (var column = 0; column < map.Columns; column++)
                {
                    var cell = new BoardCoordinate(column, row);
                    if (map.CanEnergyBallPass(cell)) continue;
                    var centerX = (column + 0.5f - map.Columns * 0.5f) * map.CellSize;
                    var centerY = (row + 0.5f - map.Rows * 0.5f) * map.CellSize;
                    var dx = Math.Max(Math.Abs(centerX) - map.CellSize * 0.5f, 0f);
                    var dy = Math.Max(Math.Abs(centerY) - map.CellSize * 0.5f, 0f);
                    if ((double)dx * dx + (double)dy * dy <= (double)radiusInBoardUnits * radiusInBoardUnits)
                        issues.Add(new MapValidationIssue(MapValidationSeverity.Error, "玻璃与中心出生球体重叠。", cell));
                }
            if (map.CellSize < radiusInBoardUnits * 2f)
                issues.Add(new MapValidationIssue(MapValidationSeverity.Warning, "单格通道宽度小于能源球直径，连通网格不代表球体可以通过。"));
            return issues;
        }
    }
}
