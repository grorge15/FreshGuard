using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>从固定世界位置上浮的Combo标识，生命周期由所属球推进和回收。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh))]
    public sealed class ComboPopupView : MonoBehaviour
    {
        private const string COMBO_GLYPHS = "012345Comb";
        [SerializeField, Min(0.01f)] private float lifetime = 0.5f;
        [SerializeField, Min(0f)] private float riseDistance = 0.35f;
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private Color[] colors = {
            Color.white, new Color(0.37f, 0.78f, 0.42f), new Color(0.28f, 0.57f, 0.95f),
            new Color(0.70f, 0.39f, 0.88f), new Color(1f, 0.76f, 0.20f) };
        private TextMesh _label;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _materialProperties;
        private readonly Vector4[] _glyphBounds = new Vector4[COMBO_GLYPHS.Length];
        private Vector3 _origin;
        private Color _color;
        private float _elapsed;
        private bool _initialized;

        public void Initialize(int combo, Vector3 position)
        {
            if (combo < 10 || combo > 50 || combo % 10 != 0) throw new ArgumentOutOfRangeException(nameof(combo));
            if (colors == null || colors.Length != 5 || lifetime <= 0f || float.IsNaN(lifetime) ||
                float.IsInfinity(lifetime) || riseDistance < 0f || float.IsNaN(riseDistance) || float.IsInfinity(riseDistance))
                throw new InvalidOperationException("Combo提示参数无效。");
            _label = GetComponent<TextMesh>();
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.fontSize = 32;
            _label.characterSize = 0.08f;
            _renderer = GetComponent<MeshRenderer>();
            _renderer.sortingOrder = 1000;
            _label.text = combo + " Combo";
            ConfigureOutline();
            _color = colors[combo / 10 - 1];
            _origin = position;
            _elapsed = 0f;
            _initialized = true;
            Apply(0f);
        }

        private void ConfigureOutline()
        {
            if (outlineMaterial == null) return;
            // 文字与描边在同一次绘制中合成，再整体应用透明度，避免半透明正文透出底层黑色。
            _renderer.sharedMaterial = outlineMaterial;
            _label.font.RequestCharactersInTexture(COMBO_GLYPHS, _label.fontSize, _label.fontStyle);
            RefreshFontTexture(_label.font);
            Font.textureRebuilt -= RefreshFontTexture;
            Font.textureRebuilt += RefreshFontTexture;
        }

        private void RefreshFontTexture(Font font)
        {
            if (_label == null || font == null || font != _label.font) return;
            if (_materialProperties == null) _materialProperties = new MaterialPropertyBlock();
            var texture = font.material.mainTexture;
            _materialProperties.SetTexture("_MainTex", texture);
            _materialProperties.SetVector("_MainTex_TexelSize", new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height));
            for (var i = 0; i < COMBO_GLYPHS.Length; i++)
            {
                if (!font.GetCharacterInfo(COMBO_GLYPHS[i], out var glyph, _label.fontSize, _label.fontStyle))
                {
                    _glyphBounds[i] = new Vector4(2f, 2f, 2f, 2f);
                    continue;
                }
                var minimum = Vector2.Min(Vector2.Min(glyph.uvBottomLeft, glyph.uvBottomRight), Vector2.Min(glyph.uvTopLeft, glyph.uvTopRight));
                var maximum = Vector2.Max(Vector2.Max(glyph.uvBottomLeft, glyph.uvBottomRight), Vector2.Max(glyph.uvTopLeft, glyph.uvTopRight));
                _glyphBounds[i] = new Vector4(minimum.x, minimum.y, maximum.x, maximum.y);
            }
            _materialProperties.SetVectorArray("_GlyphBounds", _glyphBounds);
            _renderer.SetPropertyBlock(_materialProperties);
        }

        private void OnDestroy() => Font.textureRebuilt -= RefreshFontTexture;

        public bool Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!_initialized) return true;
            _elapsed = Mathf.Min(lifetime, _elapsed + deltaTime);
            Apply(_elapsed / lifetime);
            return _elapsed >= lifetime;
        }

        private void Apply(float progress)
        {
            transform.position = _origin + Vector3.up * (riseDistance * progress);
            var color = _color;
            color.a *= 1f - progress;
            _label.color = color;
        }
    }
}
