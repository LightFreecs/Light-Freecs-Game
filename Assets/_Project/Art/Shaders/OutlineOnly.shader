// Inverted-hull outline only (same maths as ToonLit's outline pass). Used on a smooth-normal copy of flat-shaded meshes
// (menu island rocks), whose own ToonLit material has outline width 0, so the outline does not crack at hard edges.
Shader "LightMMORPG/OutlineOnly"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.08,0.06,0.1,1)
        _OutlineWidth ("Outline Width (1/1000 screen height)", Range(0,10)) = 2.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half fogFactor : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                float4 posCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(nCS);
                nCS = len > 1e-5 ? nCS / len : float2(0, 0);
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
    }
    Fallback Off
}
