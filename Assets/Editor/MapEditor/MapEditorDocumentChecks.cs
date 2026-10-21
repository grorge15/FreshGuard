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
                    document.Data.Terrains.Length == 99 && document.Data.CellSize == 0.35f, "默认地图参数");
                document.Paint(new BoardCoordinate(0, 0), TerrainType.GlassNormal);
                Require(source.GetTerrain(new BoardCoordinate(0, 0)) == TerrainType.Empty, "文档独立副本");

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

                document.Fill(TerrainType.Empty);
                var beforeStroke = document.Data.Clone();
                var group = BeginUndo(document, "MapEditor检查：完整拖拽");
                document.PaintLine(new BoardCoordinate(0, 0), new BoardCoordinate(5, 4), TerrainType.Track);
                document.PaintLine(new BoardCoordinate(5, 4), new BoardCoordinate(10, 8), TerrainType.Track);
                EndUndo(group);
                var afterStroke = document.Data.Clone();
                Require(document.Data.GetTerrain(new BoardCoordinate(10, 8)) == TerrainType.Track, "快速拖拽端点");
                Undo.PerformUndo();
                Require(MapEditorDocument.ContentEquals(beforeStroke, document.Data), "整次手势一次撤销");
                Require(!MapEditorDocument.ContentEquals(afterStroke, document.Data), "保存基线后撤销应变脏");
                Undo.PerformRedo();
                Require(MapEditorDocument.ContentEquals(afterStroke, document.Data), "整次手势一次重做");

                document.Fill(TerrainType.Empty);
                document.FillRectangle(new BoardCoordinate(7, 4), new BoardCoordinate(3, 2), TerrainType.GlassColored);
                for (var row = 0; row < 9; row++)
                    for (var column = 0; column < 11; column++)
                        Require(document.Data.GetTerrain(new BoardCoordinate(column, row)) ==
                            (column >= 3 && column <= 7 && row >= 2 && row <= 4 ? TerrainType.GlassColored : TerrainType.Empty),
                            "反向矩形填充边界");

                document.Paint(new BoardCoordinate(0, 0), TerrainType.GlassNormal);
                document.Paint(new BoardCoordinate(10, 8), TerrainType.Track);
                var beforeResize = document.Data.Clone();
                group = BeginUndo(document, "MapEditor检查：尺寸");
                document.Resize(15, 12);
                EndUndo(group);
                Require(document.Data.GetTerrain(new BoardCoordinate(0, 0)) == TerrainType.GlassNormal &&
                    document.Data.GetTerrain(new BoardCoordinate(10, 8)) == TerrainType.Track &&
                    document.Data.GetTerrain(new BoardCoordinate(14, 11)) == TerrainType.Empty, "扩展保留左下并填空");
                Undo.PerformUndo();
                Require(MapEditorDocument.ContentEquals(beforeResize, document.Data), "尺寸撤销恢复地形与尺寸");
                Undo.PerformRedo();
                Require(document.Data.Columns == 15 && document.Data.Rows == 12, "尺寸重做");
                document.Resize(4, 3);
                Require(document.Data.GetTerrain(new BoardCoordinate(0, 0)) == TerrainType.GlassNormal &&
                    document.Data.GetTerrain(new BoardCoordinate(3, 2)) == TerrainType.GlassColored, "缩小保留左下");
                document.Fill(TerrainType.Empty);
                foreach (var terrain in document.Data.Terrains) Require(terrain == TerrainType.Empty, "清空为空位");
                Require(!document.Paint(new BoardCoordinate(-1, 0), TerrainType.Track), "越界画笔忽略");
                return "MapEditor文档检查通过：默认参数、独立副本、Bresenham八象限、矩形、拖拽/尺寸Undo与Redo、保存基线、扩展/裁剪、清空。";
            }
            finally
            {
                Undo.ClearUndo(document);
                UnityEngine.Object.DestroyImmediate(document);
            }
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
