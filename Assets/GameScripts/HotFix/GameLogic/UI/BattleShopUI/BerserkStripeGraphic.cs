using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    // Tile in bar coordinates so cropping the countdown never stretches the stripes.
    public sealed class BerserkStripeGraphic : RawImage
    {
        private float _fill, _bolt, _phase, _opacity;

        public void Present(float fill, float bolt, float remainingRatio, float elapsed)
        {
            float opacity = Mathf.Clamp01((remainingRatio - 0.12f) / 0.26f);
            float phase = Mathf.Repeat(elapsed * (10f / 19f), 1f);
            if (_fill == fill && _bolt == bolt && _opacity == opacity && _phase == phase) return;
            _fill = fill;
            _bolt = bolt;
            _opacity = opacity;
            _phase = phase;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            if (r.width <= 0f || r.height <= 0f || _opacity <= 0f) return;
            // The reference tile is 19 x 10 display pixels; the bar inherits a 0.87 X scale.
            float unit = r.height / (10f * 0.87f);
            float end = Mathf.Min(_fill * r.width, _bolt * r.width - 3f * unit);
            if (end <= 0f) return;
            float left = Mathf.Min(3f * unit, end * 3f / 8f);
            float right = Mathf.Max(left, end - 5f * unit);
            float peak = Mathf.Min(1f, end / (8f * unit));
            Column(vh, r, 0f, 0f, unit);
            Column(vh, r, left, peak, unit);
            Column(vh, r, right, peak, unit);
            Column(vh, r, end, 0f, unit);
            for (int i = 0; i < 6; i += 2)
            {
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i + 2, i + 1, i + 3);
            }
        }

        private void Column(VertexHelper vh, Rect r, float x, float opacity, float unit)
        {
            var tint = color;
            tint.a *= opacity * _opacity;
            float u = x / (19f * unit) + _phase;
            vh.AddVert(new Vector3(r.xMin + x, r.yMin), tint, new Vector2(u, 0f));
            vh.AddVert(new Vector3(r.xMin + x, r.yMax), tint, new Vector2(u, 1f));
        }
    }
}
