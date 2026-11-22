// LightMMORPG/AnimeAura - the hand-drawn anime flame aura (charging, Power Surge), drawn on a camera-facing quad.
// 1:1 port of docs/bench/aura-bench.html (keep the two in step). Aura space: x right, y up, feet at 0, the fighter's head
// top at 0.62. The outline is traced from the owner's reference (a classic anime power-up aura): a flame "pine tree" of
// seven tiers a side - wide wings low down, blades that stand up straighter the higher they sit - and a narrow spire with
// one peak, round an arch-shaped see-through hollow where the fighter stands (its top just over the head). Every tier tip
// licks out and snaps back on its own rhythm, the outline boils 12 times a second like hand-drawn frames, the peak
// flickers, streaks run up the flame and small flicks rise in the hollow.
// Two layers: _Front = 0 is the flame (behind the fighter), _Front = 1 only the rising flicks (in front of the fighter).
// Premultiplied output: rgb adds (HDR, feeds Bloom), alpha darkens what is behind so the aura reads on bright skies.
// Lives in Resources so builds include it (AnimeAura.cs loads it by name).
Shader "LightMMORPG/AnimeAura"
{
    Properties
    {
        [HDR] _Color ("Aura Color (HDR)", Color) = (1.3, 1.1, 0.13, 1)
        _Strength ("Size (charging 0.72-0.92, surge 1)", Range(0.3, 1.2)) = 1
        _Fade ("Fade (also grows the aura as it appears)", Range(0, 1)) = 1
        _Front ("Front layer (flicks only)", Float) = 0
        _SpanX ("Aura-space width of the quad", Float) = 1.1
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

            // the right half of the outline in units of the aura's height (x out from the centre, y up from the feet): the
            // bottom corner, then each tier's tip and the notch where the next tier starts, then the spire; the peak (0, 1)
            // sits on the axis
            static const float2 OUTLINE[14] =
            {
                float2(0.252, 0.000),
                float2(0.457, 0.150), float2(0.299, 0.142),
                float2(0.449, 0.346), float2(0.248, 0.268),
                float2(0.319, 0.583), float2(0.224, 0.488),
                float2(0.232, 0.732), float2(0.165, 0.583),
                float2(0.157, 0.776), float2(0.130, 0.740),
                float2(0.087, 0.913), float2(0.055, 0.850),
                float2(0.035, 0.961)
            };
            #define NV 29   // 14 a side + the peak

            // how far a tier tip reaches now: it licks out slowly, whips out and snaps back, each tip on its own rhythm
            float Pump(float j, float side, float t)
            {
                float f = frac(t * 1.9 + H21(float2(j, side + 4.0)));
                return lerp(0.9, 1.12, f * f);
            }

            // signed distance to the animated outline (negative inside), in aura-height units. t = drawing time, nb = boil frame
            float SdOutline(float2 q, float t, float nb)
            {
                float2 v[NV];
                float sway = 0.025 * sin(t * 2.6);                       // the upper part sways
                [unroll] for (int side = 0; side < 2; side++)
                {
                    float sx = side == 0 ? 1.0 : -1.0;
                    [unroll] for (int j = 0; j < 14; j++)
                    {
                        float2 c = OUTLINE[j];
                        if (j % 2 == 1 && j < 13) { float2 s0 = OUTLINE[j - 1]; c = s0 + (c - s0) * Pump((float)j, (float)side, t); }
                        if (j > 0) c += (float2(H21(float2(nb, (float)(j * 3 + side))), H21(float2(nb + 0.5, (float)(j * 7 + side)))) - 0.5) * 0.014;
                        v[side == 0 ? j : NV - 1 - j] = float2(sx * c.x + sway * c.y * c.y, c.y);
                    }
                }
                float pk = 1.0 + (H21(float2(nb, 99.0)) - 0.5) * 0.06;     // the peak flickers
                v[14] = float2(sway * pk * pk, pk);
                // polygon distance (Inigo Quilez)
                float d = dot(q - v[0], q - v[0]);
                float s = 1.0;
                [unroll] for (int i = 0; i < NV; i++)
                {
                    int k = i == 0 ? NV - 1 : i - 1;
                    float2 e = v[k] - v[i], w = q - v[i];
                    float2 b = w - e * saturate(dot(w, e) / dot(e, e));
                    d = min(d, dot(b, b));
                    bool c1 = q.y >= v[i].y, c2 = q.y < v[k].y, c3 = e.x * w.y > e.y * w.x;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
                }
                return s * sqrt(d);
            }

            // the see-through hollow round the fighter: 1 inside, 0 in the flame (an arch, top at 0.70 of the aura's height)
            float Hollow(float2 q)
            {
                float hw = 0.173 * pow(max(1.0 - (q.y / 0.70) * (q.y / 0.70), 0.0), 0.65);
                return smoothstep(0.0, 0.035, hw - abs(q.x)) * step(0.0, q.y);
            }

            // small flame flicks rising inside the hollow (seed picks the set: the back and front layers use different ones)
            float Flicks(float2 q, float t, float nb, float seed)
            {
                float2 cell = float2(0.075, 0.16);
                float2 g = float2(q.x, q.y - t * 0.55) / cell;
                float2 id = floor(g);
                float hs = H21(id + seed);
                if (hs < 0.55) return 0.0;
                float2 f = frac(g) - 0.5;
                f.x -= (H21(id + seed + 7.1) - 0.5) * 0.5;
                float len = lerp(0.35, 0.8, H21(id + seed + 2.3 + floor(nb / 2.0) * 0.17));   // boils every other frame
                float along = (f.y + 0.4) / len;                                               // 0 root .. 1 tip
                float wid = 0.16 * (1.0 - along) * smoothstep(0.0, 0.2, along);
                float dx = abs(f.x - 0.06 * sin(along * 3.0 + hs * 6.0));
                return step(0.0, along) * step(along, 1.0) * smoothstep(wid, wid * 0.4, dx);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = float2((i.uv.x - 0.5) * _SpanX, i.uv.y * 1.25 - 0.04);
                float t = _Time.y + _Seed;
                float k = saturate((_Strength - 0.55) / 0.45);           // 0 a weak charge .. 1 Power Surge
                float hgt = lerp(0.865, 0.996, k) * lerp(0.6, 1.0, _Fade); // the aura's height (the hollow's top just over the head)
                float2 q = p / hgt;
                float tn = floor(t * 24.0) / 24.0;                        // drawing time (24 a second)
                float nb = floor(t * 12.0);                               // boil frame (12 a second)
                float3 c = _Color.rgb;
                // anything that reaches the quad's edge thins out instead of being cut off
                float edge = smoothstep(1.0, 0.95, i.uv.y) * smoothstep(0.0, 0.03, i.uv.x) * smoothstep(1.0, 0.97, i.uv.x);
                float fade = _Fade * min(_Strength * 1.25, 1.0) * edge;
                float hol = Hollow(q);
                if (_Front > 0.5)
                {
                    float fl = Flicks(q, tn, nb, 0.0) * hol * smoothstep(0.05, 0.12, q.y) * smoothstep(0.66, 0.5, q.y);
                    return float4(c * fl * 0.85, 0.0) * fade;
                }
                float d = SdOutline(q, tn, nb);
                float w = max(fwidth(d), 1e-4) * 0.75;
                float inside = 1.0 - smoothstep(-w, w, d);
                float band = inside * (1.0 - hol);
                float str = VNoise(float2(q.x * 30.0, q.y * 2.5 - tn * 3.0)) * 0.7 + VNoise(float2(q.x * 70.0, q.y * 4.0 - tn * 4.2)) * 0.3;
                float rim = smoothstep(-0.028, -0.004, d) * inside;      // the outline's edge is drawn brighter
                float fl = Flicks(q, tn, nb, 31.0) * hol * inside * smoothstep(0.05, 0.12, q.y);
                float3 hot = lerp(c, float3(1.0, 1.0, 1.0), 0.35);
                float3 rgb = c * (band * lerp(0.7, 1.1, str) + inside * hol * 0.16 + fl * 0.8) + hot * band * rim * 0.45;
                rgb += c * exp(-max(d, 0.0) / 0.015) * (1.0 - inside) * 0.07;   // soft glow round the outline
                float a = band * 0.78 + inside * hol * 0.1;
                return float4(rgb, a) * fade;
            }
            ENDHLSL
        }
    }
}
