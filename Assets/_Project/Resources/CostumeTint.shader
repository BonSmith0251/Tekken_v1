// Tekken costume colorization, as prepare_jin_costume_materials.py bakes it: the mask's RGB pick
// Color01..03 and the remainder Color04, and mask alpha blends the tint over the untinted map.
// Used with Graphics.Blit into a costume material's base color map; colors are linear.
Shader "Hidden/Remaster/CostumeTint"
{
    Properties
    {
        _MainTex ("Untinted diffuse (sRGB)", 2D) = "white" {}
        _MaskTex ("Color mask (linear)", 2D) = "black" {}
        _Color1 ("Color01", Color) = (1,1,1,1)
        _Color2 ("Color02", Color) = (1,1,1,1)
        _Color3 ("Color03", Color) = (1,1,1,1)
        _Color4 ("Color04", Color) = (1,1,1,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _MaskTex;
            float4 _Color1, _Color2, _Color3, _Color4;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 diffuse = tex2D(_MainTex, i.uv);
                float4 mask = tex2D(_MaskTex, i.uv);
                float4 weights = float4(mask.rgb, max(0, 1 - dot(mask.rgb, 1)));
                weights /= max(dot(weights, 1), 1e-6);
                float3 tint = weights.x * _Color1.rgb + weights.y * _Color2.rgb + weights.z * _Color3.rgb + weights.w * _Color4.rgb;
                return float4(diffuse.rgb * (1 - mask.a + mask.a * tint), diffuse.a);
            }
            ENDCG
        }
    }
}
