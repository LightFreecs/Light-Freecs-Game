// LightMMORPG/AnimeAura - the anime flame aura (charging, Power Surge), a real 3D flame round the fighter.
// The shape is modelled in Blender (tools/aura_blender.py, ArtSource/Aura/aura.blend) and baked to Resources/VFX/AuraFlame.bytes:
// rings of flame blades over an inner skin, whose silhouette from any side is the owner's traced "pine tree" outline. It rises as
// eight hand-drawn drawings (AnimeAura.cs swaps the mesh, 4 drawings lift every ring one tier in 0.45 s, like the reference clip).
// Keep in step with docs/bench/aura-bench.html (same code in GLSL).
// Mesh space ("aura units"): x right, y up, z forward, feet at 0, the outline's peak at y = 1.
// The vertex shader bends the flame along the fighter's spine (sideways only, so it keeps rising) and sorts it: _Front = 0 draws
// the blades on the far side of the axis, _Front = 1 (a later queue) the near ones, so it blends correctly from every side.
// The near side clears a see-through arch round the fighter. Premultiplied output: rgb adds (HDR, feeds Bloom), alpha darkens
// what is behind so the aura reads on bright skies. Lives in Resources so builds include it (AnimeAura.cs loads it by name).
Shader "LightMMORPG/AnimeAura"
{
    Properties
    {
        [HDR] _Color ("Aura Color (HDR)", Color) = (1.3, 1.1, 0.13, 1)
        _Strength ("Size (charging 0.72-0.92, surge 1)", Range(0.3, 1.2)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
        _Front ("Layer: 0 far side, 1 near side", Float) = 0
        _BendA ("Spine offset (x, z)", Vector) = (0, 0, 0, 0)
        _BendB ("Spine slope (x, z)", Vector) = (0, 0, 0, 0)
        _BendC ("Spine curve (x, z)", Vector) = (0, 0, 0, 0)
        _BendTail ("Flame tip's drag (x, z)", Vector) = (0, 0, 0, 0)
        _BendTop ("Head top height (aura units)", Float) = 0.65
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
                float4 _BendA;
                float4 _BendB;
                float4 _BendC;
                float4 _BendTail;
                float _BendTop;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 data : TEXCOORD0;    // across, along, edge, rnd
                float2 misc : TEXCOORD1;    // az (the blade's direction from the axis), kind (0 blade, 1 skin, 2 flick)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 data : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 view : TEXCOORD2;
                float2 q : TEXCOORD3;
                float kind : TEXCOORD4;
                float depth : TEXCOORD5;
            };

            // the spine's sideways offset at height y (aura units): a parabola through the feet, hips and head top; above the head
            // its slope fades out, so the flame carries on rising instead of following the lean, and only the tip drags behind
            float2 Bend(float y, out float2 slope)
            {
                float k = clamp(y, 0.0, _BendTop);
                float2 b = _BendA.xy + k * (_BendB.xy + k * _BendC.xy);
                slope = _BendB.xy + 2.0 * k * _BendC.xy;
                if (y > _BendTop)
                {
                    float span = max(1.0 - _BendTop, 0.05);
                    float u = saturate((y - _BendTop) / span);
                    b += slope * span * 0.6 * (u - 0.5 * u * u) + _BendTail.xy * u * u;
                    slope = slope * 0.6 * (1.0 - u) + _BendTail.xy * 2.0 * u / span;
                }
                return b;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                ZERO_INITIALIZE(Varyings, o);
                float3 camOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float2 s0;
                float2 mid = Bend(0.4, s0);
                float2 toCam = camOS.xz - mid;
                toCam /= max(length(toCam), 1e-4);
                // the far half of the ring is drawn first, the near half after it; whole blades, so none is split
                bool isNear = cos(v.misc.x) * toCam.x + sin(v.misc.x) * toCam.y > 0.0;
                if (isNear != (_Front > 0.5))
                {
                    o.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    return o;
                }
                float2 slope;
                float2 b = Bend(v.positionOS.y, slope);
                float3 p = v.positionOS + float3(b.x, 0.0, b.y);
                o.normal = float3(v.normalOS.x, v.normalOS.y - slope.x * v.normalOS.x - slope.y * v.normalOS.z, v.normalOS.z);
                o.data = v.data;
                o.kind = v.misc.y;
                o.view = camOS - p;
                o.depth = dot(v.positionOS.xz, toCam) / max(length(v.positionOS.xz), 0.04);
                // where the view ray through this point crosses the plane through the fighter: the see-through arch is measured there
                float3 nPlane = float3(toCam.x, 0.0, toCam.y);
                float3 d = p - camOS;
                float t = dot(float3(mid.x, 0.0, mid.y) - camOS, nPlane) / min(dot(d, nPlane), -0.02);
                float3 hit = camOS + d * t;
                float2 sq;
                float2 ax = Bend(hit.y, sq);
                o.q = float2(dot(hit.xz - ax, float2(-toCam.y, toCam.x)), hit.y);
                o.positionCS = TransformObjectToHClip(p);
                return o;
            }

            float H21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H21(i), H21(i + float2(1.0, 0.0)), f.x), lerp(H21(i + float2(0.0, 1.0)), H21(i + float2(1.0, 1.0)), f.x), f.y);
            }

            // the see-through arch round the fighter: 1 inside, 0 in the flame. Below the feet it stays open, so the ring's near lip
            // doesn't cross the ground in front; each blade ends at its own place, so the edge is ragged like the flame, not a cut
            float Hollow(float2 q, float rnd)
            {
                float y = max(q.y, 0.0);
                float hw = 0.173 * pow(max(1.0 - (y / 0.70) * (y / 0.70), 0.0), 0.65);
                hw *= 0.9 + 0.3 * rnd;
                return smoothstep(-0.012, 0.05, hw - abs(q.x));
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float across = i.data.x, along = i.data.y, edge = i.data.z, rnd = i.data.w;
                float inside = saturate(edge / (max(fwidth(edge), 1e-5) * 1.3));    // the blade's crisp, antialiased outline
                if (inside <= 0.0) discard;
                float kind = floor(i.kind + 0.5);
                float3 c = _Color.rgb;
                float3 hot = lerp(c, float3(1.0, 1.0, 1.0), 0.45);
                float fade = _Fade * min(_Strength * 1.25, 1.0);
                bool nearSide = _Front > 0.5;
                if (kind > 1.5)                                                     // a flick: pure light
                    return float4(hot * inside * 0.9 * (nearSide ? 1.0 : 0.55), 0.0) * fade;
                float hol = Hollow(i.q, rnd);
                float3 N = normalize(i.normal), V = normalize(i.view);
                if (dot(N, V) < 0.0) N = -N;
                float t = _Time.y + _Seed;
                float tn = floor(t * 24.0) / 24.0;
                float str = VNoise(float2(across * 2.2 + rnd * 17.0, along * 2.4 - tn * 3.0)) * 0.7
                          + VNoise(float2(across * 5.0 + rnd * 31.0, along * 4.0 - tn * 4.2)) * 0.3;
                float limb = 1.0 - abs(i.depth);                                    // 1 where the flame turns away at its outline, 0 facing the camera
                float rim = (1.0 - smoothstep(0.006, 0.03, edge)) * smoothstep(0.22, 0.7, limb);   // the bright line is the OUTLINE's: inner blades stay calm
                float isSkin = step(0.5, kind);
                // the big round form: brightest at the outline, deeper toward the arch; the far side of the ring is a darker wall behind
                float form = nearSide ? lerp(0.74, 1.08, smoothstep(0.1, 0.85, limb)) : lerp(0.42, 0.6, limb);
                float layer = lerp(0.86, 1.04, smoothstep(0.05, 0.75, along)) * lerp(0.96, 1.04, rnd);   // each blade darkens into its root, so the tiers overlap
                float key = smoothstep(-0.25, 0.55, dot(N, normalize(float3(0.25, 0.85, 0.0) + V * 0.35)));   // a soft key light from above picks out each blade
                float tone = form * layer * lerp(0.9, 1.08, str) * lerp(1.0, 0.9, isSkin) * lerp(0.86, 1.08, key);
                float a = lerp(0.92, 0.84, isSkin) * lerp(0.55, 1.0, smoothstep(0.0, 0.16, along));
                float3 rgb = c * tone + hot * rim * (nearSide ? 0.62 : 0.26);
                if (nearSide)                                                       // the near half clears the arch, so the fighter stays in view
                {
                    float clearK = 1.0 - hol * 0.96;
                    rgb *= clearK;
                    a *= clearK;
                }
                else
                {
                    rgb *= lerp(1.0, 0.62, hol);
                }
                return float4(rgb * a, a) * inside * fade;
            }
            ENDHLSL
        }
    }
}
