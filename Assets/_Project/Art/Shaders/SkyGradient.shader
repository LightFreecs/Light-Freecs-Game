// Main-menu skybox: 4-stop vertical gradient mapped over an elevation range (so it matches the screen-space bench
// gradients for the menu camera), a sun or moon disc with a halo, and hashed twinkling stars (night).
// Colours are set per sky preset by LightMMORPG.Menu.MenuSky from MenuTheme.
Shader "LightMMORPG/SkyGradient"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.16,0.45,0.85,1)
        _UpperColor ("Upper", Color) = (0.31,0.61,0.91,1)
        _HorizonColor ("Horizon", Color) = (0.53,0.76,0.96,1)
        _BelowColor ("Below horizon", Color) = (0.72,0.86,0.98,1)
        _UpperPos ("Upper stop (0 top .. 1 bottom)", Range(0,1)) = 0.38
        _HorizonPos ("Horizon stop", Range(0,1)) = 0.72
        _ElevTop ("Elevation at gradient top (deg)", Float) = 24
        _ElevBottom ("Elevation at gradient bottom (deg)", Float) = -14
        _SunDir ("Sun/moon direction (world)", Vector) = (0.3,0.2,0.9,0)
        [HDR] _SunColor ("Disc colour", Color) = (3,3,2.6,1)
        [HDR] _HaloColor ("Halo colour (a = strength)", Color) = (1,0.97,0.8,0.6)
        _SunSize ("Disc radius (deg)", Float) = 2.6
        _HaloSize ("Halo radius (deg)", Float) = 22
        _MoonShade ("Moon shading (0 sun, 1 moon)", Range(0,1)) = 0
        _Stars ("Stars", Range(0,1)) = 0
        _StarDensity ("Star density", Float) = 160
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _UpperColor, _HorizonColor, _BelowColor;
                float _UpperPos, _HorizonPos, _ElevTop, _ElevBottom;
                float4 _SunDir;
                half4 _SunColor, _HaloColor;
                float _SunSize, _HaloSize, _MoonShade, _Stars, _StarDensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hash33(float3 p)
            {
                return float3(Hash31(p), Hash31(p + 17.13), Hash31(p + 41.7));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float elev = degrees(asin(clamp(d.y, -1, 1)));
                float t = saturate((_ElevTop - elev) / max(0.01, _ElevTop - _ElevBottom));

                half3 col;
                if (t < _UpperPos) col = lerp(_TopColor.rgb, _UpperColor.rgb, smoothstep(0, _UpperPos, t));
                else if (t < _HorizonPos) col = lerp(_UpperColor.rgb, _HorizonColor.rgb, smoothstep(_UpperPos, _HorizonPos, t));
                else col = lerp(_HorizonColor.rgb, _BelowColor.rgb, smoothstep(_HorizonPos, 1, t));

                // stars (fade out towards the horizon)
                if (_Stars > 0.001)
                {
                    float3 p = d * _StarDensity;
                    float3 cell = floor(p);
                    float h = Hash31(cell);
                    float3 local = frac(p) - 0.5 - (Hash33(cell) - 0.5) * 0.6;
                    float size = lerp(0.05, 0.13, frac(h * 13.7));
                    float star = smoothstep(size, size * 0.2, length(local)) * step(0.93, h);
                    float tw = 0.55 + 0.45 * sin(_Time.y * (1.5 + h * 4.0) + h * 60.0);
                    col += star * tw * _Stars * saturate((_HorizonPos - t) * 3.0) * half3(0.92, 0.95, 1.0) * 1.6;
                }

                // sun / moon
                float3 sd = normalize(_SunDir.xyz);
                float ang = degrees(acos(clamp(dot(d, sd), -1, 1)));
                float halo = pow(saturate(1 - ang / max(0.1, _HaloSize)), 2.4);
                col += _HaloColor.rgb * halo * _HaloColor.a;
                float disc = 1 - smoothstep(_SunSize - 0.12, _SunSize + 0.12, ang);
                // moon: soft "maria" blotches inside the disc
                float maria = sin(d.x * 300) * sin(d.y * 260 + 1.3) * 0.5 + 0.5;
                half3 discCol = _SunColor.rgb * lerp(1.0, lerp(0.86, 1.0, maria), _MoonShade);
                col = lerp(col, discCol, disc);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
