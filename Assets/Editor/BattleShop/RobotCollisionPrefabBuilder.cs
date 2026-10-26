using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameConfig;
using Luban;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>按完整占格拼接唯一的整体碰撞轮廓。</summary>
    public static class RobotCollisionPrefabBuilder
    {
        [MenuItem("FreshGuard/Robots/Add Physical Colliders")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行模式并等待编译完成。");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            foreach (var config in tables.TbRobot.DataList)
            {
                var path = "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var cells = root.GetComponentsInChildren<SpriteRenderer>(true)
                        .Where(cell => cell.transform.parent == root.transform && cell.name.StartsWith("Cell_", StringComparison.Ordinal)).ToArray();
                    if (cells.Length != config.ShapeId_Ref.CellOffsets.Length || root.GetComponent<GameLogic.RobotView>() == null)
                        throw new InvalidOperationException("机器人实体格或视图组件不完整：" + path);
                    var dragArea = root.transform.Find("DragHitArea");
                    if (dragArea != null) UnityEngine.Object.DestroyImmediate(dragArea.gameObject);
                    foreach (var collider in root.GetComponentsInChildren<Collider2D>(true))
                        if (collider.transform != root.transform || !(collider is PolygonCollider2D))
                            UnityEngine.Object.DestroyImmediate(collider);
                    var body = root.GetComponent<PolygonCollider2D>();
                    if (body == null) body = root.AddComponent<PolygonCollider2D>();
                    var paths = BuildOutline(config.ShapeId_Ref.CellOffsets);
                    body.offset = Vector2.zero;
                    body.pathCount = paths.Count;
                    for (var i = 0; i < paths.Count; i++) body.SetPath(i, paths[i]);
                    body.isTrigger = false;
                    body.enabled = true;
                    var label = root.transform.Find("LevelLabel");
                    if (label != null) UnityEngine.Object.DestroyImmediate(label.gameObject);
                    // Static robots need no Rigidbody2D; the moving ball owns the dynamic body.
                    if (root.GetComponentsInChildren<Collider2D>(true).Length != 1 ||
                        root.GetComponentsInChildren<Rigidbody2D>(true).Length != 0)
                        throw new InvalidOperationException("机器人存在额外碰撞体或刚体，请检查：" + path);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Robot Collision] 机器人已统一为根节点整体轮廓，内部碰撞体和等级文字已移除。");
        }

        private static List<Vector2[]> BuildOutline(int[][] offsets)
        {
            var cells = new HashSet<Vector2Int>(offsets.Select(c => new Vector2Int(c[0], c[1])));
            if (cells.Count != offsets.Length || cells.Count == 0)
                throw new InvalidOperationException("机器人占格不能为空或重复。");
            // 用两倍格坐标描述顶点，排除相邻格共享边，避免浮点拼接误差和内部接缝。
            var edges = new Dictionary<Vector2Int, Vector2Int>();
            foreach (var c in cells.OrderBy(c => c.x).ThenBy(c => c.y))
            {
                var bl = new Vector2Int(c.x * 2 - 1, c.y * 2 - 1);
                var br = bl + Vector2Int.right * 2;
                var tr = br + Vector2Int.up * 2;
                var tl = bl + Vector2Int.up * 2;
                if (!cells.Contains(c + Vector2Int.down)) AddEdge(edges, bl, br);
                if (!cells.Contains(c + Vector2Int.right)) AddEdge(edges, br, tr);
                if (!cells.Contains(c + Vector2Int.up)) AddEdge(edges, tr, tl);
                if (!cells.Contains(c + Vector2Int.left)) AddEdge(edges, tl, bl);
            }
            var paths = new List<Vector2[]>();
            while (edges.Count > 0)
            {
                var start = edges.Keys.OrderBy(p => p.x).ThenBy(p => p.y).First();
                var current = start;
                var points = new List<Vector2>();
                do
                {
                    points.Add((Vector2)current * (GameLogic.MapGridMath.REFERENCE_CELL_SIZE * 0.5f));
                    if (!edges.TryGetValue(current, out var next))
                        throw new InvalidOperationException("机器人轮廓不能闭合。");
                    edges.Remove(current);
                    current = next;
                } while (current != start);
                // 消除共线顶点，长边上不留格子接缝。
                for (var i = points.Count - 1; i >= 0 && points.Count > 3; i--)
                {
                    var a = points[(i + points.Count - 1) % points.Count];
                    var b = points[i];
                    var c = points[(i + 1) % points.Count];
                    if (Mathf.Abs((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)) < 0.000001f)
                        points.RemoveAt(i);
                }
                paths.Add(points.ToArray());
            }
            return paths;
        }

        private static void AddEdge(Dictionary<Vector2Int, Vector2Int> edges, Vector2Int from, Vector2Int to)
        {
            if (edges.ContainsKey(from))
                throw new InvalidOperationException("机器人包含仅顶点相连的占格，请按边连接格子。");
            edges.Add(from, to);
        }
    }
}
