Shader "FreshGuard/ComboOutlinedText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width In Font Pixels", Range(0, 3)) = 1.5
        _FaceDilation ("Extra Font Weight In Pixels", Range(0, 1)) = 0
        _GradientEnabled ("Use Vertical Gradient", Float) = 0
        _GradientTop ("Gradient Top", Color) = (1, 1, 1, 1)
        _GradientBottom ("Gradient Bottom", Color) = (1, 0.3, 0.1, 1)
        _GradientRange ("Text Local Height Range", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Lighting Off
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _FaceDilation;
            float _GradientEnabled;
            fixed4 _GradientTop;
            fixed4 _GradientBottom;
            float4 _GradientRange;
            float4 _GlyphBounds[14];

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float localY : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                output.localY = input.vertex.y;
                return output;
            }

            fixed SampleGlyph(float2 uv, float4 bounds)
            {
                float2 inside = step(bounds.xy, uv) * step(uv, bounds.zw);
                float2 halfTexel = abs(_MainTex_TexelSize.xy) * 0.5;
                return tex2D(_MainTex, clamp(uv, bounds.xy + halfTexel, bounds.zw - halfTexel)).a * inside.x * inside.y;
            }

            fixed ExpandedGlyph(float2 uv, float4 bounds, float width)
            {
                float2 offset = abs(_MainTex_TexelSize.xy) * width;
                float2 diagonal = offset * 0.70710678;
                fixed coverage = SampleGlyph(uv, bounds);
                coverage = max(coverage, SampleGlyph(uv + float2(offset.x, 0), bounds));
                coverage = max(coverage, SampleGlyph(uv - float2(offset.x, 0), bounds));
                coverage = max(coverage, SampleGlyph(uv + float2(0, offset.y), bounds));
                coverage = max(coverage, SampleGlyph(uv - float2(0, offset.y), bounds));
                coverage = max(coverage, SampleGlyph(uv + diagonal, bounds));
                coverage = max(coverage, SampleGlyph(uv - diagonal, bounds));
                coverage = max(coverage, SampleGlyph(uv + float2(diagonal.x, -diagonal.y), bounds));
                coverage = max(coverage, SampleGlyph(uv + float2(-diagonal.x, diagonal.y), bounds));
                return coverage;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                // 限制采样范围，避免描边读到字体图集中相邻字形的像素。
                float4 bounds = float4(0, 0, 1, 1);
                [unroll]
                for (int index = 0; index < 14; index++)
                {
                    float4 candidate = _GlyphBounds[index];
                    if (all(input.uv >= candidate.xy) && all(input.uv <= candidate.zw)) bounds = candidate;
                }
                fixed fill = SampleGlyph(input.uv, bounds);
                if (_FaceDilation > 0.001) fill = ExpandedGlyph(input.uv, bounds, _FaceDilation);
                fixed coverage = max(fill, ExpandedGlyph(input.uv, bounds, _OutlineWidth + _FaceDilation));
                float height = saturate((input.localY - _GradientRange.x) / max(0.0001, _GradientRange.y - _GradientRange.x));
                fixed3 fillColor = lerp(input.color.rgb, lerp(_GradientBottom.rgb, _GradientTop.rgb, height), _GradientEnabled);

                // 先合成填色与描边，再统一乘透明度；黑色描边不会在淡出时透入正文。
                fixed3 color = fillColor * fill + _OutlineColor.rgb * (coverage - fill);
                return fixed4(color * input.color.a, coverage * input.color.a);
            }
            ENDCG
        }
    }
}
