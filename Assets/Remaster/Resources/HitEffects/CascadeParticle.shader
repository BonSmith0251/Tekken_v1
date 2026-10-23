// Unlit particle shader for the converted Tekken 7 battle effects (HDRP draws SRPDefaultUnlit
// passes in its forward pass). Colour comes from the particle vertex colour times one texture:
// textures with real alpha give coverage in alpha, alpha-less grey masks give it in red.
// _Intensity carries the source emitter's HDR colour peak and material glare; the stage's bloom
// then spreads values above 1, as the original renderer did.
Shader "Remaster/CascadeParticle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
        _MaskFromRed ("Coverage from red", Float) = 0
        _Scroll ("UV scroll (per second)", Vector) = (0, 0, 0, 0)
        _Tiling ("UV tiling", Vector) = (1, 1, 0, 0)
        _CameraOffset ("Offset toward the camera (m)", Float) = 0
        _AlphaClip ("Alpha clip (erosion)", Float) = 0
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
            float _Intensity, _MaskFromRed, _CameraOffset, _CascadeExposure, _AlphaClip;
            float4 _Scroll, _Tiling;

            // TEXCOORD0.z is the particle's AlphaClip over its life (Custom1.x stream), zero when unused.
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 view = UnityObjectToViewPos(v.vertex.xyz);
                // Cascade CameraOffset: draw the particle nearer the camera so it is not hidden in a body.
                view += normalize(-view) * _CameraOffset;
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1));
                o.color = v.color;
                o.uv = float3(v.uv.xy * _Tiling.xy + _Scroll.xy * _Time.y, v.uv.z);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv.xy);
                float coverage = _MaskFromRed > .5 ? t.r : t.a;
                // Inferred from the source materials' AlphaClip parameters (their maths nodes are stripped
                // in the cooked game, so these are fitted to the captures, not recovered):
                // the spark material's static "Alpha Clip" sharpens to the bright core,
                coverage = saturate(coverage * (1 + _AlphaClip) - _AlphaClip);
                // and the distortion material's per-particle AlphaClip erodes as a power over the
                // particle's life (floor rings 1 -> 5, aura 0 -> 20).
                coverage = pow(coverage, max(1, i.uv.z));
                float3 rgb = _MaskFromRed > .5 ? 1 : t.rgb;
                float exposure = _CascadeExposure > 0 ? _CascadeExposure : 1;
                float alpha = saturate(i.color.a * coverage);
                // Premultiplied output serves both blend modes.
                return float4(rgb * i.color.rgb * _Intensity * exposure * alpha, alpha);
            }
            ENDCG
        }
    }
}
