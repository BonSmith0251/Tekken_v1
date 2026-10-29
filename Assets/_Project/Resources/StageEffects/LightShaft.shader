// Light shafts of the original stages (BP_LightBeam god rays), rebuilt by MuseumStageImporter.AddBeam
// as two crossed quads. M_GodRay_* is unlit and additive and fades the shaft near the camera
// (its "fadedistance"); this does the same, and also fades each quad seen edge-on so the crossed
// planes don't show as lines. Not reproduced: the original's depth fade against geometry and its
// animated noise.
Shader "Remaster/StageLightShaft"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Colour x brightness", Color) = (1, 1, 1, 1)
        _FadeDistance ("Camera fade distance (m)", Float) = 20
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _FadeDistance, _CascadeExposure;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float fade : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 view = UnityObjectToViewPos(v.vertex.xyz);
                o.pos = mul(UNITY_MATRIX_P, float4(view, 1));
                o.uv = v.uv;
                float3 n = normalize(mul((float3x3)UNITY_MATRIX_MV, v.normal));
                float facing = abs(dot(n, normalize(-view)));
                o.fade = saturate(length(view) / max(_FadeDistance, .01)) * smoothstep(.05, .5, facing);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float mask = tex2D(_MainTex, i.uv).r;
                float exposure = _CascadeExposure > 0 ? _CascadeExposure : 1;
                return float4(_Color.rgb * mask * i.fade * exposure, 0);
            }
            ENDCG
        }
    }
}
