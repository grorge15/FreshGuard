using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FreshGuard.Editor
{
    /// <summary>MiSans 现代黑体主题。字体随 Prefab 打包，运行时不读取操作系统字体。</summary>
    public static class ModernUIFontTheme
    {
        public const string RegularPath = "Assets/AssetRaw/Fonts/MiSans-UI-Regular.ttf";
        public const string SemiboldPath = "Assets/AssetRaw/Fonts/MiSans-UI-Semibold.ttf";
        public static readonly Color ShopPriceColor = new Color(0.23f, 0.16f, 0.12f);

        public static Font LoadFont(bool emphasis)
        {
            var path = emphasis ? SemiboldPath : RegularPath;
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null) throw new InvalidOperationException("缺少 MiSans 字体：" + path);
            return font;
        }

        public static void Apply(Text text)
        {
            var name = text.name.ToLowerInvariant();
            var body = new[] { "description", "tip", "message", "storage", "placeholder", "countdown" }
                .Any(name.Contains);
            var emphasis = !body && (text.font == LoadFont(true) || text.fontStyle == FontStyle.Bold ||
                text.fontStyle == FontStyle.BoldAndItalic || text.GetComponentInParent<Button>(true) != null ||
                new[] { "title", "name", "price", "amount", "timer", "elapsedtime", "troph", "cup", "wave", "coin", "energy", "level", "layers" }
                    .Any(name.Contains));
            Apply(text, emphasis);
        }

        public static void Apply(Text text, bool emphasis)
        {
            text.font = LoadFont(emphasis);
            text.fontStyle = FontStyle.Normal;
            // MiSans 行高约为字号的 1.33 倍，原系统字体的小框会把整行裁掉。
            // 只扩展固定高度的单行文字框，保留锚点、位置、宽度及多行正文布局。
            var rect = text.rectTransform;
            var name = text.name.ToLowerInvariant();
            if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y) && rect.sizeDelta.y > 0f &&
                !text.text.Contains("\n") && !name.Contains("description") && !name.Contains("tip") && !name.Contains("message"))
            {
                var minimumHeight = Mathf.Ceil(text.fontSize * 1.4f) + 2f;
                if (rect.sizeDelta.y < minimumHeight)
                    rect.sizeDelta = new Vector2(rect.sizeDelta.x, minimumHeight);
            }
            // 字重来自字体自身，避免再次合成粗体；只在复杂背景保留轻描边。
            foreach (var shadow in text.GetComponents<Shadow>())
            {
                var distance = shadow.effectDistance;
                shadow.effectDistance = new Vector2(Mathf.Clamp(distance.x, -0.75f, 0.75f),
                    Mathf.Clamp(distance.y, -0.75f, 0.75f));
            }
            if (text.name == "m_text_Price" && text.GetComponentInParent<GameLogic.ShopPointerInput>(true) != null)
            {
                text.color = ShopPriceColor;
                foreach (var shadow in text.GetComponents<Shadow>()) shadow.enabled = false;
            }
        }

        [MenuItem("FreshGuard/Typography/Apply Modern Sans (MiSans)")]
        public static void ApplyToAllUIPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成后应用字体。");
            LoadFont(false);
            LoadFont(true);
            // 先保存被嵌套的小部件，让主窗口继承其字体，减少重复覆盖。
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AssetRaw/UI" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => AssetDatabase.GetDependencies(path).Count(item => item.EndsWith(".prefab")))
                .ThenBy(path => path).ToArray();
            var changed = 0;
            foreach (var path in paths)
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var labels = root.GetComponentsInChildren<Text>(true);
                if (labels.Length == 0) continue;
                foreach (var label in labels)
                {
                    Apply(label);
                    EditorUtility.SetDirty(label);
                    EditorUtility.SetDirty(label.rectTransform);
                    foreach (var shadow in label.GetComponents<Shadow>()) EditorUtility.SetDirty(shadow);
                }
                PrefabUtility.SavePrefabAsset(root);
                changed++;
            }
            foreach (var path in new[]
            {
                "Assets/AssetRaw/Actor/DamageNumbers/Prefabs/DamageNumber.prefab",
                "Assets/AssetRaw/Physics/Prefabs/ComboPopup.prefab"
            })
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                var text = root.GetComponent<TextMesh>();
                text.font = LoadFont(true);
                text.fontStyle = FontStyle.Normal;
                root.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
                EditorUtility.SetDirty(text);
                EditorUtility.SetDirty(root.GetComponent<MeshRenderer>());
                PrefabUtility.SavePrefabAsset(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Typography] 已为 " + changed + " 个 UI Prefab 应用 MiSans 现代黑体。");
        }
    }
}
