// LightMMORPG/AnimeAura - the hand-drawn anime flame aura (charging, Power Surge), drawn on a camera-facing quad.
// 1:1 port of docs/bench/aura-bench.html (keep the two in step). Aura space: x right, y up, feet at 0, the fighter's head
// top at 0.62. The outline is traced from the owner's reference (a classic anime power-up aura): a flame "pine tree" of
// tiers - wide wings low down, blades that stand up straighter the higher they sit - and a narrow spire with one peak,
// round an arch-shaped see-through hollow where the fighter stands (its top just over the head). The tiers rise like the
// owner's reference clip: in a cycle of 4 hand-drawn drawings (0.45 s) every tier climbs to the next tier's place, so the
// bottom wings become the middle tiers and the middle ones the top, the top one melts into the spire and a new one grows
// out of the base (each tier keeps its own reach on the way up). The outline is redrawn with each drawing (it boils), the
// peak flickers, streaks run up the flame and small flicks rise in the hollow.
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

            // the right half of the outline in units of the aura's height (x out from the centre, y up from the feet), as tiers:
            // each tier's tip and the notch above it where the next tier starts. 1..6 are the traced tiers; 0 is the bottom
            // corner, where a new tier grows from, and 7 the spire, where the top tier melts away. The peak (0, 1) is on the axis
            static const float2 TIP[8] =
            {
                float2(0.252, 0.000), float2(0.457, 0.150), float2(0.449, 0.346), float2(0.319, 0.583),
                float2(0.232, 0.732), float2(0.157, 0.776), float2(0.087, 0.913), float2(0.035, 0.961)
            };
            static const float2 NOTCH[8] =
            {
                float2(0.252, 0.000), float2(0.299, 0.142), float2(0.248, 0.268), float2(0.224, 0.488),
                float2(0.165, 0.583), float2(0.130, 0.740), float2(0.055, 0.850), float2(0.035, 0.961)
            };
            #define NS 16           // vertices a side: the bottom corner, 7 rising tiers x (tip, notch), the spire
            #define NV 33           // both sides + the peak
            #define DRAWING 0.1125  // seconds each drawing is held: 4 drawings lift every tier one place in 0.45 s (the reference)

            // a vertex's hand-drawn wobble in drawing dn (every drawing redraws the outline a little differently)
            float2 Boil(float dn, float i, float side)
            {
                return (float2(H21(float2(dn, i * 3.0 + side)), H21(float2(dn + 0.5, i * 7.0 + side))) - 0.5) * 0.014;
            }

            // signed distance to the animated outline (negative inside), in aura-height units. dn = drawing number (wrapped),
            // td = drawing time. Each drawing lifts every tier a quarter of the way to the next tier's place; after 4 drawings
            // the outline is the traced one again with every tier one place higher
            float SdOutline(float2 q, float td, float dn)
            {
                float2 v[NV];
                float ph = frac(dn * 0.25);                              // how far the tiers have risen this cycle (0, 1/4, 1/2, 3/4)
                float cyc = floor(dn * 0.25);
                float sway = 0.025 * sin(td * 2.6);                      // the upper part sways
                [unroll] for (int side = 0; side < 2; side++)
                {
                    float sx = side == 0 ? 1.0 : -1.0;
                    float fs = (float)side;
                    v[side == 0 ? 0 : NV - 1] = float2(sx * TIP[0].x, 0.0);   // the bottom corner stays planted
                    float2 root = TIP[0];
                    [unroll] for (int j = 0; j < 7; j++)
                    {
                        // a tier growing out of the base or melting into the spire is small: less reach and wobble
                        float amp = j == 0 ? ph : (j == 6 ? 1.0 - ph : 1.0);
                        float2 tip = lerp(TIP[j], TIP[j + 1], ph);
                        float2 notch = lerp(NOTCH[j], NOTCH[j + 1], ph);
                        // each tier keeps its own reach (0.9..1.1) all the way up, so the eye can follow it rising
                        tip = root + (tip - root) * (1.0 + (H21(float2(cyc - (float)j, fs + 4.0)) - 0.5) * 0.2 * amp);
                        root = notch;
                        int a = 1 + j * 2;
                        tip += Boil(dn, (float)a, fs) * amp;
                        notch += Boil(dn, (float)(a + 1), fs) * amp;
                        v[side == 0 ? a : NV - 1 - a] = float2(sx * tip.x + sway * tip.y * tip.y, tip.y);
                        v[side == 0 ? a + 1 : NV - 2 - a] = float2(sx * notch.x + sway * notch.y * notch.y, notch.y);
                    }
                    float2 sp = TIP[7] + Boil(dn, 15.0, fs);
                    v[side == 0 ? NS - 1 : NV - NS] = float2(sx * sp.x + sway * sp.y * sp.y, sp.y);
                }
                float pk = 1.0 + (H21(float2(dn, 99.0)) - 0.5) * 0.06;     // the peak flickers
                v[NS] = float2(sway * pk * pk, pk);
                // polygon distance (Inigo Quilez); the max() keeps a tier that has not grown yet (coincident vertices) harmless
                float d = dot(q - v[0], q - v[0]);
                float s = 1.0;
                [unroll] for (int i = 0; i < NV; i++)
                {
                    int k = i == 0 ? NV - 1 : i - 1;
                    float2 e = v[k] - v[i], w = q - v[i];
                    float2 b = w - e * saturate(dot(w, e) / max(dot(e, e), 1e-8));
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
                float tn = floor(t * 24.0) / 24.0;                        // streak / flick time (24 a second)
                float nb = floor(t * 12.0);                               // the flicks' boil frame (12 a second)
                float dr = floor(t / DRAWING);                            // the outline's drawing (~9 a second, as in the reference)
                float dn = fmod(dr, 1024.0);                              // wrapped (a multiple of 4) so the hashes keep their precision
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
                float d = SdOutline(q, dr * DRAWING, dn);
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
