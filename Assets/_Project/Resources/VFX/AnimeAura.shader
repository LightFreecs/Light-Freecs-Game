// LightMMORPG/AnimeAura - the hand-drawn anime flame aura (charging, Power Surge), drawn on a camera-facing quad.
// 1:1 port of docs/bench/aura-bench.html (keep the two in step). Aura space: x right, y up, feet at 0, the fighter's head
// top at 0.62. The flame hugs the body: a tall teardrop, narrow at the feet, fullest round the shoulders, narrowing to one
// sharp peak well over the head. Flame spikes (swell slowly, snap back, sheared so every tip leans up and out) race up
// both sides and melt into the peak; the peak sways and flickers; streaks and dashes rise inside; redrawn 24 times a
// second like anime frames. Two nested sheets: the outer flame (dim see-through body, bright rim) and a hotter, paler one.
// Two layers: _Front = 0 is the flame (behind the fighter), _Front = 1 only the rising dashes (in front of the fighter).
// Premultiplied output: rgb adds (HDR, feeds Bloom), alpha darkens what is behind a little so the aura reads on bright skies.
// Lives in Resources so builds include it (AnimeAura.cs loads it by name).
Shader "LightMMORPG/AnimeAura"
{
    Properties
    {
        [HDR] _Color ("Aura Color (HDR)", Color) = (1.3, 1.1, 0.13, 1)
        _Strength ("Size (charging 0.72-0.92, surge 1)", Range(0.3, 1.2)) = 1
        _Fade ("Fade (also raises the flame as it appears)", Range(0, 1)) = 1
        _Front ("Front layer (dashes only)", Float) = 0
        _SpanX ("Aura-space width of the quad", Float) = 0.84
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
                float _SpanX;
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

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H21(i), H21(i + float2(1.0, 0.0)), f.x), lerp(H21(i + float2(0.0, 1.0)), H21(i + float2(1.0, 1.0)), f.x), f.y);
            }

            float Fbm(float2 p) { return VNoise(p) * 0.57 + VNoise(p * 2.03 + 17.1) * 0.29 + VNoise(p * 4.1 + 31.7) * 0.14; }

            // the smooth envelope: half width at height y for a flame whose peak is at ytop
            float Envelope(float y, float ytop, float wmax)
            {
                float u = saturate(y / ytop);
                float body = lerp(0.58, 1.0, smoothstep(0.0, 0.4, u)) * sqrt(saturate((y + 0.035) / 0.09));   // rounded base
                float taper = pow(saturate((1.0 - u) / 0.52), 1.3);                                           // concave sides to the peak
                return wmax * body * taper;
            }

            // flame spikes up one side (side 0 left, 1 right), v counting spikes up the side: each one swells slowly and
            // snaps back; the shear in Flame() leans it up, so every tip points up and out. They race up the side and
            // every drawing redraws them a little differently.
            float Spikes(float u, float side, float t, float n, float seed)
            {
                float v = u * 5.5 - t * 2.2 + side * 0.5 + seed;
                float id = floor(v), f = frac(v);
                float amp = lerp(0.55, 1.0, H21(float2(id, side * 7.0 + 3.0 + seed)));
                amp *= lerp(0.82, 1.15, H21(float2(id + side * 50.0, n + seed)));
                return amp * pow(f, 1.8) * smoothstep(1.0, 0.93, f);
            }

            // flame shape: < 1 inside, 1 on the edge. t = drawing time, n = drawing number
            float Flame(float2 p, float t, float n, float ytop, float wmax, float amp, float sway, float seed)
            {
                float u = p.y / ytop;
                float x = p.x - sway * u * u * wmax / 0.2;                               // the upper part sways
                float W = Envelope(p.y, ytop, wmax);
                float A = amp * lerp(1.0, 0.6, saturate(u)) * smoothstep(0.0, 0.08, u) * smoothstep(0.98, 0.7, u);
                float d = max(abs(x) - W, 0.0);                                          // how far out past the smooth envelope
                float lean = lerp(1.3, 3.0, saturate(u));                                // spikes lean out low down, point up high up
                float g = Spikes((p.y - lean * d) / ytop, x < 0.0 ? 0.0 : 1.0, t, n, seed);
                float s = abs(x) / max(W + A * g, 0.002);
                s += (Fbm(float2(x / max(W, 0.02) * 3.0 + seed, p.y * 4.0 - t * 4.5)) - 0.5) * 0.14 * smoothstep(0.7, 1.0, s);   // licks tear off
                return u > 0.995 ? max(s, 1.0 + (u - 0.995) * 40.0) : s;
            }

            float CoverOf(float s) { float w = clamp(fwidth(s), 1e-4, 0.5) * 0.8; return 1.0 - smoothstep(1.0 - w, 1.0 + w, s); }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = float2((i.uv.x - 0.5) * _SpanX, i.uv.y * 1.25 - 0.04);
                float t = _Time.y + _Seed;
                float k = saturate((_Strength - 0.55) / 0.45);          // 0 a weak charge .. 1 Power Surge
                float n = floor(t * 24.0);                               // drawing number (held 2-3 frames at 60 fps)
                float tn = n / 24.0;
                // rises as it appears, flickers a little every drawing
                float ytop = lerp(0.86, 1.08, k) * lerp(0.6, 1.0, _Fade) * (1.0 + (H21(float2(n, 9.0)) - 0.5) * 0.05);
                float wmax = lerp(0.17, 0.2, k), amp = lerp(0.1, 0.13, k);
                float sway = 0.03 * sin(tn * 2.6);
                float3 c = _Color.rgb;
                // tips that reach the quad's edge thin out instead of being cut off
                float edge = smoothstep(1.0, 0.95, i.uv.y) * smoothstep(0.0, 0.04, i.uv.x) * smoothstep(1.0, 0.96, i.uv.x);
                float fade = _Fade * min(_Strength * 1.25, 1.0) * edge;
                float ground = smoothstep(-0.04, -0.01, p.y);
                float sA = Flame(p, tn, n, ytop, wmax, amp, sway, 0.0);
                float inA = CoverOf(sA) * ground;
                float xn = p.x / max(Envelope(p.y, ytop, wmax), 0.015);   // across the flame, -1 .. 1: streaks converge on the peak
                if (_Front > 0.5)
                {
                    float st = smoothstep(0.86, 0.93, VNoise(float2(xn * 6.0, p.y * 6.0 - tn * 7.0))) * inA * smoothstep(0.04, 0.16, p.y) * smoothstep(0.98, 0.6, sA);
                    return float4(c * st * 0.9, 0.0) * fade;
                }
                float sB = Flame(p, tn * 1.2 + 3.7, n + 77.0, ytop * 0.8, wmax * 0.62, amp * 0.7, sway, 13.0);
                float inB = CoverOf(sB) * ground;
                float str = VNoise(float2(xn * 7.0, p.y * 2.2 - tn * 3.2)) * 0.7 + VNoise(float2(xn * 17.0 + 5.0, p.y * 3.6 - tn * 4.2)) * 0.3;
                float eA = smoothstep(0.5, 1.0, sA);                     // 0 deep inside .. 1 at the rim
                float eB = smoothstep(0.45, 1.0, sB);
                float rimA = smoothstep(0.86, 0.985, sA) * inA;           // bright outline round every spike
                float rimB = smoothstep(0.86, 0.985, sB) * inB;
                float bodyA = inA * lerp(0.08, 0.85, eA * eA) * lerp(0.45, 1.35, str);       // dim see-through body, brighter to the rim
                float bodyB = inB * 0.8 * eB * eB * eB * eB * lerp(0.55, 1.25, str);        // inner sheet: hotter, paler, mostly rim
                float3 hot = lerp(c, float3(1.0, 1.0, 1.0), 0.35);
                float3 rgb = c * bodyA + hot * (bodyB * 0.5 + rimB * 0.45 + rimA * 0.55);
                rgb += c * exp(-max(sA - 1.0, 0.0) * 16.0) * (1.0 - inA) * 0.08 * ground;   // soft glow just outside the edge
                float a = inA * lerp(0.18, 0.7, eA) + inB * 0.1 * eB;
                return float4(rgb, min(a, 0.9)) * fade;
            }
            ENDHLSL
        }
    }
}
