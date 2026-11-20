// LightMMORPG/AnimeAura - the hand-drawn anime flame aura (charging, Power Surge), drawn on a camera-facing quad.
// 1:1 port of docs/bench/aura-bench.html (keep the two in step). Aura space: x right, y up, feet at 0, crown tip near 1:
// an egg-shaped body round the fighter with straight sharp spikes on it that point out and up (flat out at the feet,
// straight up at the crown), redrawn 20 times a second like anime frames; thin strokes rise inside.
// Two layers: _Front = 0 is the flame (behind the fighter), _Front = 1 only the rising strokes (in front of the fighter).
// Premultiplied output: rgb adds (HDR, feeds Bloom), alpha darkens what is behind a little so the aura reads on bright skies.
// Lives in Resources so builds include it (AnimeAura.cs loads it by name).
Shader "LightMMORPG/AnimeAura"
{
    Properties
    {
        [HDR] _Color ("Aura Color (HDR)", Color) = (1.3, 1.1, 0.13, 1)
        _Strength ("Size (0.85 charging, 1 surge)", Range(0.3, 1.2)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
        _Front ("Front layer (strokes only)", Float) = 0
        _Aspect ("Quad width / height * 1.15", Float) = 1
        _Seed ("Time offset (per fighter)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "AnimeAura"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Strength;
                float _Fade;
                float _Front;
                float _Aspect;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float H21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            static const float2 EGG_C = float2(0.0, 0.28);
            static const float EGG_W = 0.21, EGG_TOP = 0.44, EGG_BOT = 0.28;
            static const float PI_F = 3.14159265;

            float SdEgg(float2 p)
            {
                float2 q = p - EGG_C;
                float2 r = float2(EGG_W, q.y > 0.0 ? EGG_TOP : EGG_BOT);
                float k0 = length(q / r), k1 = length(q / (r * r));
                return k0 * (k0 - 1.0) / max(k1, 1e-4);
            }

            // isosceles triangle, apex at the origin, base (half width q.x) at y = q.y
            float SdTri(float2 p, float2 q)
            {
                p.x = abs(p.x);
                float2 a = p - q * saturate(dot(p, q) / dot(q, q));
                float2 b = p - q * float2(saturate(p.x / q.x), 1.0);
                float s = -sign(q.y);
                float2 d = min(float2(dot(a, a), s * (p.x * q.y - p.y * q.x)), float2(dot(b, b), s * (p.y - q.y)));
                return -sqrt(d.x) * sign(d.y);
            }

            // one spike: base centred on the egg at angle th (0 = top, + = right side), pointing at angle phi from straight up
            float SdSpike(float2 p, float th, float phi, float len, float halfW)
            {
                float c = cos(th);
                float2 bse = EGG_C + float2(sin(th) * EGG_W, c * (c > 0.0 ? EGG_TOP : EGG_BOT)) * 0.86;
                float2 dir = float2(sin(phi), cos(phi));
                float2 d = p - (bse + dir * len);
                return SdTri(float2(dot(d, float2(dir.y, -dir.x)), -dot(d, dir)), float2(halfW, len + 0.05));
            }

            float SMin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }

            #define SIDE_SPIKES 6

            // distance to the whole silhouette (negative inside) for drawing number n
            float SdAura(float2 p, float n, float rise)
            {
                float d = SdEgg(p);
                float sp = 1e3;
                // crown: one or two tall spikes and two shorter ones beside them
                float twin = step(0.6, H21(float2(n, 11.0)));
                float c0 = (H21(float2(n, 1.0)) - 0.5) * 0.12;
                sp = min(sp, SdSpike(p, c0 - twin * 0.07, c0 * 0.6 - twin * 0.05, lerp(0.25, 0.31, H21(float2(n, 2.0))), 0.042));
                if (twin > 0.5) sp = min(sp, SdSpike(p, c0 + 0.08, c0 * 0.6 + 0.06, lerp(0.22, 0.29, H21(float2(n, 3.0))), 0.04));
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float sgn = k == 0 ? -1.0 : 1.0;
                    sp = min(sp, SdSpike(p, sgn * lerp(0.42, 0.55, H21(float2(n, 4.0 + k))), sgn * lerp(0.18, 0.3, H21(float2(n, 6.0 + k))),
                                         lerp(0.18, 0.26, H21(float2(n, 8.0 + k))), 0.036));
                }
                // flanks: spikes slide up each side, leaning further out the lower they sit
                [unroll] for (int side = 0; side < 2; side++)
                {
                    float sgn = side == 0 ? -1.0 : 1.0;
                    [unroll] for (int i = 0; i < SIDE_SPIKES; i++)
                    {
                        float u = (i + 0.5 + side * 0.5) / SIDE_SPIKES - rise;
                        float cyc = floor(u);
                        u -= cyc;
                        float id = i - cyc * SIDE_SPIKES + side * 100.0;
                        u += (H21(float2(id, n)) - 0.5) * 0.05;               // each drawing wobbles a little
                        float s = lerp(0.22, 0.93, u);                        // 0 top .. 1 feet
                        float th = sgn * s * PI_F;
                        float phi = sgn * lerp(0.45, 1.45, s);                // ~25 deg from vertical up top, ~83 at the feet
                        float len = lerp(0.16, 0.26, H21(float2(id, 21.0))) * lerp(0.85, 1.12, H21(float2(id, n + 40.0)));
                        len *= smoothstep(0.0, 0.12, u) * smoothstep(1.0, 0.93, u);
                        sp = min(sp, SdSpike(p, th, phi, len, len * 0.2 + 0.012));
                    }
                }
                return SMin(d, sp, 0.016);
            }

            // rising strokes inside the aura: hooks / arcs and thin vertical lines
            float Strokes(float2 p, float t)
            {
                float2 cell = float2(1.0 / 8.0, 1.0 / 5.0);
                float2 g = float2(p.x, p.y - t * 0.9) / cell;
                float2 id = floor(g);
                float hs = H21(id + 0.37);
                if (hs < 0.45) return 0.0;
                float2 q = (frac(g) - 0.5 - (float2(H21(id + 7.1), H21(id + 3.3)) - 0.5) * 0.3) * cell;
                if (hs < 0.8)
                {
                    float rad = lerp(0.016, 0.028, H21(id + 1.9));
                    float a = atan2(q.x, q.y) - (H21(id + 5.5) - 0.5) * 2.0;   // arcs open downwards, tilted
                    float d = abs(length(q) - rad);
                    return smoothstep(0.004, 0.0012, d) * step(abs(a), 1.35);
                }
                float len = lerp(0.025, 0.06, H21(id + 2.7));
                return smoothstep(0.0032, 0.001, abs(q.x - q.y * 0.15)) * step(abs(q.y), len);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = float2((i.uv.x - 0.5) * _Aspect, i.uv.y * 1.15 - 0.04);
                p /= max(_Strength, 0.05);                 // smaller while charging, full size in Power Surge
                float t = _Time.y + _Seed;
                float n = floor(t * 20.0);                 // drawing number (held 3 frames at 60 fps)
                float tn = n / 20.0;
                float3 c = _Color.rgb;
                float fade = _Fade * min(_Strength * 1.25, 1.0);
                float egg = SdEgg(p);
                float st = Strokes(p, tn) * smoothstep(0.0, -0.03, egg) * smoothstep(0.03, 0.14, p.y);
                if (_Front > 0.5) return float4(c * st * 1.1, 0.0) * fade;
                float d = SdAura(p, n, frac(tn * 1.6));
                const float soft = 0.004;
                float inside = 1.0 - smoothstep(-soft, soft, d);
                float fill = inside * pow(smoothstep(-0.07, 0.01, egg), 2.0);     // spikes solid, the body fades to dark inside
                float tipDim = 1.0 - 0.25 * smoothstep(0.02, 0.2, egg);           // tips a touch deeper
                float halo = exp(-max(d, 0.0) / 0.03) * (1.0 - inside) * 0.2 + (1.0 - inside) * exp(-max(egg, 0.0) / 0.07) * 0.07;
                float haze = inside * (1.0 - fill) * 0.04;                        // dim haze in the body
                float3 rgb = c * (fill * tipDim + halo + haze + st * 1.2);
                float a = inside * lerp(0.3, 0.85, fill);
                return float4(rgb, a) * fade;
            }
            ENDHLSL
        }
    }
}
