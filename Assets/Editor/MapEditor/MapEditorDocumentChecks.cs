using System;
using System.Collections.Generic;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>由父任务在Editor就绪后显式调用；不创建或保存场景、Prefab、资源文件。</summary>
    public static class MapEditorDocumentChecks
    {
        public static string Run()
        {
            var document = ScriptableObject.CreateInstance<MapEditorDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var source = MapData.Create();
                document.Replace(source);
                Require(document.Data.Columns == 11 && document.Data.Rows == 9 &&
                    document.Data.Cells.Length == 99 && document.Data.CellSize == 0.35f, "默认地图参数");
                var origin = new BoardCoordinate(0, 0);
                document.Paint(origin, MapEditorBrush.GlassNormal);
                Require(source.GetObstacle(origin) == ObstacleType.None, "文档障碍独立副本");
                document.Paint(origin, MapEditorBrush.Track);
                RequireCell(document.Data, origin, BaseTerrainType.Track, ObstacleType.GlassNormal, "基础画笔保留障碍");
                Require(source.GetBaseTerrain(origin) == BaseTerrainType.Empty, "文档基础独立副本");
                document.Paint(origin, MapEditorBrush.GlassColored);
                RequireCell(document.Data, origin, BaseTerrainType.Track, ObstacleType.GlassColored, "障碍画笔保留基础");
                var coloredTrack = document.Data.Clone();
                document.Paint(origin, MapEditorBrush.RemoveObstacle);
                RequireCell(document.Data, origin, BaseTerrainType.Track, ObstacleType.None, "擦除仅移除障碍");
                Require(!MapEditorDocument.ContentEquals(coloredTrack, document.Data), "仅障碍变化标脏");
                var plainTrack = document.Data.Clone();
                document.Paint(origin, MapEditorBrush.Empty);
                Require(!MapEditorDocument.ContentEquals(plainTrack, document.Data), "仅基础变化标脏");
                document.Paint(origin, MapEditorBrush.GlassNormal);
                document.Paint(origin, MapEditorBrush.ResetCell);
                RequireCell(document.Data, origin, BaseTerrainType.Empty, ObstacleType.None, "单格重置两层");

                var center = new BoardCoordinate(5, 4);
                foreach (var end in new[]
                {
                    new BoardCoordinate(10, 5), new BoardCoordinate(6, 8), new BoardCoordinate(4, 8),
                    new BoardCoordinate(0, 5), new BoardCoordinate(0, 3), new BoardCoordinate(4, 0),
                    new BoardCoordinate(6, 0), new BoardCoordinate(10, 3), center
                })
                {
                    var cells = new List<BoardCoordinate>(MapEditorDocument.RasterizeLine(center, end));
                    Require(cells[0].Equals(center) && cells[cells.Count - 1].Equals(end), "Bresenham端点");
                    Require(cells.Count == Math.Max(Math.Abs(end.Column - center.Column), Math.Abs(end.Row - center.Row)) + 1,
                        "Bresenham各象限长度");
                    for (var index = 1; index < cells.Count; index++)
                        Require(Math.Abs(cells[index].Column - cells[index - 1].Column) <= 1 &&
                            Math.Abs(cells[index].Row - cells[index - 1].Row) <= 1 && !cells[index].Equals(cells[index - 1]),
                            "Bresenham连续且无重复");
                }

                document.Fill(MapEditorBrush.ResetCell);
                document.Fill(MapEditorBrush.GlassNormal);
                var beforeStroke = document.Data.Clone();
                var group = BeginUndo(document, "MapEditor检查：完整拖拽");
                document.PaintLine(new BoardCoordinate(0, 0), new BoardCoordinate(5, 4), MapEditorBrush.Track);
                document.PaintLine(new BoardCoordinate(5, 4), new BoardCoordinate(10, 8), MapEditorBrush.Track);
                EndUndo(group);
                var afterStroke = document.Data.Clone();
                RequireCell(document.Data, new BoardCoordinate(10, 8), BaseTerrainType.Track, ObstacleType.GlassNormal,
                    "快速拖拽端点保留障碍");
                Undo.PerformUndo();
                Require(MapEditorDocument.ContentEquals(beforeStroke, document.Data), "整次手势一次撤销");
                Require(!MapEditorDocument.ContentEquals(afterStroke, document.Data), "保存基线后撤销应变脏");
                Undo.PerformRedo();
                Require(MapEditorDocument.ContentEquals(afterStroke, document.Data), "整次手势一次重做");

                document.Fill(MapEditorBrush.ResetCell);
                document.Fill(MapEditorBrush.Track);
                document.FillRectangle(new BoardCoordinate(7, 4), new BoardCoordinate(3, 2), MapEditorBrush.GlassColored);
                for (var row = 0; row < 9; row++)
                    for (var column = 0; column < 11; column++)
                        RequireCell(document.Data, new BoardCoordinate(column, row), BaseTerrainType.Track,
                            column >= 3 && column <= 7 && row >= 2 && row <= 4 ? ObstacleType.GlassColored : ObstacleType.None,
                            "反向障碍矩形边界及基础保留");

                document.FillRectangle(new BoardCoordinate(3, 2), new BoardCoordinate(7, 4), MapEditorBrush.Empty);
                RequireCell(document.Data, new BoardCoordinate(3, 2), BaseTerrainType.Empty, ObstacleType.GlassColored,
                    "基础矩形保留障碍");
                document.Fill(MapEditorBrush.Empty);
                RequireCell(document.Data, new BoardCoordinate(3, 2), BaseTerrainType.Empty, ObstacleType.GlassColored,
                    "基础全图填充保留障碍");

                document.Paint(origin, MapEditorBrush.GlassNormal);
                document.Paint(origin, MapEditorBrush.Track);
                document.Paint(new BoardCoordinate(10, 8), MapEditorBrush.Track);
                var beforeResize = document.Data.Clone();
                group = BeginUndo(document, "MapEditor检查：尺寸");
                document.Resize(15, 12);
                EndUndo(group);
                RequireCell(document.Data, origin, BaseTerrainType.Track, ObstacleType.GlassNormal, "扩展保留完整两层");
                RequireCell(document.Data, new BoardCoordinate(10, 8), BaseTerrainType.Track, ObstacleType.None, "扩展保留右上");
                RequireCell(document.Data, new BoardCoordinate(14, 11), BaseTerrainType.Empty, ObstacleType.None, "扩展新增默认两层");
                Undo.PerformUndo();
                Require(MapEditorDocument.ContentEquals(beforeResize, document.Data), "尺寸撤销恢复地形与尺寸");
                Undo.PerformRedo();
                Require(document.Data.Columns == 15 && document.Data.Rows == 12, "尺寸重做");
                document.Resize(4, 3);
                RequireCell(document.Data, origin, BaseTerrainType.Track, ObstacleType.GlassNormal, "缩小保留左下两层");
                RequireCell(document.Data, new BoardCoordinate(3, 2), BaseTerrainType.Empty, ObstacleType.GlassColored, "缩小保留矩形两层");

                var beforeReload = document.Data.Clone();
                document.OnBeforeSerialize();
                document.OnAfterDeserialize();
                Require(MapEditorDocument.ContentEquals(beforeReload, document.Data), "窗口文档快照恢复两层");
                document.Paint(origin, MapEditorBrush.RemoveObstacle);
                Require(beforeReload.GetObstacle(origin) == ObstacleType.GlassNormal, "恢复后仍为深副本");
                CheckLegacySnapshot();

                document.Fill(MapEditorBrush.ResetCell);
                foreach (var cell in document.Data.Cells)
                    Require(cell.BaseTerrain == BaseTerrainType.Empty && cell.Obstacle == ObstacleType.None, "重置全图两层");
                Require(!document.Paint(new BoardCoordinate(-1, 0), MapEditorBrush.Track), "越界画笔忽略");
                return "MapEditor文档检查通过：两层独立绘制/矩形/填充、障碍擦除、重置、深副本、Bresenham八象限、整笔/尺寸Undo与Redo、保存基线、扩展/裁剪、内存快照与旧内存迁移保护。";
            }
            finally
            {
                Undo.ClearUndo(document);
                UnityEngine.Object.DestroyImmediate(document);
            }
        }

        private static void CheckLegacySnapshot()
        {
            var snapshot = new MapEditorMapSnapshot
            {
                Version = 1, Columns = 4, Rows = 1, CellSize = 0.35f, NormalGlassId = 1, ColoredGlassId = 2,
                Terrains = new[]
                {
                    MapEditorMapSnapshot.LegacyTerrain.Track, MapEditorMapSnapshot.LegacyTerrain.Empty,
                    MapEditorMapSnapshot.LegacyTerrain.GlassNormal, MapEditorMapSnapshot.LegacyTerrain.GlassColored
                }
            };
            var restored = snapshot.Restore(out var migrated);
            Require(migrated && restored.Version == 2, "旧内存迁移到V2");
            RequireCell(restored, new BoardCoordinate(0, 0), BaseTerrainType.Track, ObstacleType.None, "旧轨道迁移");
            RequireCell(restored, new BoardCoordinate(1, 0), BaseTerrainType.Empty, ObstacleType.None, "旧空位迁移");
            RequireCell(restored, new BoardCoordinate(2, 0), BaseTerrainType.Empty, ObstacleType.GlassNormal, "旧普通玻璃迁移");
            RequireCell(restored, new BoardCoordinate(3, 0), BaseTerrainType.Empty, ObstacleType.GlassColored, "旧彩色玻璃迁移");
            Require(snapshot.Version == 1 && snapshot.Terrains.Length == 4, "迁移不修改旧快照");

            var baseline = MapEditorMapSnapshot.Capture(restored).Restore(out migrated);
            Require(!migrated && MapEditorDocument.ContentEquals(restored, baseline), "保存基线快照恢复");
            restored.SetBaseTerrain(new BoardCoordinate(2, 0), BaseTerrainType.Track);
            Require(!MapEditorDocument.ContentEquals(restored, baseline), "基线与未保存编辑分别恢复");

            snapshot.Terrains = null;
            var rejected = false;
            try { snapshot.Restore(out migrated); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && snapshot.Cells == null && snapshot.Terrains == null, "缺失旧数据拒绝静默重置");
            snapshot = MapEditorMapSnapshot.Capture(baseline);
            snapshot.Cells = Array.Empty<MapCellData>();
            rejected = false;
            try { snapshot.Restore(out migrated); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && snapshot.Cells.Length == 0, "V4空Cells保留错误快照");
        }

        private static void RequireCell(MapData map, BoardCoordinate coordinate, BaseTerrainType terrain,
            ObstacleType obstacle, string check)
        {
            Require(map.GetBaseTerrain(coordinate) == terrain && map.GetObstacle(coordinate) == obstacle, check);
        }

        private static int BeginUndo(MapEditorDocument document, string name)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(document, name);
            return group;
        }

        private static void EndUndo(int group)
        {
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
        }

        private static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("MapEditor检查失败：" + check);
        }
    }
}
