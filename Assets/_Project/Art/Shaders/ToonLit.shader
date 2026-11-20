// LightMMORPG/ToonLit — cel-shaded URP 17 shader.
// 3 bands from the main light (lit / shade / deep shade) with shadows, additional lights folded into the bands,
// SH ambient, rim light, emission, inverted-hull outline (clip-space extrusion = stable pixel width).
// SRP-batcher compatible: every pass shares the same UnityPerMaterial CBUFFER.
Shader "LightMMORPG/ToonLit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color (multiplies base)", Color) = (0.62,0.55,0.75,1)
        _RampThreshold ("Ramp Threshold", Range(-1,1)) = 0.05
        _RampSmooth ("Ramp Smoothness", Range(0.001,0.5)) = 0.02
        _Band2Threshold ("2nd Band Threshold", Range(-1,1)) = -0.45
        _Band2Color ("2nd Band Color (multiplies shade)", Color) = (0.78,0.74,0.88,1)
        _AmbientStrength ("Ambient (SH) Strength", Range(0,2)) = 0.6
        [HDR] _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,16)) = 5
        _RimStrength ("Rim Strength", Range(0,2)) = 0.6
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _OutlineColor ("Outline Color", Color) = (0.08,0.06,0.1,1)
        _OutlineWidth ("Outline Width (1/1000 screen height)", Range(0,10)) = 2.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "UniversalMaterialType"="Lit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _RampThreshold;
            half _RampSmooth;
            half _Band2Threshold;
            half4 _Band2Color;
            half _AmbientStrength;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half4 _EmissionColor;
            half4 _OutlineColor;
            float _OutlineWidth;
            // battle damage (InjuryVisuals, per renderer through a MaterialPropertyBlock): _Bruises xyz = spot in the body's
            // bind pose (mesh UV2), w = strength 0..1; _BruiseN xyz = the skin's outward direction there (bind pose), w = a
            // random seed; params x = count, y = smudge radius, z = strength boost (low health), w = height scale
            float4 _Bruises[8];
            float4 _BruiseN[8];
            float4 _BruiseParams;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        // ---------------------------------------------------------------- Forward
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float3 bindPos    : TEXCOORD2;   // fighters: the vertex in the bind pose (bruises stick to the skin)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float3 bindPos    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.bindPos = input.bindPos;
                return o;
            }

            float HatchHash(float n) { return frac(sin(n * 12.9898 + 4.1414) * 43758.5453); }

            // anime damage marks: a little cluster of short parallel strokes (n of them, odd), slanted by ang, in some 2D
            // space q (relative to the cluster's centre); spacing / length / width in the same units. 0..1 coverage.
            float Hatch(float2 q, float ang, float n, float spacing, float len, float width, float seed)
            {
                float c = cos(ang), s = sin(ang);
                q = float2(c * q.x + s * q.y, -s * q.x + c * q.y);
                float h = (n - 1.0) * 0.5;
                float i = clamp(round(q.y / spacing), -h, h);
                float r1 = HatchHash(i + seed * 17.0), r2 = HatchHash(i * 3.7 + seed * 5.0);
                float halfLen = len * (0.32 + 0.22 * r1) * (1.0 - 0.35 * abs(i) / max(h, 1.0));
                float x = q.x - (r2 - 0.5) * len * 0.35;                   // strokes start a little staggered
                float y = q.y - i * spacing - (r1 - 0.5) * spacing * 0.25;
                float t = saturate(1.0 - (x / max(halfLen, 1e-5)) * (x / max(halfLen, 1e-5)));   // tapered at both ends
                float w = width * (0.45 + 0.55 * sqrt(t));
                float aa = max(fwidth(y), 1e-5);
                return smoothstep(w + aa, w - aa, abs(y)) * step(abs(x), halfLen);
            }

            // battle damage at this skin point: x = soft brownish-purple smudge, y = red scratch strokes (anime hatching)
            half2 Damage(float3 bp)
            {
                float sc = max(_BruiseParams.w, 0.2), r = max(_BruiseParams.y, 1e-4), boost = _BruiseParams.z;
                half smudge = 0.0, strokes = 0.0;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float on = step((float)i + 0.5, _BruiseParams.x);
                    float3 d = bp - _Bruises[i].xyz;
                    float3 nrm = normalize(_BruiseN[i].xyz + float3(0, 0, 1e-4));
                    float front = smoothstep(-0.03 * sc, 0.0, dot(d, nrm));   // this side of the limb only
                    float w = saturate(_Bruises[i].w * boost) * on * front;
                    float dist = length(d) / r;
                    smudge = max(smudge, w * 0.8 * saturate(1.0 - dist * dist));
                    // strokes in the skin's tangent plane there, slanting down-across like drawn scratches
                    float3 up = abs(nrm.y) < 0.85 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 t = normalize(cross(up, nrm)), bt = cross(nrm, t);
                    float2 q = float2(dot(d, t), dot(d, bt)) / sc;
                    float seed = _BruiseN[i].w;
                    float ang = -0.75 + (HatchHash(seed * 9.1) - 0.5) * 0.7;
                    float n = w > 0.55 ? 5.0 : 3.0;
                    strokes = max(strokes, saturate(w * 1.6) * Hatch(q, ang, n, 0.018, 0.11, 0.0048, seed));
                    // a second, smaller cluster beside it (the reference art scatters them)
                    float2 off = float2(HatchHash(seed * 3.3) - 0.5, HatchHash(seed * 5.9) - 0.5) * 0.09 + float2(0.05, -0.03);
                    strokes = max(strokes, saturate(w * 1.6 - 0.25) * Hatch(q - off, ang + 0.25, 3.0, 0.016, 0.08, 0.0042, seed + 2.0));
                }
                return half2(saturate(smudge), saturate(strokes));
            }

            // Lit (1) / shade (0) factor for a given N.L, hard edge softened by _RampSmooth.
            half Band(half ndl, half threshold)
            {
                return smoothstep(threshold - _RampSmooth, threshold + _RampSmooth, ndl);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_BruiseParams.x > 0.5)
                {
                    // anime battle damage: a faint brownish-purple smudge with red scratch strokes over it
                    half2 dmg = Damage(input.bindPos);
                    albedo.rgb = lerp(albedo.rgb, albedo.rgb * half3(0.74, 0.55, 0.6), dmg.x);
                    albedo.rgb = lerp(albedo.rgb, half3(0.55, 0.09, 0.08), dmg.y * 0.95);
                }
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.shadowMask = half4(1,1,1,1);

                // --- main light: 3 bands
                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                half ndl = dot(n, mainLight.direction);
                half shadow = smoothstep(0.35h, 0.65h, mainLight.shadowAttenuation);
                half lit = Band(ndl, _RampThreshold) * shadow;
                half deep = 1.0h - Band(ndl, _Band2Threshold);
                half3 shade = albedo.rgb * _ShadeColor.rgb * lerp(half3(1,1,1), _Band2Color.rgb, deep);
                half3 color = lerp(shade, albedo.rgb, lit) * mainLight.color;

                // --- additional lights: same banding, additive
                #if defined(_ADDITIONAL_LIGHTS)
                #if USE_CLUSTER_LIGHT_LOOP
                for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light l = GetAdditionalLight(li, inputData.positionWS, inputData.shadowMask);
                    half a = Band(dot(n, l.direction), _RampThreshold) * l.distanceAttenuation * l.shadowAttenuation;
                    color += albedo.rgb * l.color * a;
                }
                #endif
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                    half a = Band(dot(n, l.direction), _RampThreshold) * saturate(l.distanceAttenuation) * l.shadowAttenuation;
                    color += albedo.rgb * l.color * a;
                LIGHT_LOOP_END
                #endif

                // --- ambient (SH)
                color += albedo.rgb * SampleSH(n) * _AmbientStrength;

                // --- rim (stronger on the lit side so it reads as a backlight edge)
                half fres = pow(1.0h - saturate(dot(n, v)), _RimPower);
                half rim = smoothstep(0.45h, 0.55h, fres) * _RimStrength * lerp(0.35h, 1.0h, lit);
                color += _RimColor.rgb * rim * mainLight.color;

                color += _EmissionColor.rgb;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Outline (inverted hull)
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half   fogFactor  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 posCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(nCS);
                nCS = len > 1e-5 ? nCS / len : float2(0, 0);
                // width in 1/1000 of screen height; x corrected for aspect; * w = constant pixel width at any distance
                float aspect = _ScreenParams.y / _ScreenParams.x;
                float2 offset = nCS * (_OutlineWidth * 0.002);
                offset.x *= aspect;
                posCS.xy += offset * posCS.w;
                o.positionCS = posCS;
                o.fogFactor = ComputeFogFactor(posCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- ShadowCaster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                o.positionCS = ApplyShadowClamping(posCS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // ---------------------------------------------------------------- DepthOnly
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half Frag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        // ---------------------------------------------------------------- DepthNormals (SSAO / depth-normal prepass)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
