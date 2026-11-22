// LightMMORPG/AnimeAura - the hand-drawn anime flame aura (charging, Power Surge), drawn on a camera-facing quad.
// 1:1 port of docs/bench/aura-bench.html (keep the two in step). Aura space: x right, y up, feet at 0, the fighter's head
// top near 0.62. The flame is a fan of long streaky tongues that shoot out of the ground round the feet: out and up at
// the sides, straight up over the head. Everything flows outward along the fan all the time (tongues pump, sway, tear
// off), redrawn 24 times a second like anime frames. Two nested sheets: the big outer flame and a hotter, paler inner
// one; bright rims, a dim see-through body.
// Two layers: _Front = 0 is the flame (behind the fighter), _Front = 1 only the rising dashes (in front of the fighter).
// Premultiplied output: rgb adds (HDR, feeds Bloom), alpha darkens what is behind a little so the aura reads on bright skies.
// Lives in Resources so builds include it (AnimeAura.cs loads it by name).
Shader "LightMMORPG/AnimeAura"
{
    Properties
    {
        [HDR] _Color ("Aura Color (HDR)", Color) = (1.3, 1.1, 0.13, 1)
        _Strength ("Size (charging 0.72-0.92, surge 1)", Range(0.3, 1.2)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
        _Front ("Front layer (dashes only)", Float) = 0
        _Aspect ("Quad width / height * 1.15", Float) = 1.4
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

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H21(i), H21(i + float2(1.0, 0.0)), f.x), lerp(H21(i + float2(0.0, 1.0)), H21(i + float2(1.0, 1.0)), f.x), f.y);
            }

            float Fbm(float2 p) { return VNoise(p) * 0.57 + VNoise(p * 2.03 + 17.1) * 0.29 + VNoise(p * 4.1 + 31.7) * 0.14; }

            static const float2 FAN_O = float2(0.0, -0.01);   // the fan's origin, at the feet

            // one row of tongues across the fan: k = fan angle * count; each tongue is a needle whose length flows outward
            float Tongues(float k, float r, float t, float seed)
            {
                float id = floor(k), f = frac(k);
                float tri = 1.0 - abs(2.0 * f - 1.0);
                float h = VNoise(float2(id * 1.73 + seed, r * 1.4 - t * 2.4));
                h = smoothstep(0.1, 0.9, h);
                return pow(tri, 1.35) * h;
            }

            // flame shape: < 1 inside, 1 on the edge. q = point in aura space, t = drawing time
            float Flame(float2 q, float t, float seed, float spread)
            {
                float2 d = q - FAN_O;
                float r = length(d);
                float a = atan2(d.x, d.y);                                                // 0 straight up, +-1.57 flat out
                float base = lerp(0.34, 1.0, pow(max(cos(a * spread), 0.0), 1.3)) * 0.98;
                float warp = (Fbm(float2(a * 2.2 + seed, r * 1.8 - t * 2.8)) - 0.5) * 1.1;   // tongues sway and lean as they rise
                float k = a * 5.5 + warp;
                float tg = max(Tongues(k, r, t, seed), Tongues(k * 2.3 + 0.37, r * 1.2, t * 1.25, seed + 31.0) * 0.75);
                float R = base * (0.68 + 0.5 * tg);
                float s = r / max(R, 1e-3);
                // licks that tear off the edge and rise away
                s += (Fbm(float2(a * 8.0 + seed, r * 3.2 - t * 4.2)) - 0.5) * 0.32 * smoothstep(0.55, 1.0, s);
                return s;
            }

            // the streak texture running along the fan (motion-blurred brush strokes)
            float Streaks(float2 q, float t, float seed)
            {
                float2 d = q - FAN_O;
                float r = length(d), a = atan2(d.x, d.y);
                return VNoise(float2(a * 24.0 + seed, r * 1.3 - t * 3.2)) * 0.7 + VNoise(float2(a * 60.0 + seed * 2.0, r * 2.2 - t * 4.0)) * 0.3;
            }

            float CoverOf(float s) { float w = max(fwidth(s), 1e-4) * 0.8; return 1.0 - smoothstep(1.0 - w, 1.0 + w, s); }

            // rising dashes inside the aura (front layer): short bright strokes flying up the fan
            float Strokes(float2 q, float t)
            {
                float2 d = q - FAN_O;
                float r = length(d), a = atan2(d.x, d.y);
                return smoothstep(0.86, 0.93, VNoise(float2(a * 34.0, r * 5.0 - t * 7.0)));
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = float2((i.uv.x - 0.5) * _Aspect, i.uv.y * 1.15 - 0.04);
                p /= max(_Strength, 0.05);                 // smaller while charging, full size in Power Surge
                float t = _Time.y + _Seed;
                float tn = floor(t * 24.0) / 24.0;         // drawing time (held 2-3 frames at 60 fps)
                float3 c = _Color.rgb;
                // tongue tips that reach the quad's edge thin out instead of being cut off
                float edge = smoothstep(1.0, 0.9, i.uv.y) * smoothstep(0.0, 0.05, i.uv.x) * smoothstep(1.0, 0.95, i.uv.x);
                float fade = _Fade * min(_Strength * 1.25, 1.0) * edge;
                float ground = smoothstep(-0.03, 0.02, p.y);
                float sA = Flame(p, tn, 0.0, 1.0);
                float inA = CoverOf(sA) * ground;
                if (_Front > 0.5)
                {
                    float st = Strokes(p, tn) * inA * smoothstep(0.04, 0.16, p.y) * smoothstep(0.98, 0.6, sA);
                    return float4(c * st * 0.9, 0.0) * fade;
                }
                float sB = Flame(p / 0.74 + float2(0.0, 0.03), tn * 1.15 + 3.7, 57.0, 1.08);
                float inB = CoverOf(sB) * ground;
                float str = Streaks(p, tn, 0.0);
                float eA = smoothstep(0.5, 1.0, sA);                                   // 0 deep inside .. 1 at the rim
                float eB = smoothstep(0.45, 1.0, sB);
                float rimA = smoothstep(0.86, 0.985, sA) * inA;                         // bright outline round every tongue
                float bodyA = inA * lerp(0.08, 0.85, eA * eA) * lerp(0.45, 1.35, str);       // dim see-through body, brighter to the rim
                float bodyB = inB * lerp(0.04, 0.95, eB * eB * eB) * lerp(0.55, 1.25, str);  // inner sheet: hotter, paler
                float3 hot = lerp(c, float3(1.0, 1.0, 1.0), 0.5);
                float3 rgb = c * bodyA + hot * (bodyB * 0.6 + rimA * 0.55);
                rgb += c * exp(-max(sA - 1.0, 0.0) * 16.0) * (1.0 - inA) * 0.08 * ground;   // soft glow just outside the edge
                float a = inA * lerp(0.18, 0.7, eA) + inB * 0.1 * eB;
                return float4(rgb, min(a, 0.9)) * fade;
            }
            ENDHLSL
        }
    }
}
