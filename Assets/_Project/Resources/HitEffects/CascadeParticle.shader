// Unlit particle shader for the converted Tekken 7 battle effects (HDRP draws SRPDefaultUnlit
// passes in its forward pass). Colour comes from the particle vertex colour times one texture:
// textures with real alpha give coverage in alpha, alpha-less grey masks give it in red.
// _Intensity carries the source emitter's HDR colour peak and material glare (both compressed in
// CascadeEffects.MaterialFor); the fragment's per-channel shoulder stands in for the original tonemap.
Shader "Remaster/CascadeParticle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
        _MaskFromRed ("Coverage from red", Float) = 0
        _MaskKeepsColour ("Alpha-less: coverage from the brightest channel, hue kept", Float) = 0
        _Scroll ("UV scroll (per second)", Vector) = (0, 0, 0, 0)
        _Tiling ("UV tiling", Vector) = (1, 1, 0, 0)
        _CameraOffset ("Offset toward the camera (m)", Float) = 0
        _AlphaClip ("Alpha clip (erosion)", Float) = 0
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _SubUVGrid ("Source atlas: columns, rows, blend, enabled", Vector) = (1, 1, 0, 0)
        // Premultiplied: One/OneMinusSrcAlpha for translucent emitters, One/One for additive ones.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity, _MaskFromRed, _MaskKeepsColour, _CameraOffset, _CascadeExposure, _AlphaClip;
            float4 _Scroll, _Tiling, _Tint, _SubUVGrid;

            // Custom1.x is AlphaClip (z), Custom2.x the absolute source atlas frame (w).
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 view = UnityObjectToViewPos(v.vertex.xyz);
                // Cascade CameraOffset: draw the particle nearer the camera so it is not hidden in a body.
                view += normalize(-view) * _CameraOffset;
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1));
                o.color = v.color;
                o.uv = float4(v.uv.xy * _Tiling.xy + _Scroll.xy * _Time.y, v.uv.zw);
                return o;
            }

            float2 AtlasUV(float2 uv, float frame)
            {
                // Cooked SubUV tiles progress left-to-right from the texture's top row.
                float2 grid = max(_SubUVGrid.xy, 1);
                float column = fmod(frame, grid.x);
                float row = grid.y - 1 - floor(frame / grid.x);
                return (uv + float2(column, row)) / grid;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv.xy);
                if (_SubUVGrid.w > .5)
                {
                    float lastFrame = _SubUVGrid.x * _SubUVGrid.y - 1;
                    float index = clamp(i.uv.w, 0, lastFrame);
                    float frame = floor(index);
                    float4 first = tex2D(_MainTex, AtlasUV(i.uv.xy, frame));
                    float4 next = tex2D(_MainTex, AtlasUV(i.uv.xy, min(frame + 1, lastFrame)));
                    t = lerp(first, next, frac(index) * _SubUVGrid.z);
                }
                // Alpha-less colour textures (character libraries only): the brightest channel is
                // coverage and the colour is the texture's hue. A grey mask gives the red rule exactly.
                float peak = max(t.r, max(t.g, t.b));
                bool keepColour = _MaskFromRed > .5 && _MaskKeepsColour > .5;
                float coverage = _MaskFromRed > .5 ? (keepColour ? peak : t.r) : t.a;
                // Inferred from the source materials' AlphaClip parameters (their maths nodes are stripped
                // in the cooked game, so these are fitted to the captures, not recovered):
                // the spark material's static "Alpha Clip" sharpens to the bright core. Sparks are 1-1.5 cm
                // wide, a few pixels at fighting distance; there the mip-averaged coverage never clears the
                // clip and the spray vanished, so the clip eases off below ~6 pixels across (none at 3).
                float pixelSpan = max(fwidth(i.uv.x), fwidth(i.uv.y));
                float clipAmount = _AlphaClip * saturate(2 - pixelSpan * 6);
                coverage = saturate(coverage * (1 + clipAmount) - clipAmount);
                // and the distortion material's per-particle AlphaClip erodes as a power over the
                // particle's life (floor rings 1 -> 5, aura 0 -> 20).
                coverage = pow(coverage, max(1, i.uv.z));
                float3 rgb = keepColour ? t.rgb / max(peak, 1e-4) : _MaskFromRed > .5 ? 1 : t.rgb;
                float exposure = _CascadeExposure > 0 ? _CascadeExposure : 1;
                float alpha = saturate(i.color.a * coverage);
                // Premultiplied output serves both blend modes. The original rendered these colours far
                // above 1 and its tonemap brought them back per channel, so a hot orange turns yellow
                // (red saturates first, then green) instead of clipping to white. The same shoulder here:
                // linear up to .35, then an exponential approach to 1, per channel. The output stays
                // within 1, so the display clip and the default HDRP bloom (threshold 0) can't whiten it.
                float3 c = rgb * _Tint.rgb * i.color.rgb * _Intensity * exposure * alpha;
                const float toe = .35;
                float3 shoulder = toe + (1 - toe) * (1 - exp(-(c - toe) / (1 - toe)));
                c = lerp(c, shoulder, step(toe, c));
                return float4(c, alpha);
            }
            ENDCG
        }
    }
}
