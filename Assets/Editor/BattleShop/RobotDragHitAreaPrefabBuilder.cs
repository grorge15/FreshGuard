using System;
using System.Collections.Generic;
using System.IO;
using GameConfig;
using GameLogic;
using Luban;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>把机器人真实占格的拖拽命中区域直接保存到八个 Prefab。</summary>
    public static class RobotDragHitAreaPrefabBuilder
    {
        private const string PrefabRoot = "Assets/AssetRaw/Actor/Robots/Prefabs/";

        [MenuItem("FreshGuard/Robots/Add Drag Hit Areas")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行模式并等待编译完成。");

            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            foreach (var config in tables.TbRobot.DataList) BuildPrefab(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Robot Drag Hit] 已将八种机器人真实占格的 DragHitArea 保存到 Prefab。");
        }

        private static void BuildPrefab(GameConfig.robot.Robot config)
        {
            var path = PrefabRoot + config.RobotPrefab + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<RobotView>() == null)
                    throw new InvalidOperationException("机器人缺少 RobotView：" + path);

                var old = root.transform.Find("DragHitArea");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

                var cells = new List<CellGeometry>(config.ShapeId_Ref.CellOffsets.Length);
                var byCoordinate = new Dictionary<BoardCoordinate, int>();
                foreach (var offset in config.ShapeId_Ref.CellOffsets)
                {
                    var coordinate = new BoardCoordinate(offset[0], offset[1]);
                    var cell = root.transform.Find("Cell_" + offset[0] + "_" + offset[1]);
                    if (cell == null) throw new InvalidOperationException("缺少实体格：" + path + " " + coordinate);
                    var source = cell.GetComponent<BoxCollider2D>();
                    if (source == null) throw new InvalidOperationException("实体格缺少 BoxCollider2D：" + path + " " + coordinate);
                    byCoordinate.Add(coordinate, cells.Count);
                    cells.Add(new CellGeometry(coordinate, RootLocalRect(root.transform, cell, source)));
                }

                var area = new GameObject("DragHitArea");
                area.layer = root.layer;
                area.transform.SetParent(root.transform, false);
                area.transform.SetAsLastSibling();

                var directions = new[]
                {
                    new BoardCoordinate(1, 0), new BoardCoordinate(-1, 0),
                    new BoardCoordinate(0, 1), new BoardCoordinate(0, -1)
                };
                foreach (var geometry in cells)
                {
                    var rect = geometry.Rect;
                    foreach (var direction in directions)
                    {
                        int neighborIndex;
                        var neighborCoordinate = new BoardCoordinate(
                            geometry.Coordinate.Column + direction.Column,
                            geometry.Coordinate.Row + direction.Row);
                        if (!byCoordinate.TryGetValue(neighborCoordinate, out neighborIndex)) continue;

                        var neighbor = cells[neighborIndex].Rect;
                        if (direction.Column != 0)
                        {
                            var gap = direction.Column > 0 ? neighbor.xMin - rect.xMax : rect.xMin - neighbor.xMax;
                            if (gap > 0f)
                            {
                                var half = gap * 0.5f;
                                if (direction.Column > 0) rect.xMax += half;
                                else rect.xMin -= half;
                            }
                        }
                        else
                        {
                            var gap = direction.Row > 0 ? neighbor.yMin - rect.yMax : rect.yMin - neighbor.yMax;
                            if (gap > 0f)
                            {
                                var half = gap * 0.5f;
                                if (direction.Row > 0) rect.yMax += half;
                                else rect.yMin -= half;
                            }
                        }
                    }

                    var hit = new GameObject("HitCell_" + geometry.Coordinate.Column + "_" + geometry.Coordinate.Row);
                    hit.layer = root.layer;
                    hit.transform.SetParent(area.transform, false);
                    hit.transform.localPosition = rect.center;
                    hit.transform.localRotation = Quaternion.identity;
                    hit.transform.localScale = Vector3.one;
                    hit.AddComponent<RobotDragHitCollider>();
                    var collider = hit.AddComponent<BoxCollider2D>();
                    collider.size = rect.size;
                    collider.offset = Vector2.zero;
                    collider.isTrigger = true;
                    collider.enabled = true;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Rect RootLocalRect(Transform root, Transform cell, BoxCollider2D source)
        {
            var half = source.size * 0.5f;
            var center = source.offset;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var point in new[]
            {
                new Vector3(center.x - half.x, center.y - half.y, 0f),
                new Vector3(center.x - half.x, center.y + half.y, 0f),
                new Vector3(center.x + half.x, center.y - half.y, 0f),
                new Vector3(center.x + half.x, center.y + half.y, 0f)
            })
            {
                var rootPoint = root.InverseTransformPoint(cell.TransformPoint(point));
                min = Vector2.Min(min, rootPoint);
                max = Vector2.Max(max, rootPoint);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private struct CellGeometry
        {
            public readonly BoardCoordinate Coordinate;
            public readonly Rect Rect;

            public CellGeometry(BoardCoordinate coordinate, Rect rect)
            {
                Coordinate = coordinate;
                Rect = rect;
            }
        }
    }
}
