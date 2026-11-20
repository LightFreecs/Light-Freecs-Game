using System.Collections.Generic;
using LightMMORPG.Characters;
using LightMMORPG.Combat;
using UnityEngine;

namespace LightMMORPG.VFX
{
    /// <summary>
    /// Anime battle damage (owner's reference art: clusters of short red scratch strokes over a faint brownish-purple
    /// smudge) and one eye shut (every client, from synced state only, so everyone sees the same thing):
    /// <list type="bullet">
    /// <item>Every clean hit (not blocked) leaves a bruise where it landed (the struck spot the server reports): up to
    /// <see cref="MaxBruises"/> per fighter, nearby hits darken the same bruise, the weakest is replaced when full. The spot
    /// is stored in the body's bind pose (mesh UV2, <c>LightMMORPG/ToonLit</c> <c>_Bruises</c>), so it sticks to the skin.</item>
    /// <item>Hits to the head bruise the face instead (<c>LightMMORPG/ToonFace</c> <c>_Injury</c>).</item>
    /// <item>Low health: below 60 % every bruise darkens and the face bruises (round one eye, the other cheek) come up;
    /// heavily injured (under 30 %, fully by 20 %) one eye shuts (a closed lid line).</item>
    /// <item>Back to full health (respawn, the training dummy's refill) heals everything.</item>
    /// </list>
    /// </summary>
    public sealed class InjuryVisuals : MonoBehaviour
    {
        public const int MaxBruises = 8;
        /// <summary>Health share where low-health bruising starts, and where the eye starts / finishes swelling shut.</summary>
        public const float BruiseBelow = 0.6f, EyeStart = 0.3f, EyeShut = 0.2f;

        private static readonly int BruisesId = Shader.PropertyToID("_Bruises"), BruiseNId = Shader.PropertyToID("_BruiseN"), BruiseParamsId = Shader.PropertyToID("_BruiseParams");
        private static readonly int InjuryId = Shader.PropertyToID("_Injury");

        private sealed class State
        {
            public CharacterRig Rig;
            public readonly Vector4[] Bruises = new Vector4[MaxBruises];
            public readonly Vector4[] Normals = new Vector4[MaxBruises];   // xyz = the skin's outward direction (bind pose), w = seed
            public int Count;
            public float FaceHits, Eye, LastHealth = -1f;
            public float EyeSide;
            public bool Dirty = true, Clean = true;
        }

        private readonly Dictionary<PlayerStats, State> _states = new();
        private readonly List<PlayerStats> _gone = new();
        private MaterialPropertyBlock _mpb;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("InjuryVisuals") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<InjuryVisuals>();
        }

        private void OnEnable() => PlayerStats.OnHitFeedback += OnHit;
        private void OnDisable() => PlayerStats.OnHitFeedback -= OnHit;

        private State Get(PlayerStats p)
        {
            if (!_states.TryGetValue(p, out var s))
            {
                s = new State { EyeSide = (p.ObjectId & 1) == 0 ? 1f : -1f };   // same eye on every client
                _states[p] = s;
            }
            return s;
        }

        private static CharacterRig FindRig(PlayerStats p)
        {
            var g = p.transform.Find("Graphics/Model");
            return (g != null ? g : p.transform).GetComponentInChildren<CharacterRig>();
        }

        private void OnHit(PlayerStats victim, PlayerStats attacker, Vector3 point, float damage, bool blocked, bool guardBreak, HitKind kind)
        {
            if (victim == null || blocked || damage <= 0f) return;
            var s = Get(victim);
            if (s.Rig == null) s.Rig = FindRig(victim);
            var rig = s.Rig;
            if (rig == null) return;
            float add = kind is HitKind.Light ? 0.35f : kind is HitKind.Blast ? 0.4f : 0.6f;   // finisher / heavy / beam: bigger
            Vector3 local = rig.transform.InverseTransformPoint(point);
            if (local.y / Mathf.Max(0.5f, rig.Height) > 0.8f)
            {
                s.FaceHits = Mathf.Min(1f, s.FaceHits + add * 0.6f);
                s.Dirty = true;
                return;
            }
            Vector3 bind = ToBind(rig, local, out Vector3 normal);
            AddBruise(s, bind, normal, add);
        }

        // the main bone segments a hit can land on (bone → the joint it reaches to)
        private static readonly (CharacterBone a, CharacterBone b)[] Segments =
        {
            (CharacterBone.Hips, CharacterBone.Spine), (CharacterBone.Spine, CharacterBone.Chest), (CharacterBone.Chest, CharacterBone.UpperChest),
            (CharacterBone.UpperChest, CharacterBone.Neck), (CharacterBone.Neck, CharacterBone.Head),
            (CharacterBone.LeftUpperArm, CharacterBone.LeftLowerArm), (CharacterBone.LeftLowerArm, CharacterBone.LeftHand),
            (CharacterBone.RightUpperArm, CharacterBone.RightLowerArm), (CharacterBone.RightLowerArm, CharacterBone.RightHand),
            (CharacterBone.LeftUpperLeg, CharacterBone.LeftLowerLeg), (CharacterBone.LeftLowerLeg, CharacterBone.LeftFoot),
            (CharacterBone.RightUpperLeg, CharacterBone.RightLowerLeg), (CharacterBone.RightLowerLeg, CharacterBone.RightFoot),
        };

        /// <summary>
        /// A point near the posed body (rig space) → the same spot in the bind pose: through the nearest bone segment (bones
        /// have identity bind rotations), pulled in towards the bone a little since the hit point sits on the hit capsule,
        /// outside the skin.
        /// </summary>
        private static Vector3 ToBind(CharacterRig rig, Vector3 p, out Vector3 normal)
        {
            float best = float.MaxValue;
            CharacterBone bone = CharacterBone.Spine;
            Vector3 closest = p;
            foreach (var (a, b) in Segments)
            {
                Vector3 pa = rig.ModelPosition(a), pb = rig.ModelPosition(b), ab = pb - pa;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - pa, ab) / ab.sqrMagnitude) : 0f;
                Vector3 c = pa + ab * t;
                float d = (p - c).sqrMagnitude;
                if (d < best) { best = d; bone = a; closest = c; }
            }
            Vector3 outward = p - closest;
            Quaternion toBind = Quaternion.Inverse(rig.GetModelRotation(bone));
            normal = outward.sqrMagnitude > 1e-8f ? (toBind * outward).normalized : Vector3.forward;
            p = closest + outward * 0.75f;
            return rig.BindPosition(bone) + toBind * (p - rig.ModelPosition(bone));
        }

        private static void AddBruise(State s, Vector3 bind, Vector3 normal, float add)
        {
            float merge = 0.08f * Mathf.Max(0.5f, s.Rig.Height) / 2.3f;
            for (int i = 0; i < s.Count; i++)
            {
                var b = s.Bruises[i];
                if ((new Vector3(b.x, b.y, b.z) - bind).sqrMagnitude > merge * merge) continue;
                // the same spot again: darker, centred a little towards the new hit
                Vector3 c = Vector3.Lerp(new Vector3(b.x, b.y, b.z), bind, 0.3f);
                s.Bruises[i] = new Vector4(c.x, c.y, c.z, Mathf.Min(1f, b.w + add * 0.7f));
                s.Dirty = true;
                return;
            }
            int slot = s.Count;
            if (slot >= MaxBruises)
            {
                slot = 0;
                for (int i = 1; i < MaxBruises; i++) if (s.Bruises[i].w < s.Bruises[slot].w) slot = i;
            }
            else s.Count++;
            s.Bruises[slot] = new Vector4(bind.x, bind.y, bind.z, add);
            s.Normals[slot] = new Vector4(normal.x, normal.y, normal.z, Random.Range(0f, 50f));
            s.Dirty = true;
        }

        private void LateUpdate()
        {
            _mpb ??= new MaterialPropertyBlock();
            _gone.Clear();
            foreach (var kv in _states) if (kv.Key == null || !PlayerStats.All.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var k in _gone) _states.Remove(k);

            float dt = Time.deltaTime;
            foreach (var p in PlayerStats.All)
            {
                if (p == null) continue;
                var s = Get(p);
                var rig = FindRigCached(p, s);
                float max = Mathf.Max(1f, p.MaxHealth), hp = p.Health.Value, frac = hp / max;
                // healed to full (respawn, the dummy's refill): everything goes
                if (hp >= max - 0.5f && (s.Count > 0 || s.FaceHits > 0f || s.Eye > 0.001f) && !p.IsDown.Value)
                {
                    s.Count = 0; s.FaceHits = 0f; s.Eye = 0f; s.Dirty = true;
                }
                if (Mathf.Abs(hp - s.LastHealth) > 0.01f) { s.LastHealth = hp; s.Dirty = true; }
                float eyeWant = Mathf.InverseLerp(EyeStart, EyeShut, frac);
                if (p.IsDown.Value) eyeWant = 1f;
                float eye = Mathf.MoveTowards(s.Eye, eyeWant, dt / (eyeWant > s.Eye ? 0.5f : 0.2f));
                if (!Mathf.Approximately(eye, s.Eye)) { s.Eye = eye; s.Dirty = true; }
                if (rig == null || rig.Body == null || !s.Dirty) continue;
                s.Dirty = false;

                float low = Mathf.Clamp01((BruiseBelow - frac) / BruiseBelow);   // 0 above 60 % health .. 1 at 0
                float face = Mathf.Clamp01(Mathf.Max(s.FaceHits, low * 0.9f));
                bool any = s.Count > 0 || face > 0.001f || s.Eye > 0.001f;
                if (!any && s.Clean) continue;
                s.Clean = !any;
                var body = rig.Body;
                foreach (int slot in rig.SkinSlots)
                {
                    body.GetPropertyBlock(_mpb, slot);
                    _mpb.SetVectorArray(BruisesId, s.Bruises);
                    _mpb.SetVectorArray(BruiseNId, s.Normals);
                    _mpb.SetVector(BruiseParamsId, new Vector4(s.Count, 0.06f * rig.Height / 2.3f, 1f + low, rig.Height / 2.3f));
                    body.SetPropertyBlock(_mpb, slot);
                }
                if (rig.FaceSlot >= 0)
                {
                    body.GetPropertyBlock(_mpb, rig.FaceSlot);
                    _mpb.SetVector(InjuryId, new Vector4(face, s.Eye, s.EyeSide, 0f));
                    body.SetPropertyBlock(_mpb, rig.FaceSlot);
                }
            }
        }

        // ---------------- preview (dev screenshots: -injuryCapture) ----------------

        // where the preview bruises sit: bone + offset from its bind position (front = +Z, the fighter's right = +X), strength
        private static readonly (CharacterBone bone, Vector3 off, float w)[] PreviewSpots =
        {
            (CharacterBone.UpperChest, new Vector3(-0.09f, -0.02f, 0.13f), 0.7f),   // left pec
            (CharacterBone.Spine, new Vector3(0.06f, 0.04f, 0.14f), 0.85f),        // gut, right of centre
            (CharacterBone.RightUpperArm, new Vector3(0.05f, -0.12f, 0.03f), 0.5f), // right upper arm
            (CharacterBone.LeftLowerArm, new Vector3(-0.02f, -0.1f, 0.04f), 0.6f),  // left forearm (blocking)
            (CharacterBone.Chest, new Vector3(0.14f, 0.02f, 0.06f), 0.55f),         // right ribs
            (CharacterBone.LeftUpperLeg, new Vector3(-0.04f, -0.15f, 0.08f), 0.45f),// left thigh
            (CharacterBone.RightLowerArm, new Vector3(0.02f, -0.12f, 0.04f), 0.4f), // right forearm
            (CharacterBone.Hips, new Vector3(-0.12f, 0.08f, 0.1f), 0.5f),           // left hip
        };

        /// <summary>
        /// Shows injuries on a rig without a fight (screenshots): the first <paramref name="bruises"/> preset bruises and the
        /// low-health look for <paramref name="healthFraction"/> (face bruises below 60 %, the eye swelling shut under 30 %).
        /// </summary>
        public static void ApplyPreview(CharacterRig rig, float healthFraction, int bruises)
        {
            if (rig == null || rig.Body == null) return;
            var arr = new Vector4[MaxBruises];
            var nrm = new Vector4[MaxBruises];
            int n = Mathf.Clamp(bruises, 0, Mathf.Min(MaxBruises, PreviewSpots.Length));
            float hs = rig.Height / 2.3f;
            for (int i = 0; i < n; i++)
            {
                var (bone, off, w) = PreviewSpots[i];
                Vector3 p = rig.BindPosition(bone) + off * hs;
                arr[i] = new Vector4(p.x, p.y, p.z, w);
                Vector3 outward = new Vector3(off.x, 0f, off.z).normalized;
                nrm[i] = new Vector4(outward.x, outward.y, outward.z, i * 7.3f + 1f);
            }
            float low = Mathf.Clamp01((BruiseBelow - healthFraction) / BruiseBelow);
            float eye = Mathf.InverseLerp(EyeStart, EyeShut, healthFraction);
            var mpb = new MaterialPropertyBlock();
            foreach (int slot in rig.SkinSlots)
            {
                rig.Body.GetPropertyBlock(mpb, slot);
                mpb.SetVectorArray(BruisesId, arr);
                mpb.SetVectorArray(BruiseNId, nrm);
                mpb.SetVector(BruiseParamsId, new Vector4(n, 0.06f * hs, 1f + low, hs));
                rig.Body.SetPropertyBlock(mpb, slot);
            }
            if (rig.FaceSlot >= 0)
            {
                rig.Body.GetPropertyBlock(mpb, rig.FaceSlot);
                mpb.SetVector(InjuryId, new Vector4(Mathf.Clamp01(low * 0.9f), eye, 1f, 0f));
                rig.Body.SetPropertyBlock(mpb, rig.FaceSlot);
            }
        }

        // the hero is rebuilt when its looks change: pick up the new rig (and push the injuries onto it again)
        private static CharacterRig FindRigCached(PlayerStats p, State s)
        {
            if (s.Rig != null && s.Rig.Body != null) return s.Rig;
            s.Rig = FindRig(p);
            if (s.Rig != null) { s.Dirty = true; s.Clean = false; }
            return s.Rig;
        }
    }
}
