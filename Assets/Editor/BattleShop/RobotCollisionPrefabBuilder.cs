using System;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>为现有机器人实体格装配静态碰撞体；不修改图标或棋盘空格。</summary>
    public static class RobotCollisionPrefabBuilder
    {
        [MenuItem("FreshGuard/Robots/Add Physical Colliders")]
        public static void Build()
        {
            if (Application.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行模式并等待编译完成。");
            foreach (var name in new[] { "RobotI", "RobotL", "RobotT" })
            {
                var path = "Assets/AssetRaw/Actor/Robots/Prefabs/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var cells = root.GetComponentsInChildren<SpriteRenderer>(true);
                    if (cells.Length != 4 || root.GetComponent<GameLogic.RobotView>() == null)
                        throw new InvalidOperationException("机器人实体格或视图组件不完整：" + path);
                    foreach (var cell in cells)
                    {
                        if (!cell.name.StartsWith("Cell_", StringComparison.Ordinal) || cell.sprite == null)
                            throw new InvalidOperationException("机器人格子缺少图形：" + path);
                        var collider = cell.GetComponent<BoxCollider2D>();
                        if (collider == null) collider = cell.gameObject.AddComponent<BoxCollider2D>();
                        collider.size = cell.sprite.bounds.size;
                        collider.offset = cell.sprite.bounds.center;
                        collider.isTrigger = false;
                        collider.enabled = true;
                    }
                    // A static collider needs no Rigidbody2D; the moving ball owns the dynamic body.
                    if (root.GetComponentsInChildren<Collider2D>(true).Length != cells.Length ||
                        root.GetComponentsInChildren<Rigidbody2D>(true).Length != 0)
                        throw new InvalidOperationException("机器人存在额外碰撞体或刚体，请检查：" + path);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Robot Collision] I/L/T 已装配逐格静态碰撞体；重复执行不增加组件。");
        }
    }
}
