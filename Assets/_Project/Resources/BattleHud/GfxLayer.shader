// One layer of a Scaleform clip drawn by GfxClip through Graphics.DrawTexture inside OnGUI:
// the Flash colour transform (multiply + add, applied to gamma-space colour as Flash does),
// normal or additive blending, a clip-layer mask (the letter outline, rasterised by the exporter)
// and the BlurFilter (box blur).
Shader "Hidden/BattleHud/GfxLayer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Mul ("Multiply", Vector) = (1,1,1,1)
        _Add ("Add (0-1)", Vector) = (0,0,0,0)
        _Blur ("Blur (uv)", Vector) = (0,0,0,0)
        _MaskRow0 ("Mask row 0", Vector) = (1,0,0,0)
        _MaskRow1 ("Mask row 1", Vector) = (0,1,0,0)
        _MaskRect ("Mask rect", Vector) = (0,0,0,0)
        _MaskTex ("Mask", 2D) = "white" {}
        _MaskRow2 ("Mask 2 row 0", Vector) = (1,0,0,0)
        _MaskRow3 ("Mask 2 row 1", Vector) = (0,1,0,0)
        _MaskRect2 ("Mask 2 rect", Vector) = (0,0,0,0)
        _MaskTex2 ("Mask 2", 2D) = "white" {}
        _ProjX ("Transform row x", Vector) = (1,0,0,0)
        _ProjY ("Transform row y", Vector) = (0,1,0,0)
        _ProjW ("Transform row w", Vector) = (0,0,0,1)
        _UseMask ("Use mask", Float) = 0
        _SrcBlend ("Src blend", Float) = 1
        _DstBlend ("Dst blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend [_SrcBlend] [_DstBlend]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _MaskTex, _MaskTex2;
            float4 _Mul, _Add, _Blur, _MaskRow0, _MaskRow1, _MaskRect, _MaskRow2, _MaskRow3, _MaskRect2;
            float4 _ProjX, _ProjY, _ProjW;
            float _UseMask;

            float MaskCoverage(sampler2D tex, float4 row0, float4 row1, float4 rect, float3 p)
            {
                float2 m = float2(dot(row0.xyz, p), dot(row1.xyz, p));
                float2 st = (m - rect.xy) / (rect.zw - rect.xy);
                if (any(st < 0) || any(st > 1)) return 0;
                return tex2D(tex, float2(st.x, 1 - st.y)).a;
            }

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                // GUI.matrix only takes TRS matrices, so GfxClip leaves it at identity and passes the layer's
                // full stage transform (2D, or with the 3D tween's perspective) here: image pixel -> screen pixel,
                // homogeneous. Scaling the clip position by w keeps the texture perspective-correct.
                float4 p = float4(v.vertex.xy, 0, 1);
                float3 s = float3(dot(_ProjX, p), dot(_ProjY, p), dot(_ProjW, p));
                o.pos = UnityObjectToClipPos(float3(s.xy / s.z, v.vertex.z)) * s.z;
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float coverage = 1;
                if (_UseMask > 0.5)
                {
                    // Layer pixel (x right, y down) -> mask-local position -> the rasterised mask shape. A sprite
                    // mask is the union of its shapes (up to two here).
                    float3 p = float3(i.uv.x, 1 - i.uv.y, 1);
                    coverage = MaskCoverage(_MaskTex, _MaskRow0, _MaskRow1, _MaskRect, p);
                    if (_UseMask > 1.5) coverage = max(coverage, MaskCoverage(_MaskTex2, _MaskRow2, _MaskRow3, _MaskRect2, p));
                    if (coverage <= 0) discard;
                }
                float4 c;
                if (_Blur.x + _Blur.y > 0)
                {
                    // Flash BlurFilter quality 1 is a box blur blurX x blurY wide.
                    c = 0;
                    [unroll] for (int y = -3; y <= 3; y++)
                        [unroll] for (int x = -3; x <= 3; x++)
                        {
                            float4 s = tex2D(_MainTex, i.uv + float2(x, y) / 6.0 * _Blur.xy);
                            c += float4(s.rgb * s.a, s.a); // average premultiplied colour
                        }
                    c.rgb = c.a > 0 ? c.rgb / c.a : 0;
                    c.a /= 49.0;
                }
                else c = tex2D(_MainTex, i.uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = LinearToGammaSpace(c.rgb);
                #endif
                c = saturate(c * _Mul + _Add);
                c.a *= coverage;
                // Scaleform's source contribution is alpha-weighted in display (gamma) space.
                // Weighting AFTER GammaToLinearSpace makes its 5-8% red echo copies ~25-31%
                // bright in this linear project. Emit premultiplied source colour instead.
                // Destination attenuation remains Unity's linear blend (see BATTLE_HUD.txt).
                c.rgb *= c.a;
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
