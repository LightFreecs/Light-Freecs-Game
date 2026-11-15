// Cel-shaded anime cloud (main menu). Opaque, unlit: flat colour bands from a custom light direction with noisy band
// edges (lumpy look), bright highlight bubbles on the small top lumps (vertex colour R), a glowing rim on the underside
// and lower silhouette, and ragged, noise-clipped "wispy" silhouettes (stronger on tails, vertex colour G).
// Colours/direction are set per sky preset by LightMMORPG.Menu.MenuSky.
Shader "LightMMORPG/ToonCloud"
{
    Properties
    {
        _ShadowColor ("Shadow (body)", Color) = (0.56,0.69,0.86,1)
        _MidColor ("Mid", Color) = (0.79,0.87,0.96,1)
        _LitColor ("Lit top", Color) = (1,1,1,1)
        _HighlightColor ("Highlight bubbles", Color) = (1,1,1,1)
        [HDR] _RimColor ("Underside glow (a = strength)", Color) = (1.1,1.05,0.85,1)
        _LightDir ("Light direction (world, towards light)", Vector) = (0.3,0.8,-0.4,0)
        _Band1 ("Shadow→mid threshold", Range(0,1)) = 0.42
        _Band2 ("Mid→lit threshold", Range(0,1)) = 0.66
        _BandNoise ("Band edge noise", Range(0,1)) = 0.22
        _NoiseScale ("Band noise scale", Float) = 0.35
        _RimWidth ("Rim width (fresnel)", Range(0,1)) = 0.32
        _EdgeCut ("Silhouette cut", Range(0.3,1.5)) = 0.95
        _EdgeNoiseScale ("Silhouette noise scale", Float) = 0.6
        _FogAmount ("Fog amount", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ShadowColor, _MidColor, _LitColor, _HighlightColor, _RimColor;
            float4 _LightDir;
            float _Band1, _Band2, _BandNoise, _NoiseScale, _RimWidth, _EdgeCut, _EdgeNoiseScale, _FogAmount;
        CBUFFER_END

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float Noise3(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            float a = lerp(lerp(Hash31(i), Hash31(i + float3(1,0,0)), f.x), lerp(Hash31(i + float3(0,1,0)), Hash31(i + float3(1,1,0)), f.x), f.y);
            float b = lerp(lerp(Hash31(i + float3(0,0,1)), Hash31(i + float3(1,0,1)), f.x), lerp(Hash31(i + float3(0,1,1)), Hash31(i + float3(1,1,1)), f.x), f.y);
            return lerp(a, b, f.z);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = i.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 l = normalize(_LightDir.xyz);

                float fres = 1 - saturate(dot(n, v));
                // ragged silhouette (wisps): noise-modulated fresnel cut, stronger on tails
                float en = Noise3(i.positionWS * _EdgeNoiseScale + float3(_Time.y * 0.04, 0, 0));
                float edge = fres * (1 + i.color.g * 1.3) * (0.6 + 0.8 * en);
                clip(_EdgeCut - edge);

                float bn = Noise3(i.positionOS * _NoiseScale + i.color.b * 7.0);
                float ndl = dot(n, l) * 0.5 + 0.5 + (bn - 0.5) * _BandNoise;
                half3 col = _ShadowColor.rgb;
                col = lerp(col, _MidColor.rgb, smoothstep(_Band1 - 0.01, _Band1 + 0.01, ndl));
                col = lerp(col, _LitColor.rgb, smoothstep(_Band2 - 0.01, _Band2 + 0.01, ndl));
                col = lerp(col, _HighlightColor.rgb, i.color.r * smoothstep(_Band2 - 0.06, _Band2 - 0.04, ndl));

                // glowing underside: down-facing surfaces + a band along the lower silhouette
                float down = saturate(-n.y);
                float rimBand = smoothstep(1 - _RimWidth - 0.01, 1 - _RimWidth + 0.01, fres) * smoothstep(0.02, 0.12, -n.y + 0.05);
                float under = smoothstep(0.55, 0.6, down);
                col = lerp(col, _RimColor.rgb, saturate(max(rimBand, under)) * _RimColor.a);

                col = lerp(col, MixFog(col, i.fog), _FogAmount);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
