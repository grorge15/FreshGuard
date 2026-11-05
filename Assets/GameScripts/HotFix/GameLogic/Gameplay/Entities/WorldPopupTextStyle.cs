using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>共用飘字渐变与描边，字体图集重建时同步采样边界。</summary>
    internal sealed class WorldPopupTextStyle : IDisposable
    {
        private const string GLYPHS = "0123456789Comb";
        private readonly TextMesh _label;
        private readonly MeshRenderer _renderer;
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();
        private readonly Vector4[] _glyphBounds = new Vector4[GLYPHS.Length];
        private readonly Material _ownedMaterial;

        internal WorldPopupTextStyle(TextMesh label, Material material, Color top, Color bottom,
            float faceDilation = 0f, float outlineWidth = 1.5f)
        {
            _label = label;
            _renderer = label.GetComponent<MeshRenderer>();
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (material == null)
            {
                var shader = Shader.Find("FreshGuard/ComboOutlinedText");
                if (shader == null) { _renderer.sharedMaterial = label.font.material; return; }
                material = _ownedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            _renderer.sharedMaterial = material;
            _properties.SetFloat("_GradientEnabled", 1f);
            _properties.SetColor("_GradientTop", top);
            _properties.SetColor("_GradientBottom", bottom);
            _properties.SetFloat("_FaceDilation", faceDilation);
            _properties.SetFloat("_OutlineWidth", outlineWidth);
            _properties.SetColor("_OutlineColor", Color.black);
            label.font.RequestCharactersInTexture(GLYPHS, label.fontSize, label.fontStyle);
            RefreshFontTexture(label.font);
            Font.textureRebuilt += RefreshFontTexture;
        }

        private void RefreshFontTexture(Font font)
        {
            if (_label == null || _renderer == null || font == null || font != _label.font) return;
            var texture = font.material.mainTexture;
            if (texture == null) return;
            _properties.SetTexture("_MainTex", texture);
            _properties.SetVector("_MainTex_TexelSize", new Vector4(1f / texture.width, 1f / texture.height,
                texture.width, texture.height));
            for (var i = 0; i < GLYPHS.Length; i++)
            {
                if (!font.GetCharacterInfo(GLYPHS[i], out var glyph, _label.fontSize, _label.fontStyle))
                {
                    _glyphBounds[i] = new Vector4(2f, 2f, 2f, 2f);
                    continue;
                }
                var minimum = Vector2.Min(Vector2.Min(glyph.uvBottomLeft, glyph.uvBottomRight),
                    Vector2.Min(glyph.uvTopLeft, glyph.uvTopRight));
                var maximum = Vector2.Max(Vector2.Max(glyph.uvBottomLeft, glyph.uvBottomRight),
                    Vector2.Max(glyph.uvTopLeft, glyph.uvTopRight));
                _glyphBounds[i] = new Vector4(minimum.x, minimum.y, maximum.x, maximum.y);
            }
            _properties.SetVectorArray("_GlyphBounds", _glyphBounds);
            var bounds = _renderer.localBounds;
            _properties.SetVector("_GradientRange", new Vector4(bounds.min.y, bounds.max.y, 0f, 0f));
            _renderer.SetPropertyBlock(_properties);
        }

        public void Dispose()
        {
            Font.textureRebuilt -= RefreshFontTexture;
            if (_ownedMaterial == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_ownedMaterial);
            else UnityEngine.Object.DestroyImmediate(_ownedMaterial);
        }
    }
}
