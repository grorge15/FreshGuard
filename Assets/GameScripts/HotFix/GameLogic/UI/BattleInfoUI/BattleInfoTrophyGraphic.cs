using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>无独立美术素材时使用的可缩放奖杯图形，保持 UI 射线穿透。</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BattleInfoTrophyGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            Quad(mesh, rect, 0.12f, 0.50f, 0.25f, 0.82f);
            Quad(mesh, rect, 0.12f, 0.75f, 0.35f, 0.82f);
            Quad(mesh, rect, 0.16f, 0.43f, 0.34f, 0.52f);
            Quad(mesh, rect, 0.75f, 0.50f, 0.88f, 0.82f);
            Quad(mesh, rect, 0.65f, 0.75f, 0.88f, 0.82f);
            Quad(mesh, rect, 0.66f, 0.43f, 0.84f, 0.52f);
            Quad(mesh, rect, 0.44f, 0.16f, 0.56f, 0.46f);
            Quad(mesh, rect, 0.29f, 0.06f, 0.71f, 0.18f);
            var first = mesh.currentVertCount;
            Vertex(mesh, rect, 0.23f, 0.89f);
            Vertex(mesh, rect, 0.36f, 0.43f);
            Vertex(mesh, rect, 0.64f, 0.43f);
            Vertex(mesh, rect, 0.77f, 0.89f);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }
        private void Quad(VertexHelper mesh, Rect rect, float left, float bottom, float right, float top)
        {
            var first = mesh.currentVertCount;
            Vertex(mesh, rect, left, bottom);
            Vertex(mesh, rect, left, top);
            Vertex(mesh, rect, right, top);
            Vertex(mesh, rect, right, bottom);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }
        private void Vertex(VertexHelper mesh, Rect rect, float x, float y)
        {
            mesh.AddVert(new Vector3(rect.x + rect.width * x, rect.y + rect.height * y), color, Vector2.zero);
        }
    }
}
