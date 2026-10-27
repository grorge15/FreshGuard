using System;
using System.IO;
using System.Linq;
using GameConfig;
using GameLogic;
using Luban;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FreshGuard.Editor
{
    /// <summary>只更新盘面外观，保留占格节点、整体碰撞体和输入组件。</summary>
    public static class RobotIconPrefabBuilder
    {
        [MenuItem("FreshGuard/Robots/Apply Icon Visuals")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行模式并等待编译完成。");
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            foreach (var config in tables.TbRobot.DataList)
            {
                var iconPath = "Assets/AssetRaw/Actor/Robots/Icons/" + config.RobotIcon + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (sprite == null) throw new InvalidOperationException("缺少机器人 Icon：" + iconPath);
                var pixels = OpaqueBounds(iconPath);
                var path = "Assets/AssetRaw/Actor/Robots/Prefabs/" + config.RobotPrefab + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var cells = root.GetComponentsInChildren<SpriteRenderer>(true)
                        .Where(cell => cell.transform.parent == root.transform && cell.name.StartsWith("Cell_", StringComparison.Ordinal)).ToArray();
                    if (cells.Length != config.ShapeId_Ref.CellOffsets.Length || root.GetComponent<RobotView>() == null)
                        throw new InvalidOperationException("机器人占格节点或视图组件不完整：" + path);
                    var iconNode = root.transform.Find("RobotIcon");
                    if (iconNode == null)
                    {
                        iconNode = new GameObject("RobotIcon").transform;
                        iconNode.gameObject.layer = root.layer;
                        iconNode.SetParent(root.transform, false);
                    }
                    var renderer = iconNode.GetComponent<SpriteRenderer>();
                    if (renderer == null) renderer = iconNode.gameObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprite;
                    renderer.color = Color.white;
                    renderer.enabled = true;
                    renderer.sortingLayerID = cells[0].sortingLayerID;
                    renderer.sortingOrder = cells.Max(cell => cell.sortingOrder) + 1;

                    var offsets = config.ShapeId_Ref.CellOffsets;
                    var min = new Vector2(offsets.Min(cell => cell[0]), offsets.Min(cell => cell[1]));
                    var max = new Vector2(offsets.Max(cell => cell[0]), offsets.Max(cell => cell[1]));
                    var size = (max - min + Vector2.one) * MapGridMath.REFERENCE_CELL_SIZE;
                    // 按非透明内容对齐实际占格，排除 PNG 四周留白，并保留相邻机器人的小间距。
                    size -= Vector2.one * 0.012f;
                    var scale = new Vector2(size.x * sprite.pixelsPerUnit / pixels.width,
                        size.y * sprite.pixelsPerUnit / pixels.height);
                    var artworkCenter = (pixels.center - sprite.pivot) / sprite.pixelsPerUnit;
                    var center = (min + max) * (MapGridMath.REFERENCE_CELL_SIZE * 0.5f);
                    iconNode.localRotation = Quaternion.identity;
                    iconNode.localScale = new Vector3(scale.x, scale.y, 1f);
                    iconNode.localPosition = center - Vector2.Scale(artworkCenter, scale);

                    foreach (var previous in root.GetComponentsInChildren<SpriteRenderer>(true))
                        if (previous != renderer) previous.enabled = false;
                    var serialized = new SerializedObject(root.GetComponent<RobotView>());
                    serialized.FindProperty("_iconRenderer").objectReferenceValue = renderer;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("[Robot Icons] 8 种盘面机器人已改用配置对应的 Icon，占格和碰撞数据保持原样。");
        }

        private static Rect OpaqueBounds(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("无法读取 Icon：" + path);
                var pixels = texture.GetPixels32();
                var minX = texture.width;
                var minY = texture.height;
                var maxX = -1;
                var maxY = -1;
                for (var y = 0; y < texture.height; y++)
                    for (var x = 0; x < texture.width; x++)
                    {
                        if (pixels[y * texture.width + x].a <= 16) continue;
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
                if (maxX < minX) throw new InvalidOperationException("Icon 没有可见内容：" + path);
                return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
