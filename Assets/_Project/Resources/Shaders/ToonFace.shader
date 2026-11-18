// LightMMORPG/ToonFace — the Toon Head's skin (URP 17). Port of the Toon Head design bench.
// * Face shadow from a painted map instead of the mesh: the light's angle around the head picks a threshold, the map
//   (per head shape, ToonHead.ShadowMap) decides where the edge lands — nose shadow first, then a curve over the cheek.
//   Head axes come from the skinned tangent (= the head's right), so the shadow turns with the head.
// * Two tones (lit / warm shade) with a narrow soft edge, SH ambient, faint rim; cast shadows from the world.
// * Painted features (eyes, brows, mouth) from _BaseMap over the front of the face (uv1.w = front weight).
// * Moving face (Brow Bench): irises, blinking / expression lids and brows are drawn here every frame from material values
//   (ToonFaceAnimator), so the face texture is never repainted. Irises and lids stay inside _EyeMask (eye white minus lid
//   lines); the brow is sampled from _BrowTex after undoing the expression's bend and the player's transform per pixel.
// * Inverted-hull outline in the outline colour, thinner across the face (uv1.z = face mask).
// Vertex data: TANGENT.xyz = head right, TEXCOORD0 = face texture uv, TEXCOORD1 = (sin θ, height, face mask, front weight).
// Lives in Resources so builds always include it (materials are created at runtime by ProceduralCharacterBuilder).
Shader "LightMMORPG/ToonFace"
{
    Properties
    {
        [MainTexture] _BaseMap ("Face Features (RGBA)", 2D) = "clear" {}
        _ShadowMap ("Face Shadow Map (R)", 2D) = "gray" {}
        [MainColor] _BaseColor ("Lit Skin", Color) = (1,0.91,0.86,1)
        _ShadeColor ("Shade Skin", Color) = (0.95,0.70,0.65,1)
        _BodyThreshold ("Body Shadow Threshold", Range(-1,1)) = 0.01
        _EdgeSoftness ("Shadow Edge Softness", Range(0.001,0.2)) = 0.04
        _FaceEdgeSoftness ("Face Shadow Edge Softness", Range(0.001,0.1)) = 0.012
        _ShadowV0 ("Shadow Map Bottom (head units)", Float) = -0.05
        _ShadowVSpan ("Shadow Map Height (head units)", Float) = 0.8
        _AmbientStrength ("Ambient (SH) Strength", Range(0,2)) = 0.4
        _FeatureLight ("Feature Light (0 = unlit, 1 = full light colour)", Range(0,1)) = 0.5
        [HDR] _RimColor ("Rim Color", Color) = (1,0.95,0.92,1)
        _RimStrength ("Rim Strength", Range(0,2)) = 0.35
        _OutlineColor ("Outline Color", Color) = (0.61,0.33,0.27,1)
        _OutlineWidth ("Outline Width (1/1000 screen height)", Range(0,10)) = 3
        _FaceOutlineScale ("Outline Width Across the Face", Range(0,1)) = 0.45

        [Header(Moving face)]
        _EyeLive ("Live Eyes (0 = painted)", Float) = 0
        _BrowLive ("Live Brows (0 = painted)", Float) = 0
        [NoScaleOffset] _EyeMask ("Eye Mask (R white, G height in white, B white height)", 2D) = "black" {}
        [NoScaleOffset] _BrowTex ("Brow Map (R coverage, G middle line)", 2D) = "black" {}
        _EyeGeo ("Eye (x, y, width, shift)", Vector) = (0.15,0.38,0.17,0)
        _Iris0 ("Iris (u, v, rx, ry)", Vector) = (0.466,0,0.18,0.232)
        _Iris1 ("Iris (ring, dark top)", Vector) = (0.026,0,0,0)
        _Shine0 ("Shine 1 (u, v, r, on)", Vector) = (0,0,0,0)
        _Shine1 ("Shine 2 (u, v, r, on)", Vector) = (0,0,0,0)
        _IrisColor ("Iris Color", Color) = (0.2,0.25,0.35,1)
        _Look ("Look (right u, v, left u, v)", Vector) = (0,0,0,0)
        _EyeAnim ("Eyes (iris scale, upper lid, lower lid)", Vector) = (1,0,0,0)
        _BrowColor ("Brow Color", Color) = (0.1,0.07,0.07,1)
        _BrowRest ("Brow Rest (mean x, mean y, x0, length)", Vector) = (0.15,0.55,0.05,0.2)
        _BrowMapRange ("Brow Map (s0, s span, d half)", Vector) = (-0.03,1.06,0.03,0)
        _BrowXform ("Brow Transform (dx, dy, cos/size, sin/size)", Vector) = (0,0,1,0)
        _BrowBend0 ("Brow Bend (inner, outer, arch, pinch)", Vector) = (0,0,0,0)
        _BrowBend1 ("Brow Bend (thick, thick inner)", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "UniversalMaterialType"="Lit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _ShadowMap_ST;
            half4 _BaseColor;
            half4 _ShadeColor;
            half _BodyThreshold;
            half _EdgeSoftness;
            half _FaceEdgeSoftness;
            float _ShadowV0;
            float _ShadowVSpan;
            half _AmbientStrength;
            half _FeatureLight;
            half4 _RimColor;
            half _RimStrength;
            half4 _OutlineColor;
            float _OutlineWidth;
            half _FaceOutlineScale;
            float _EyeLive;
            float _BrowLive;
            float4 _EyeGeo;
            float4 _Iris0;
            float4 _Iris1;
            float4 _Shine0;
            float4 _Shine1;
            half4 _IrisColor;
            float4 _Look;
            float4 _EyeAnim;
            half4 _BrowColor;
            float4 _BrowRest;
            float4 _BrowMapRange;
            float4 _BrowXform;
            float4 _BrowBend0;
            float4 _BrowBend1;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_ShadowMap);
        SAMPLER(sampler_ShadowMap);
        TEXTURE2D(_EyeMask);
        SAMPLER(sampler_EyeMask);
        TEXTURE2D(_BrowTex);
        SAMPLER(sampler_BrowTex);
        ENDHLSL

        // ---------------------------------------------------------------- Forward
        Pass
        {
            Name "ToonFaceForward"
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
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 face       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half3  rightWS    : TEXCOORD3;
                float4 face       : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
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
                o.rightWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.face = input.face;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half Band(half ndl, half threshold, half soft)
            {
                return smoothstep(threshold - soft, threshold + soft, ndl);
            }

            // Brow Bench paintIris: the iris (moved by look, scaled by k) over the face features, clipped to the eye mask
            half3 LiveIris(half3 col, float2 e, float px, half m, float side)
            {
                float2 look = side > 0.0 ? _Look.xy : _Look.zw;
                float k = _EyeAnim.x;
                float2 c = _Iris0.xy + look;
                float rx = _Iris0.z * k, ry = _Iris0.w * k, rm = min(rx, ry);
                float r = length((e - c) / float2(rx, ry));
                half3 iris = _IrisColor.rgb, top = iris * 0.32h, glow = lerp(iris, half3(1, 1, 1), 0.55h);
                half3 ic;
                if (_Iris1.y > 0.0)
                {
                    float cut = c.y + _Iris0.w * k * (1.0 - 2.0 * _Iris1.y);
                    ic = lerp(iris, top, saturate((e.y - cut) / px + 0.5));
                }
                else
                {
                    float t = saturate((c.y + ry - e.y) / (2.0 * ry));   // 0 at the top
                    ic = t < 0.5 ? lerp(top, iris, t / 0.5) : lerp(iris, glow, saturate((t - 0.5) / 0.4));
                }
                col = lerp(col, ic, saturate((1.0 - r) * rm / px + 0.5) * m);
                col = lerp(col, top, saturate((_Iris1.x * k * 0.5 - abs(r - 1.0) * rm) / px + 0.5) * m);
                float2 s0 = c + (_Shine0.xy - _Iris0.xy) * k, s1 = c + (_Shine1.xy - _Iris0.xy) * k;
                half sh = max(_Shine0.w * saturate((_Shine0.z * k - length(e - s0)) / px + 0.5), _Shine1.w * saturate((_Shine1.z * k - length(e - s1)) / px + 0.5));
                return lerp(col, half3(1, 1, 1), sh * m);
            }

            // Brow Bench bendBrow + paintBrows, inverted: where this point came from on the resting brow, and its coverage there
            half LiveBrow(float2 hp)
            {
                float2 p = float2(abs(hp.x), hp.y) - _BrowRest.xy - _BrowXform.xy;
                float2 b = _BrowRest.xy + float2(p.x * _BrowXform.z + p.y * _BrowXform.w, -p.x * _BrowXform.w + p.y * _BrowXform.z);
                float x0 = _BrowRest.z, L = max(_BrowRest.w, 1e-4), pinch = _BrowBend0.w;
                float s = (b.x - x0) / L;
                [unroll] for (int it = 0; it < 3; it++)
                {
                    float om = saturate(1.0 - s);
                    float f = x0 + L * s - pinch * L * pow(om, 2.2) - b.x;
                    float df = L + pinch * L * 2.2 * pow(om, 1.2);
                    s -= f / df;
                }
                float sc = saturate(s), fi = pow(1.0 - sc, 2.2), fo = pow(sc, 2.2), fa = 4.0 * sc * (1.0 - sc);
                float u = (s - _BrowMapRange.x) / _BrowMapRange.y;
                float mid = SAMPLE_TEXTURE2D_LOD(_BrowTex, sampler_BrowTex, float2(u, 0.5), 0).g + _BrowRest.y;
                float t = max(0.2, 1.0 + _BrowBend1.x + _BrowBend1.y * fi);
                float d = (b.y - mid - L * (_BrowBend0.x * fi + _BrowBend0.y * fo + _BrowBend0.z * fa)) / t;
                float v = (d + _BrowMapRange.z) / (2.0 * max(_BrowMapRange.z, 1e-4));
                return SAMPLE_TEXTURE2D_LOD(_BrowTex, sampler_BrowTex, float2(u, v), 0).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.shadowMask = half4(1,1,1,1);

                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                half3 L = mainLight.direction;

                // body / back of the head: plain N.L with a soft narrow edge
                half body = Band(dot(n, L), _BodyThreshold, _EdgeSoftness);

                // face: the light's angle around the head picks the threshold, the painted map decides the shape
                float3 right = normalize(input.rightWS);
                float3 fwd = normalize(cross(right, float3(0, 1, 0)));
                float lx = dot(L, right), lz = dot(L, fwd);
                float th = (1.0 - lz / max(length(float2(lx, lz)), 1e-4)) * 0.5;
                float uNear = input.face.x * (lx >= 0.0 ? 1.0 : -1.0);
                float t = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, float2(uNear * 0.5 + 0.5, (input.face.y - _ShadowV0) / _ShadowVSpan)).r;
                half faceLit = smoothstep(th - _FaceEdgeSoftness, th + _FaceEdgeSoftness, t);

                half shadow = smoothstep(0.35h, 0.65h, mainLight.shadowAttenuation);
                half lit = lerp(body, faceLit, saturate(input.face.z)) * shadow;
                half3 albedo = lerp(_ShadeColor.rgb, _BaseColor.rgb, lit);
                half3 color = albedo * mainLight.color;

                #if defined(_ADDITIONAL_LIGHTS)
                #if USE_CLUSTER_LIGHT_LOOP
                for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light l = GetAdditionalLight(li, inputData.positionWS, inputData.shadowMask);
                    half a = Band(dot(n, l.direction), _BodyThreshold, _EdgeSoftness) * l.distanceAttenuation * l.shadowAttenuation;
                    color += _BaseColor.rgb * l.color * a;
                }
                #endif
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                    half a = Band(dot(n, l.direction), _BodyThreshold, _EdgeSoftness) * saturate(l.distanceAttenuation) * l.shadowAttenuation;
                    color += _BaseColor.rgb * l.color * a;
                LIGHT_LOOP_END
                #endif

                half3 ambient = SampleSH(n);
                color += _BaseColor.rgb * ambient * _AmbientStrength;

                // faint rim, stronger on the lit side
                half fres = pow(1.0h - saturate(dot(n, v)), 4.0h);
                color = lerp(color, _RimColor.rgb * mainLight.color, saturate(fres * _RimStrength * lerp(0.35h, 1.0h, lit)));

                // painted features over the front of the face (no face shadow on them, a little of the light's colour)
                half4 feat = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 featLight = lerp(half3(1, 1, 1), saturate(mainLight.color * 0.85h + ambient * 0.3h), _FeatureLight);
                half front = saturate(input.face.w);
                half3 skin = color;
                // head position of this texel (the face texture spans x -0.5..0.5, y -0.1..0.9 head units)
                float2 hp = float2(input.uv.x - 0.5, input.uv.y - 0.1);
                float side = hp.x >= 0.0 ? 1.0 : -1.0;
                float W = max(_EyeGeo.z, 1e-4);
                float2 e = float2((side * hp.x - _EyeGeo.x) / W + 0.5, (hp.y - _EyeGeo.y) / W - _EyeGeo.w);
                float px = max(max(fwidth(e.x), fwidth(e.y)), 1e-5);   // eye widths per screen pixel
                half4 em = half4(0, 0, 0, 0);
                if (_EyeLive > 0.5)
                {
                    em = SAMPLE_TEXTURE2D_LOD(_EyeMask, sampler_EyeMask, input.uv, 0);
                    if (em.r > 0.0) feat.rgb = LiveIris(feat.rgb, e, px, em.r, side);
                }
                color = lerp(color, feat.rgb * featLight, feat.a * front);

                // blink / expression lids: skin slides over the white from the top and the bottom, with a line at the edge
                if (_EyeLive > 0.5 && em.r > 0.0)
                {
                    float h = em.b * 1.5, w = em.g, lb = saturate(_EyeAnim.z), lt = saturate(_EyeAnim.y) * (1.0 - lb);
                    half m = em.r * front;
                    if (lb > 0.002)
                    {
                        color = lerp(color, skin, saturate((lb - w) * h / px + 0.5) * m);
                        half a = saturate((0.011 - abs(w - lb) * h) / px + 0.5) * min(1.0, lb * 6.0) * 0.8;
                        color = lerp(color, half3(0.69, 0.47, 0.416) * featLight, a * m);
                    }
                    if (lt > 0.002)
                    {
                        float edge = 1.0 - lt;
                        color = lerp(color, skin, saturate((w - edge) * h / px + 0.5) * m);
                        half a = saturate((0.021 - abs(w - edge) * h) / px + 0.5) * min(1.0, lt * 6.0);
                        color = lerp(color, half3(0.114, 0.086, 0.086) * featLight, a * m);
                    }
                }

                // brows on top
                if (_BrowLive > 0.5)
                {
                    half ba = LiveBrow(hp);
                    color = lerp(color, _BrowColor.rgb * featLight, ba * front);
                }

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Outline (inverted hull, thinner across the face)
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
                float4 face       : TEXCOORD1;
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
                float width = _OutlineWidth * lerp(1.0, _FaceOutlineScale, saturate(input.face.z));
                float aspect = _ScreenParams.y / _ScreenParams.x;
                float2 offset = nCS * (width * 0.002);
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

        // ---------------------------------------------------------------- DepthNormals
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
