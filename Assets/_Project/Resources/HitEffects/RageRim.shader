// Rage aura as the original shows it: a saturated red rim glow around the fighter's silhouette
// (TEKKEN 7 2026-10-23 08-04-08.mp4, Devil Jin 58-62 s: rim pixels median sRGB (192, 0, 16), up to
// ~230 red at the edges, fading inward). Drawn additively on a copy of each skinned mesh, pushed out
// a few millimetres along the normal so it neither z-fights the body nor needs its depth.
// The original's own Rage material code is not recovered; colour, power and width are fitted.
Shader "Remaster/RageRim"
{
    Properties
    {
        _Color ("Rim colour (linear)", Color) = (0.8, 0, 0.006, 1)
        _Power ("Rim power", Float) = 3.5
        _Intensity ("Rim intensity", Float) = 2.2
        _Push ("Push along normal (m)", Float) = 0.004
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            float4 _Color;
            float _Power, _Intensity, _Push, _CascadeExposure;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                // View space (HDRP renders camera-relative; view space avoids world-position maths).
                float3 normal = normalize(mul((float3x3)UNITY_MATRIX_V, UnityObjectToWorldNormal(v.normal)));
                float3 view = UnityObjectToViewPos(v.vertex.xyz) + normal * _Push;
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1));
                o.normal = normal;
                o.view = -view;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float facing = saturate(dot(normalize(i.normal), normalize(i.view)));
                float rim = pow(1 - facing, _Power);
                float exposure = _CascadeExposure > 0 ? _CascadeExposure : 1;
                return float4(saturate(_Color.rgb * rim * exposure * _Intensity), 0);
            }
            ENDCG
        }
    }
}
