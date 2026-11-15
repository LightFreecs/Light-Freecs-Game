// LightMMORPG/AuraAdditive — unlit additive fresnel glow (aura shells, beam core, projectiles, impacts).
// HDR color feeds Bloom. Optional procedural scrolling noise (no textures). SRP-batcher compatible.
Shader "LightMMORPG/AuraAdditive"
{
    Properties
    {
        [HDR] _Color ("Glow Color (HDR)", Color) = (1.5,1.1,0.3,1)
        _Intensity ("Intensity", Range(0,10)) = 1
        _FresnelPower ("Fresnel Power", Range(0.1,8)) = 2
        _FresnelStrength ("Fresnel Strength", Range(0,4)) = 1
        _CoreStrength ("Core Strength (center fill)", Range(0,2)) = 0.1
        _NoiseScale ("Noise Scale", Range(0.1,20)) = 4
        _NoiseScroll ("Noise Scroll (xyz, object space/s)", Vector) = (0,1.5,0,0)
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "AuraForward"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _FresnelPower;
                half _FresnelStrength;
                half _CoreStrength;
                float _NoiseScale;
                float4 _NoiseScroll;
                half _NoiseStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float3 noisePos   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.noisePos = input.positionOS.xyz * _NoiseScale - _NoiseScroll.xyz * _Time.y;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fres = pow(1.0h - saturate(abs(dot(n, v))), _FresnelPower);
                half glow = fres * _FresnelStrength + _CoreStrength;
                half noise = ValueNoise(input.noisePos) * 0.65 + ValueNoise(input.noisePos * 2.13) * 0.35;
                glow *= lerp(1.0h, noise * 2.0h, _NoiseStrength);
                return half4(_Color.rgb * (glow * _Intensity * _Color.a), 0);
            }
            ENDHLSL
        }
    }
}
