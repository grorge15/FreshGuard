using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>按数字实际字形高度着色；放在 Outline 之前，保留原描边和透明度动画。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class CountdownTextGradient : BaseMeshEffect
    {
        [SerializeField] private Color _topColor = new Color32(255, 248, 196, 255);
        [SerializeField] private Color _bottomColor = new Color32(255, 151, 39, 255);

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || vertices.currentVertCount == 0) return;
            var vertex = new UIVertex();
            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            for (var i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                minimum = Mathf.Min(minimum, vertex.position.y);
                maximum = Mathf.Max(maximum, vertex.position.y);
            }
            for (var i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                var color = Color.Lerp(_bottomColor, _topColor, Mathf.InverseLerp(minimum, maximum, vertex.position.y));
                vertex.color = (Color)vertex.color * color;
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
