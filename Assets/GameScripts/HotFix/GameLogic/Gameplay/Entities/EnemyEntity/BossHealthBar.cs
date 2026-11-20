using UnityEngine;

namespace GameLogic
{
    // World renderers participate in the same two-camera visibility rules as their owner.
    internal sealed class BossHealthBar
    {
        private readonly Transform _root;
        private readonly Mesh _frameMesh, _fillMesh;
        private readonly MeshRenderer _fillRenderer;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector2[] _uv = new Vector2[4];
        private const float ArtWidth = 2105f;
        private const float ArtworkScale = 1.155f * 1.15f;
        // Keep the crest and end cap at their native proportions; shorten only the well.
        private const float WellLeft = 331f, SourceWellWidth = 1698f, DisplayWellWidth = 1138f;
        private const float DisplayWidth = ArtWidth - SourceWellWidth + DisplayWellWidth;
        private const float LabelScale = 0.95f;
        private readonly MeshRenderer _barRenderer, _textRenderer;
        private readonly TextMesh _text;
        private readonly TextMesh[] _outline = new TextMesh[8];
        private readonly MeshRenderer[] _outlineRenderers = new MeshRenderer[8];
        private int _hp = -1;

        internal BossHealthBar(Transform owner, Material spriteMaterial, Texture2D frame, Texture2D fill)
        {
            var go = new GameObject("BossHealthBar", typeof(MeshFilter), typeof(MeshRenderer));
            _root = go.transform;
            _frameMesh = new Mesh { name = "Boss HP frame" };
            go.GetComponent<MeshFilter>().sharedMesh = _frameMesh;
            _barRenderer = go.GetComponent<MeshRenderer>();
            SetArtwork(_barRenderer, spriteMaterial, frame);
            BuildFrame();
            var strip = new GameObject("Fill", typeof(MeshFilter), typeof(MeshRenderer));
            strip.transform.SetParent(_root, false);
            _fillMesh = new Mesh { name = "Boss HP fill" };
            _fillMesh.MarkDynamic();
            strip.GetComponent<MeshFilter>().sharedMesh = _fillMesh;
            _fillRenderer = strip.GetComponent<MeshRenderer>();
            SetArtwork(_fillRenderer, spriteMaterial, fill);
            _text = new GameObject("CurrentHP", typeof(TextMesh)).GetComponent<TextMesh>();
            _text.transform.SetParent(_root, false);
            _text.transform.localPosition = Vector3.up * 0.161f;
            _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontStyle = FontStyle.Bold;
            _text.fontSize = 48;
            _text.characterSize = 0.0442f;
            _text.color = Color.white;
            _textRenderer = _text.GetComponent<MeshRenderer>();
            _textRenderer.sharedMaterial = _text.font.material;
            // Offset whole glyphs: an outline sampled inside a TextMesh quad clips at its edges.
            for (int i = 0; i < _outline.Length; i++)
            {
                var shadow = new GameObject("Outline").AddComponent<TextMesh>();
                shadow.transform.SetParent(_text.transform, false);
                float angle = 2f * Mathf.PI * i / _outline.Length;
                shadow.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.018f;
                shadow.font = _text.font;
                shadow.anchor = _text.anchor;
                shadow.alignment = _text.alignment;
                shadow.fontStyle = _text.fontStyle;
                shadow.fontSize = _text.fontSize;
                shadow.characterSize = _text.characterSize;
                shadow.color = Color.black;
                _outline[i] = shadow;
                _outlineRenderers[i] = shadow.GetComponent<MeshRenderer>();
                _outlineRenderers[i].sharedMaterial = _text.font.material;
            }
            // Attach the completed subtree so the side-camera registry sees both renderers.
            _root.SetParent(owner, false);
        }

        internal void Bind(Bounds body, EnemyEntity enemy, int layer, int order)
        {
            _root.gameObject.SetActive(true);
            float width = body.size.x * 0.6192f;
            var scale = _root.parent.lossyScale;
            _root.localScale = new Vector3(width / Mathf.Abs(scale.x), width / Mathf.Abs(scale.y), 1f);
            _root.position = new Vector3(body.center.x, body.min.y + body.size.x * 0.01f, body.center.z);
            _hp = -1;
            Refresh(enemy.CurrentHp, enemy.MaxHp);
            SetSorting(layer, order);
        }

        internal void Refresh(int hp, int maxHp)
        {
            if (hp == _hp || !_root.gameObject.activeSelf) return;
            _hp = hp;
            _text.text = hp.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var shadow in _outline) shadow.text = _text.text;
            _text.transform.localScale = Vector3.one * (LabelScale * Mathf.Min(1f, 0.94f / Mathf.Max(0.01f, _textRenderer.localBounds.size.x)));
            float ratio = Mathf.Clamp01((float)hp / maxHp);
            // Source artwork places the fill at (331,122) in a 2105 x 293 top-left canvas.
            WriteQuad(_fillMesh, (WellLeft - DisplayWidth / 2f) / ArtWidth, (146.5f - 122f) / ArtWidth,
                DisplayWellWidth / ArtWidth, 119f / ArtWidth, ratio);
        }

        private void BuildFrame()
        {
            float[] source = { 0, WellLeft, WellLeft + SourceWellWidth, ArtWidth };
            float[] display = { 0, WellLeft, WellLeft + DisplayWellWidth, DisplayWidth };
            var vertices = new Vector3[8];
            var uvs = new Vector2[8];
            var colors = new Color[8];
            float unit = ArtworkScale / ArtWidth;
            for (int i = 0; i < 4; i++)
            {
                float x = (display[i] - DisplayWidth / 2f) * unit;
                vertices[2*i] = new Vector3(x, 146.5f * unit);
                vertices[2*i+1] = new Vector3(x, -146.5f * unit);
                uvs[2*i] = new Vector2(source[i] / ArtWidth, 1f);
                uvs[2*i+1] = new Vector2(source[i] / ArtWidth, 0f);
                colors[2*i] = colors[2*i+1] = Color.white;
            }
            _frameMesh.vertices = vertices;
            _frameMesh.uv = uvs;
            _frameMesh.colors = colors;
            _frameMesh.triangles = new[] { 0,2,3, 0,3,1, 2,4,5, 2,5,3, 4,6,7, 4,7,5 };
        }

        private static void SetArtwork(MeshRenderer renderer, Material material, Texture texture)
        {
            renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", texture);
            renderer.SetPropertyBlock(block);
        }

        private void WriteQuad(Mesh mesh, float left, float top, float width, float height, float fraction)
        {
            left *= ArtworkScale;
            top *= ArtworkScale;
            width *= ArtworkScale;
            height *= ArtworkScale;
            float right = left + width * fraction;
            _corners[0] = new Vector3(left, top);
            _corners[1] = new Vector3(right, top);
            _corners[2] = new Vector3(right, top - height);
            _corners[3] = new Vector3(left, top - height);
            _uv[0] = Vector2.up;
            _uv[1] = new Vector2(fraction, 1);
            _uv[2] = new Vector2(fraction, 0);
            _uv[3] = Vector2.zero;
            bool first = mesh.vertexCount == 0;
            mesh.vertices = _corners;
            mesh.uv = _uv;
            if (first)
            {
                mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            }
            mesh.RecalculateBounds();
        }

        internal void SetSorting(int layer, int order)
        {
            _barRenderer.sortingLayerID = _fillRenderer.sortingLayerID = _textRenderer.sortingLayerID = layer;
            _barRenderer.sortingOrder = Mathf.Min(32764, order + 3);
            _fillRenderer.sortingOrder = _barRenderer.sortingOrder + 1;
            foreach (var renderer in _outlineRenderers)
            {
                renderer.sortingLayerID = layer;
                renderer.sortingOrder = _fillRenderer.sortingOrder + 1;
            }
            _textRenderer.sortingOrder = _fillRenderer.sortingOrder + 2;
        }

        internal void Hide() => _root.gameObject.SetActive(false);
        internal void Dispose()
        {
            if (Application.isPlaying) { Object.Destroy(_frameMesh); Object.Destroy(_fillMesh); }
            else { Object.DestroyImmediate(_frameMesh); Object.DestroyImmediate(_fillMesh); }
        }
    }
}
