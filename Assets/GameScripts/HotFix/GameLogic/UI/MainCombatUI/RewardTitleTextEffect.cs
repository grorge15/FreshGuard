using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    // 按实际字形高度着色，保留原文字透明度及淡出动画。
    [RequireComponent(typeof(Text))]
    [DisallowMultipleComponent]
    public sealed class RewardTitleTextEffect : BaseMeshEffect
    {
        [SerializeField] private Color _topColor = new Color32(255, 248, 196, 255);
        [SerializeField] private Color _bottomColor = new Color32(255, 151, 39, 255);
        private static readonly Color Outline = new Color32(46, 39, 35, 255);
        private static readonly Color Glow = new Color(1f, .66f, .12f, 1f);
        [SerializeField, Range(0f, 4f)] private float outlineWidth = 1.5f;
        [SerializeField, Range(0f, 12f)] private float glowRadius = 6f;
        private readonly List<UIVertex> source = new List<UIVertex>();
        private readonly List<UIVertex> decorated = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive() || helper.currentVertCount == 0) return;
            source.Clear();
            helper.GetUIVertexStream(source);
            decorated.Clear();
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var vertex in source)
            {
                minY = Mathf.Min(minY, vertex.position.y);
                maxY = Mathf.Max(maxY, vertex.position.y);
            }
            // Low-alpha concentric glyph copies give a restrained soft halo.
            if (glowRadius > 0f && source.Count <= 64000 / 33)
            {
                AddRing(glowRadius, Glow, .06f);
                AddRing(glowRadius * .67f, Glow, .10f);
                AddRing(glowRadius * .33f, Glow, .14f);
            }
            if (outlineWidth > 0f && source.Count <= 64000 / 9) AddRing(outlineWidth, Outline, 1f);
            if (source.Count > 64000)
            {
                // 长文本不展开三角形，仍在原有顶点上保留渐变。
                for (int i = 0; i < helper.currentVertCount; i++)
                {
                    UIVertex v = default;
                    helper.PopulateUIVertex(ref v, i);
                    var c = Color.Lerp(_bottomColor, _topColor, Mathf.InverseLerp(minY, maxY, v.position.y));
                    c.a *= v.color.a / 255f;
                    v.color = c;
                    helper.SetUIVertex(v, i);
                }
                return;
            }
            foreach (var original in source)
            {
                var vertex = original;
                var tint = Color.Lerp(_bottomColor, _topColor,
                    Mathf.InverseLerp(minY, maxY, vertex.position.y));
                tint.a *= original.color.a / 255f;
                vertex.color = tint;
                decorated.Add(vertex);
            }
            helper.Clear();
            helper.AddUIVertexTriangleStream(decorated);
        }

        private void AddRing(float radius, Color color, float opacity)
        {
            for (var direction = 0; direction < 8; direction++)
            {
                var angle = direction * Mathf.PI / 4f;
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                foreach (var original in source)
                {
                    var vertex = original;
                    vertex.position += offset;
                    var tint = color;
                    tint.a *= opacity * original.color.a / 255f;
                    vertex.color = tint;
                    decorated.Add(vertex);
                }
            }
        }
    }
}
