using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// One full-body pose of a basic attack, in "attack space" (the fighter's facing; +Z forward, +X right, metres of the 2.3 m
    /// design body, scaled by rig height / 2.3 at play time). Limb targets are attack-space points, so a planted foot stays put
    /// while the body turns. Root = the whole model (Graphics/Model) offset and turn. Port of the Attack Bench
    /// (docs/bench/attack-bench.html, https://claude.ai/artifact/Btq5eM1mhUCJLQmYprZ2mB).
    /// </summary>
    public sealed class AttackPose
    {
        public Vector3 RootPos; public float RootYaw, RootPitch, RootRoll;
        public Vector3 HipsPos; public float HipsYaw, HipsPitch, HipsRoll;
        public float TorsoYaw, TorsoPitch, TorsoRoll;
        public float HeadYaw, HeadPitch;
        public Vector3 ArmL, ArmR; public float TwistL, TwistR;
        /// <summary>Hands: 0 = fist (every attack), 1 = open flat hand (the run's arm thrown back).</summary>
        public float OpenL, OpenR;
        /// <summary>
        /// Where the elbow points, in the chest's frame (+x right, +y up, +z forward); the length (0..1) is how much it overrides
        /// the solver's own elbow (<see cref="AttackPoseSolver.ElbowPole"/>). Zero = the solver's. The heavy's wind-up uses it to
        /// keep the elbow pointing at the floor while the chest leans far forward (the solver's own "down" tilts with the chest).
        /// </summary>
        public Vector3 ElbowL, ElbowR;
        public Vector3 LegL, LegR; public float FootYawL, FootYawR, FootPitchL, FootPitchR;

        public AttackPose Clone() => (AttackPose)MemberwiseClone();

        public Quaternion RootRotation => Quaternion.AngleAxis(RootYaw, Vector3.up) * Quaternion.AngleAxis(RootPitch, Vector3.right) * Quaternion.AngleAxis(RootRoll, Vector3.forward);

        public static void Lerp(AttackPose a, AttackPose b, float t, AttackPose o)
        {
            o.RootPos = Vector3.LerpUnclamped(a.RootPos, b.RootPos, t);
            o.RootYaw = L(a.RootYaw, b.RootYaw); o.RootPitch = L(a.RootPitch, b.RootPitch); o.RootRoll = L(a.RootRoll, b.RootRoll);
            o.HipsPos = Vector3.LerpUnclamped(a.HipsPos, b.HipsPos, t);
            o.HipsYaw = L(a.HipsYaw, b.HipsYaw); o.HipsPitch = L(a.HipsPitch, b.HipsPitch); o.HipsRoll = L(a.HipsRoll, b.HipsRoll);
            o.TorsoYaw = L(a.TorsoYaw, b.TorsoYaw); o.TorsoPitch = L(a.TorsoPitch, b.TorsoPitch); o.TorsoRoll = L(a.TorsoRoll, b.TorsoRoll);
            o.HeadYaw = L(a.HeadYaw, b.HeadYaw); o.HeadPitch = L(a.HeadPitch, b.HeadPitch);
            o.ArmL = Vector3.LerpUnclamped(a.ArmL, b.ArmL, t); o.ArmR = Vector3.LerpUnclamped(a.ArmR, b.ArmR, t);
            o.TwistL = L(a.TwistL, b.TwistL); o.TwistR = L(a.TwistR, b.TwistR);
            o.OpenL = L(a.OpenL, b.OpenL); o.OpenR = L(a.OpenR, b.OpenR);
            o.ElbowL = Vector3.LerpUnclamped(a.ElbowL, b.ElbowL, t); o.ElbowR = Vector3.LerpUnclamped(a.ElbowR, b.ElbowR, t);
            o.LegL = Vector3.LerpUnclamped(a.LegL, b.LegL, t); o.LegR = Vector3.LerpUnclamped(a.LegR, b.LegR, t);
            o.FootYawL = L(a.FootYawL, b.FootYawL); o.FootYawR = L(a.FootYawR, b.FootYawR);
            o.FootPitchL = L(a.FootPitchL, b.FootPitchL); o.FootPitchR = L(a.FootPitchR, b.FootPitchR);
            float L(float x, float y) => x + (y - x) * t;
        }

        /// <summary>o = wa·a + wb·b + wc·c + wd·d, field by field (spline weights; they sum to 1). o may not be one of the inputs.</summary>
        public static void Combine(AttackPose a, AttackPose b, AttackPose c, AttackPose d, float wa, float wb, float wc, float wd, AttackPose o)
        {
            o.RootPos = a.RootPos * wa + b.RootPos * wb + c.RootPos * wc + d.RootPos * wd;
            o.RootYaw = S(a.RootYaw, b.RootYaw, c.RootYaw, d.RootYaw); o.RootPitch = S(a.RootPitch, b.RootPitch, c.RootPitch, d.RootPitch);
            o.RootRoll = S(a.RootRoll, b.RootRoll, c.RootRoll, d.RootRoll);
            o.HipsPos = a.HipsPos * wa + b.HipsPos * wb + c.HipsPos * wc + d.HipsPos * wd;
            o.HipsYaw = S(a.HipsYaw, b.HipsYaw, c.HipsYaw, d.HipsYaw); o.HipsPitch = S(a.HipsPitch, b.HipsPitch, c.HipsPitch, d.HipsPitch);
            o.HipsRoll = S(a.HipsRoll, b.HipsRoll, c.HipsRoll, d.HipsRoll);
            o.TorsoYaw = S(a.TorsoYaw, b.TorsoYaw, c.TorsoYaw, d.TorsoYaw); o.TorsoPitch = S(a.TorsoPitch, b.TorsoPitch, c.TorsoPitch, d.TorsoPitch);
            o.TorsoRoll = S(a.TorsoRoll, b.TorsoRoll, c.TorsoRoll, d.TorsoRoll);
            o.HeadYaw = S(a.HeadYaw, b.HeadYaw, c.HeadYaw, d.HeadYaw); o.HeadPitch = S(a.HeadPitch, b.HeadPitch, c.HeadPitch, d.HeadPitch);
            o.ArmL = a.ArmL * wa + b.ArmL * wb + c.ArmL * wc + d.ArmL * wd; o.ArmR = a.ArmR * wa + b.ArmR * wb + c.ArmR * wc + d.ArmR * wd;
            o.TwistL = S(a.TwistL, b.TwistL, c.TwistL, d.TwistL); o.TwistR = S(a.TwistR, b.TwistR, c.TwistR, d.TwistR);
            o.OpenL = S(a.OpenL, b.OpenL, c.OpenL, d.OpenL); o.OpenR = S(a.OpenR, b.OpenR, c.OpenR, d.OpenR);
            o.ElbowL = a.ElbowL * wa + b.ElbowL * wb + c.ElbowL * wc + d.ElbowL * wd; o.ElbowR = a.ElbowR * wa + b.ElbowR * wb + c.ElbowR * wc + d.ElbowR * wd;
            o.LegL = a.LegL * wa + b.LegL * wb + c.LegL * wc + d.LegL * wd; o.LegR = a.LegR * wa + b.LegR * wb + c.LegR * wc + d.LegR * wd;
            o.FootYawL = S(a.FootYawL, b.FootYawL, c.FootYawL, d.FootYawL); o.FootYawR = S(a.FootYawR, b.FootYawR, c.FootYawR, d.FootYawR);
            o.FootPitchL = S(a.FootPitchL, b.FootPitchL, c.FootPitchL, d.FootPitchL); o.FootPitchR = S(a.FootPitchR, b.FootPitchR, c.FootPitchR, d.FootPitchR);
            float S(float x, float y, float z, float w) => x * wa + y * wb + z * wc + w * wd;
        }

        /// <summary>Left ↔ right: the same pose done by the other side (the second step of a walk or run cycle).</summary>
        public AttackPose Mirrored()
        {
            var o = new AttackPose();
            Mirror(this, o);
            return o;
        }

        /// <summary>o = <paramref name="a"/> done by the other side (left ↔ right), without allocating. o may not be a.</summary>
        public static void Mirror(AttackPose a, AttackPose o)
        {
            static Vector3 M(Vector3 v) => new(-v.x, v.y, v.z);
            o.RootPos = M(a.RootPos); o.RootYaw = -a.RootYaw; o.RootPitch = a.RootPitch; o.RootRoll = -a.RootRoll;
            o.HipsPos = M(a.HipsPos); o.HipsYaw = -a.HipsYaw; o.HipsPitch = a.HipsPitch; o.HipsRoll = -a.HipsRoll;
            o.TorsoYaw = -a.TorsoYaw; o.TorsoPitch = a.TorsoPitch; o.TorsoRoll = -a.TorsoRoll;
            o.HeadYaw = -a.HeadYaw; o.HeadPitch = a.HeadPitch;
            o.ArmL = M(a.ArmR); o.ArmR = M(a.ArmL); o.TwistL = -a.TwistR; o.TwistR = -a.TwistL; o.OpenL = a.OpenR; o.OpenR = a.OpenL;
            o.ElbowL = M(a.ElbowR); o.ElbowR = M(a.ElbowL);
            o.LegL = M(a.LegR); o.LegR = M(a.LegL); o.FootYawL = -a.FootYawR; o.FootYawR = -a.FootYawL; o.FootPitchL = a.FootPitchR; o.FootPitchR = a.FootPitchL;
        }
    }

    /// <summary>How the segment that arrives at a key moves: Snap = the strike (most of the travel in the first frame or two).</summary>
    public enum KeyEase : byte { Smooth, Snap, In }

    public sealed class AttackKey
    {
        public float T;
        public KeyEase Ease;
        public AttackPose Pose;
    }

    public enum StrikeLimb : byte { HandL, HandR, FootL, FootR }

    /// <summary>One hit of a basic combo: keyed poses plus when it lands and when its effects run (seconds from the press).</summary>
    public sealed class AttackMove
    {
        public string Name;
        public StrikeLimb Limb;
        /// <summary>When the strike lands (seconds).</summary>
        public float Hit;
        /// <summary>Speed lines on the striking limb run from x to y (seconds).</summary>
        public Vector2 Streak;
        /// <summary>Back in guard (seconds).</summary>
        public float Length;
        /// <summary>Finisher (bigger, longer hold).</summary>
        public bool Big;
        public AttackKey[] Keys;

        public static float Ease(KeyEase e, float u) => e switch
        {
            KeyEase.Snap => 1f - Mathf.Pow(1f - u, 5f),
            KeyEase.In => u * u,
            _ => 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u),
        };

        /// <summary>The pose at time t (seconds), written into <paramref name="o"/>.</summary>
        public void Sample(float t, AttackPose o)
        {
            if (t <= 0f) { AttackPose.Lerp(Keys[0].Pose, Keys[0].Pose, 0f, o); return; }
            for (int i = 1; i < Keys.Length; i++)
            {
                if (t > Keys[i].T) continue;
                float u = (t - Keys[i - 1].T) / Mathf.Max(1e-5f, Keys[i].T - Keys[i - 1].T);
                AttackPose.Lerp(Keys[i - 1].Pose, Keys[i].Pose, Ease(Keys[i].Ease, Mathf.Clamp01(u)), o);
                return;
            }
            var last = Keys[Keys.Length - 1].Pose;
            AttackPose.Lerp(last, last, 0f, o);
        }

        /// <summary>
        /// Like <see cref="Sample"/>, but the move starts from <paramref name="start"/> instead of its first key: a swing released
        /// from wherever the wind-up was (half drawn back after a tap, fully coiled after a long hold).
        /// </summary>
        public void SampleFrom(AttackPose start, float t, AttackPose o)
        {
            if (Keys.Length < 2 || t >= Keys[1].T) { Sample(t, o); return; }
            float u = Mathf.Clamp01(t / Mathf.Max(1e-5f, Keys[1].T));
            AttackPose.Lerp(start, Keys[1].Pose, Ease(Keys[1].Ease, u), o);
        }
    }

    /// <summary>
    /// A moveset's charged heavy attack. Press: <see cref="WindUp"/> plays once (the fist is pulled back) and its last pose is held,
    /// coiling deeper towards <see cref="Coiled"/> as the charge builds (the animator adds a tremble). Release: <see cref="Swing"/>
    /// strikes from whatever pose the wind-up was holding; its <see cref="AttackMove.Hit"/> is when the fist lands, which is also
    /// when the server resolves the hit.
    /// </summary>
    public sealed class HeavyAttack
    {
        public string Name;
        public StrikeLimb Limb;
        public AttackMove WindUp;
        public AttackPose Coiled;
        public AttackMove Swing;
    }

    /// <summary>A basic-attack moveset: the guard, one move per combo hit (played in order, wrapping), the charged heavy, the power-up and the surge.</summary>
    public sealed class BasicMoveset
    {
        public string Id, Name;
        public AttackPose Guard;
        public AttackMove[] Moves;
        public HeavyAttack Heavy;
        /// <summary>Holding Charge: the keys drop the fighter into the power-up stance and the last one is held.</summary>
        public AttackMove Charge;
        /// <summary>Power Surge: the burst, played once when it's activated.</summary>
        public AttackMove Surge;

        public AttackMove Move(int comboIndex) => Moves[((comboIndex % Moves.Length) + Moves.Length) % Moves.Length];
    }

    /// <summary>Whole-body reactions in attack space (like the moves), played by the victim's animator.</summary>
    public static class HitPoses
    {
        /// <summary>
        /// Sent flying by a charged heavy: folded over the blow (hips leading, chest and head forward), flying backwards (-Z) with the
        /// arms and legs trailing towards the attacker (+Z), hands open. The animator turns it to face back along the push and holds
        /// it while the fighter is in the air.
        /// </summary>
        public static readonly AttackPose Launched = new()
        {
            RootPos = new Vector3(0f, 0.12f, 0f), RootPitch = -34f, RootRoll = 4f,
            HipsPos = new Vector3(0f, -0.04f, -0.04f), HipsYaw = 4f, HipsPitch = 18f,
            TorsoYaw = -6f, TorsoPitch = 44f,
            HeadPitch = 30f,
            ArmL = new Vector3(-0.34f, 1.56f, 0.40f), TwistL = -10f, OpenL = 0.85f,
            ArmR = new Vector3(0.40f, 1.10f, 0.46f), TwistR = 10f, OpenR = 0.85f,
            LegL = new Vector3(-0.14f, 0.70f, 0.30f), FootYawL = -6f, FootPitchL = 35f,
            LegR = new Vector3(0.22f, 0.50f, 0.58f), FootYawR = 8f, FootPitchR = 30f,
        };
    }

    /// <summary>
    /// The basic-attack movesets. Built so fighters can later equip one from an inventory: everything that plays a basic
    /// attack asks for a set by id (<see cref="Get"/>), and adding a set is adding an entry to <see cref="All"/>. Today there is
    /// one, <see cref="Rush"/>, the default for every fighter.
    /// </summary>
    public static class BasicMovesets
    {
        public const string DefaultId = "rush";

        /// <summary>
        /// "Rush", from the owner's reference clip (a straight-punch rush): left straight, right straight, left body blow,
        /// right power straight (finisher). Numbers = the Attack Bench (the owner dropped the kick finisher). The left straight and
        /// the power straight go to the face (chin height, owner 2026-11-20: "have some of those hits aim for the head"), the
        /// right straight to the chest and the body blow to the gut, so the hit reactions differ along the combo.
        /// </summary>
        public static readonly BasicMoveset Rush = BuildRush();

        public static readonly IReadOnlyList<BasicMoveset> All = new[] { Rush };

        private static readonly Dictionary<string, BasicMoveset> ById = BuildIndex();

        public static BasicMoveset Get(string id) => id != null && ById.TryGetValue(id, out var s) ? s : Rush;

        private static Dictionary<string, BasicMoveset> BuildIndex()
        {
            var d = new Dictionary<string, BasicMoveset>();
            foreach (var s in All) d[s.Id] = s;
            return d;
        }

        private static BasicMoveset BuildRush()
        {
            var guard = new AttackPose
            {
                RootYaw = 12f,
                HipsPos = new Vector3(0f, -0.06f, 0f), HipsYaw = 6f, HipsPitch = 3f,
                TorsoYaw = 4f, TorsoPitch = 6f,
                HeadPitch = -6f,
                ArmL = new Vector3(-0.07f, 1.55f, 0.32f), TwistL = -20f,
                ArmR = new Vector3(0.12f, 1.49f, 0.20f), TwistR = 20f,
                LegL = new Vector3(-0.18f, 0.105f, 0.22f), FootYawL = 14f,
                LegR = new Vector3(0.22f, 0.105f, -0.22f), FootYawR = 34f,
            };
            AttackPose P(Action<AttackPose> edit = null) { var p = guard.Clone(); edit?.Invoke(p); return p; }
            AttackKey K(float t, KeyEase e, AttackPose p) => new() { T = t, Ease = e, Pose = p };

            var left = new AttackMove
            {
                Name = "Left straight", Limb = StrikeLimb.HandL, Hit = 0.07f, Streak = new Vector2(0.025f, 0.11f), Length = 0.28f,
                Keys = new[]
                {
                    K(0f, KeyEase.Smooth, P()),
                    K(0.03f, KeyEase.Smooth, P(p => { p.RootYaw = 14f; p.ArmL = new Vector3(-0.10f, 1.62f, 0.25f); })),
                    K(0.07f, KeyEase.Snap, P(p =>
                    {
                        p.RootYaw = 26f; p.RootPos = new Vector3(0f, 0f, 0.06f); p.HipsYaw = 6f; p.HipsPitch = 7f; p.TorsoYaw = 6f; p.TorsoPitch = 8f;
                        p.ArmL = new Vector3(-0.02f, 1.95f, 0.97f); p.TwistL = -60f; p.ArmR = new Vector3(0.14f, 1.46f, 0.12f); p.TwistR = 20f;
                        p.LegL = new Vector3(-0.18f, 0.105f, 0.30f);
                    })),
                    K(0.15f, KeyEase.Smooth, P(p =>
                    {
                        p.RootYaw = 25f; p.RootPos = new Vector3(0f, 0f, 0.06f); p.HipsYaw = 6f; p.HipsPitch = 7f; p.TorsoYaw = 6f; p.TorsoPitch = 8f;
                        p.ArmL = new Vector3(-0.03f, 1.94f, 0.91f); p.TwistL = -55f; p.ArmR = new Vector3(0.14f, 1.46f, 0.12f); p.TwistR = 20f;
                        p.LegL = new Vector3(-0.18f, 0.105f, 0.30f);
                    })),
                    K(0.28f, KeyEase.Smooth, P()),
                },
            };
            var right = new AttackMove
            {
                Name = "Right straight", Limb = StrikeLimb.HandR, Hit = 0.07f, Streak = new Vector2(0.025f, 0.11f), Length = 0.28f,
                Keys = new[]
                {
                    K(0f, KeyEase.Smooth, P()),
                    K(0.03f, KeyEase.Smooth, P(p => { p.RootYaw = 10f; p.ArmR = new Vector3(0.15f, 1.46f, 0.12f); })),
                    K(0.07f, KeyEase.Snap, P(p =>
                    {
                        p.RootYaw = -16f; p.RootPos = new Vector3(0f, 0f, 0.08f); p.HipsYaw = -8f; p.HipsPitch = 7f; p.TorsoYaw = -10f; p.TorsoPitch = 15f;
                        p.ArmR = new Vector3(0.03f, 1.56f, 1.00f); p.TwistR = 60f; p.ArmL = new Vector3(-0.13f, 1.47f, 0.14f); p.TwistL = -20f;
                        p.LegR = new Vector3(0.20f, 0.105f, -0.14f); p.FootYawR = 5f; p.FootPitchR = 20f; p.LegL = new Vector3(-0.18f, 0.105f, 0.28f);
                    })),
                    K(0.15f, KeyEase.Smooth, P(p =>
                    {
                        p.RootYaw = -15f; p.RootPos = new Vector3(0f, 0f, 0.08f); p.HipsYaw = -8f; p.HipsPitch = 7f; p.TorsoYaw = -10f; p.TorsoPitch = 15f;
                        p.ArmR = new Vector3(0.04f, 1.56f, 0.94f); p.TwistR = 55f; p.ArmL = new Vector3(-0.13f, 1.47f, 0.14f); p.TwistL = -20f;
                        p.LegR = new Vector3(0.20f, 0.105f, -0.14f); p.FootYawR = 5f; p.FootPitchR = 20f; p.LegL = new Vector3(-0.18f, 0.105f, 0.28f);
                    })),
                    K(0.28f, KeyEase.Smooth, P()),
                },
            };
            var body = new AttackMove
            {
                Name = "Left body blow", Limb = StrikeLimb.HandL, Hit = 0.08f, Streak = new Vector2(0.03f, 0.12f), Length = 0.30f,
                Keys = new[]
                {
                    K(0f, KeyEase.Smooth, P()),
                    K(0.03f, KeyEase.Smooth, P(p => { p.HipsPos = new Vector3(0f, -0.10f, 0f); p.ArmL = new Vector3(-0.12f, 1.40f, 0.22f); })),
                    K(0.08f, KeyEase.Snap, P(p =>
                    {
                        p.RootYaw = 22f; p.RootPos = new Vector3(0f, 0f, 0.08f); p.HipsYaw = 6f; p.HipsPitch = 8f; p.HipsPos = new Vector3(0f, -0.15f, 0f);
                        p.TorsoYaw = 6f; p.TorsoPitch = 16f;
                        p.ArmL = new Vector3(-0.02f, 1.20f, 0.98f); p.TwistL = -60f; p.ArmR = new Vector3(0.12f, 1.50f, 0.16f); p.TwistR = 20f;
                        p.LegL = new Vector3(-0.18f, 0.105f, 0.34f);
                    })),
                    K(0.16f, KeyEase.Smooth, P(p =>
                    {
                        p.RootYaw = 21f; p.RootPos = new Vector3(0f, 0f, 0.08f); p.HipsYaw = 6f; p.HipsPitch = 8f; p.HipsPos = new Vector3(0f, -0.15f, 0f);
                        p.TorsoYaw = 6f; p.TorsoPitch = 16f;
                        p.ArmL = new Vector3(-0.03f, 1.21f, 0.92f); p.TwistL = -55f; p.ArmR = new Vector3(0.12f, 1.50f, 0.16f); p.TwistR = 20f;
                        p.LegL = new Vector3(-0.18f, 0.105f, 0.34f);
                    })),
                    K(0.30f, KeyEase.Smooth, P()),
                },
            };
            var power = new AttackMove
            {
                Name = "Right power straight", Limb = StrikeLimb.HandR, Hit = 0.10f, Streak = new Vector2(0.04f, 0.14f), Length = 0.40f, Big = true,
                Keys = new[]
                {
                    K(0f, KeyEase.Smooth, P()),
                    K(0.05f, KeyEase.Smooth, P(p => { p.RootYaw = 20f; p.HipsPos = new Vector3(0f, -0.09f, 0f); p.ArmR = new Vector3(0.17f, 1.52f, 0.08f); })),
                    K(0.10f, KeyEase.Snap, P(p =>
                    {
                        p.RootYaw = -22f; p.RootPos = new Vector3(0f, 0f, 0.16f); p.HipsYaw = -10f; p.HipsPitch = 9f; p.HipsPos = new Vector3(0f, -0.10f, 0f);
                        p.TorsoYaw = -12f; p.TorsoPitch = 10f;
                        p.ArmR = new Vector3(0.03f, 1.95f, 1.06f); p.TwistR = 60f; p.ArmL = new Vector3(-0.13f, 1.46f, 0.14f); p.TwistL = -20f;
                        p.LegR = new Vector3(0.20f, 0.105f, -0.06f); p.FootYawR = 5f; p.FootPitchR = 25f; p.LegL = new Vector3(-0.18f, 0.105f, 0.40f);
                    })),
                    K(0.24f, KeyEase.Smooth, P(p =>
                    {
                        p.RootYaw = -21f; p.RootPos = new Vector3(0f, 0f, 0.16f); p.HipsYaw = -10f; p.HipsPitch = 9f; p.HipsPos = new Vector3(0f, -0.10f, 0f);
                        p.TorsoYaw = -12f; p.TorsoPitch = 10f;
                        p.ArmR = new Vector3(0.04f, 1.94f, 1.00f); p.TwistR = 55f; p.ArmL = new Vector3(-0.13f, 1.46f, 0.14f); p.TwistL = -20f;
                        p.LegR = new Vector3(0.20f, 0.105f, -0.06f); p.FootYawR = 5f; p.FootPitchR = 25f; p.LegL = new Vector3(-0.18f, 0.105f, 0.40f);
                    })),
                    K(0.40f, KeyEase.Smooth, P()),
                },
            };
            // the charged heavy, from the owner's reference clip (2026-11-22: "base it on this video 1:1", the face close-ups
            // left out): a cocked haymaker to the face that launches the opponent, a long follow-through, then a slump. Traced on a
            // copy of the Locomotion Bench with the game's solver, each key rendered beside its frame from a matching camera.
            // The clip's wind-up shot is mirrored (the punch itself is a right), so it is done here with the right hand.
            // Hold: the fist comes up beside the cheek and clenches with the elbow pointing at the floor, then draws back out to the
            // side as the chest turns 48° away and leans in, the head on the target, the left arm hanging loose. Full charge coils
            // deeper (fist further back, body lower, more lean) with the elbow still down. Release: the elbow stays low as the arm
            // drives forward, the fist lands on the opponent's face with the head thrown back, sweeps on up and across the face to
            // the left (the clip's long held follow-through: arm straight over the head, left arm out low, chest arched and leaning
            // left), then the arm drops in an arc out in front, the fighter slumps forward, arms hanging, and steps back into the guard.
            // The cocked arm (owner 2026-11-22: "the elbow should be down"; docs/bench/heavy-windup-bench.html,
            // https://claude.ai/artifact/2M5MydQdsjRYCsoU7AUQJB): the elbow stays 17-25 cm below the shoulder for the whole wind-up
            // and the upper arm rolls 67° at most. Placed from the posed shoulder, aimed at the most downward direction square to
            // shoulder→fist and turned 15-25° outward, which keeps it out of the torso.
            var raise = P(p =>
            {
                p.RootPos = new Vector3(0f, 0f, -0.02f); p.RootYaw = 18f; p.RootPitch = 2f; p.RootRoll = 0f;
                p.HipsPos = new Vector3(0f, -0.10f, -0.01f); p.HipsYaw = 6f; p.HipsPitch = 5f; p.HipsRoll = 0f;
                p.TorsoYaw = 8f; p.TorsoPitch = 12f; p.TorsoRoll = -2f; p.HeadYaw = 0f; p.HeadPitch = 0f;
                p.ArmR = new Vector3(0.393f, 1.631f, 0.219f); p.TwistR = 20f; p.OpenR = 0f; p.ElbowR = new Vector3(0.259f, -0.795f, 0.548f);
                p.ArmL = new Vector3(-0.12f, 1.28f, 0.32f); p.TwistL = -15f; p.OpenL = 0.1f;
                // the lead foot steps out a little (lifted mid-step); the back foot stays where the guard has it
                p.LegL = new Vector3(-0.19f, 0.15f, 0.30f); p.FootYawL = 16f; p.FootPitchL = 0f;
                p.LegR = new Vector3(0.22f, 0.105f, -0.22f); p.FootYawR = 34f; p.FootPitchR = 0f;
            });
            var cock = P(p =>
            {
                p.RootPos = new Vector3(0f, 0f, -0.06f); p.RootYaw = 28f; p.RootPitch = 8f; p.RootRoll = 0f;
                p.HipsPos = new Vector3(0f, -0.15f, -0.04f); p.HipsYaw = 8f; p.HipsPitch = 12f; p.HipsRoll = 0f;
                p.TorsoYaw = 12f; p.TorsoPitch = 32f; p.TorsoRoll = -4f; p.HeadYaw = 0f; p.HeadPitch = 0f;
                p.ArmR = new Vector3(0.608f, 1.482f, 0.259f); p.TwistR = 20f; p.OpenR = 0f; p.ElbowR = new Vector3(0.298f, -0.49f, 0.819f);
                p.ArmL = new Vector3(-0.06f, 1.02f, 0.36f); p.TwistL = -10f; p.OpenL = 0.15f;
                p.LegL = new Vector3(-0.20f, 0.105f, 0.36f); p.FootYawL = 18f; p.FootPitchL = 0f;
                p.LegR = new Vector3(0.22f, 0.105f, -0.22f); p.FootYawR = 40f; p.FootPitchR = 0f;
            });
            var coiled = P(p =>
            {
                p.RootPos = new Vector3(0f, 0f, -0.10f); p.RootYaw = 33f; p.RootPitch = 10f; p.RootRoll = 0f;
                p.HipsPos = new Vector3(0f, -0.22f, -0.06f); p.HipsYaw = 10f; p.HipsPitch = 14f; p.HipsRoll = 0f;
                p.TorsoYaw = 13f; p.TorsoPitch = 36f; p.TorsoRoll = -5f; p.HeadYaw = 0f; p.HeadPitch = -2f;
                p.ArmR = new Vector3(0.671f, 1.367f, 0.167f); p.TwistR = 20f; p.OpenR = 0f; p.ElbowR = new Vector3(0.352f, -0.426f, 0.833f);
                p.ArmL = new Vector3(-0.05f, 0.98f, 0.38f); p.TwistL = -10f; p.OpenL = 0.15f;
                p.LegL = new Vector3(-0.22f, 0.105f, 0.42f); p.FootYawL = 20f; p.FootPitchL = 0f;
                p.LegR = new Vector3(0.24f, 0.105f, -0.26f); p.FootYawR = 44f; p.FootPitchR = 0f;
            });
            // the follow-through, struck and then held (the head comes down a touch, the arm sinks)
            AttackPose Follow(float torsoPitch, float torsoRoll, float headYaw, float headPitch, Vector3 armR) => P(p =>
            {
                p.RootPos = new Vector3(0f, 0f, 0.44f); p.RootYaw = -26f; p.RootPitch = -3f; p.RootRoll = 0f;
                p.HipsPos = new Vector3(-0.06f, -0.16f, 0f); p.HipsYaw = -8f; p.HipsPitch = 0f; p.HipsRoll = 0f;
                p.TorsoYaw = -10f; p.TorsoPitch = torsoPitch; p.TorsoRoll = torsoRoll; p.HeadYaw = headYaw; p.HeadPitch = headPitch;
                p.ArmR = armR; p.TwistR = 50f; p.OpenR = 0f;
                p.ArmL = new Vector3(-0.589f, 1.171f, -0.047f); p.TwistL = -20f; p.OpenL = 0.1f;
                p.LegL = new Vector3(-0.24f, 0.105f, 0.80f); p.FootYawL = -10f; p.FootPitchL = 0f;
                p.LegR = new Vector3(0.30f, 0.105f, -0.02f); p.FootYawR = 0f; p.FootPitchR = 35f;
            });
            // the slump after it: bent over the front knee, head hanging, both arms loose
            AttackPose Slump(float hipsY, float hipsPitch, float torsoPitch, float headPitch, Vector3 armR, Vector3 armL) => P(p =>
            {
                p.RootPos = new Vector3(0f, 0f, 0.36f); p.RootYaw = -10f; p.RootPitch = 8f; p.RootRoll = 0f;
                p.HipsPos = new Vector3(0f, hipsY, 0f); p.HipsYaw = -4f; p.HipsPitch = hipsPitch; p.HipsRoll = 0f;
                p.TorsoYaw = -4f; p.TorsoPitch = torsoPitch; p.TorsoRoll = 0f; p.HeadYaw = 0f; p.HeadPitch = headPitch;
                p.ArmR = armR; p.TwistR = 20f; p.OpenR = 0.35f;
                p.ArmL = armL; p.TwistL = -20f; p.OpenL = 0.35f;
                // the feet stay where the follow-through planted them (the back heel comes down)
                p.LegL = new Vector3(-0.24f, 0.105f, 0.80f); p.FootYawL = 0f; p.FootPitchL = 0f;
                p.LegR = new Vector3(0.30f, 0.105f, -0.02f); p.FootYawR = 14f; p.FootPitchR = 10f;
            });
            var held = Follow(-12f, 20f, -45f, -34f, new Vector3(-0.323f, 1.797f, 0.30f));
            var slump = Slump(-0.27f, 22f, 40f, 40f, new Vector3(0.121f, 0.673f, 1.01f), new Vector3(-0.366f, 0.695f, 0.825f));
            // the arm drops from over the head in an arc out in front (straight across to the slump it folded into the stomach and
            // grazed the chest); the body halfway. Eased in then out (In, Snap) at matched speeds, so it doesn't stall at this key.
            var drop = new AttackPose();
            AttackPose.Lerp(held, slump, 0.5f, drop);
            drop.ArmR = new Vector3(-0.048f, 1.407f, 1.169f);
            var heavy = new HeavyAttack
            {
                Name = "Charged punch", Limb = StrikeLimb.HandR, Coiled = coiled,
                WindUp = new AttackMove
                {
                    Name = "Charged punch wind-up", Limb = StrikeLimb.HandR, Hit = 0.30f, Streak = new Vector2(-1f, -1f), Length = 0.30f,
                    Keys = new[]
                    {
                        K(0f, KeyEase.Smooth, P()),
                        // the fist comes up beside the cheek and clenches, the elbow down
                        K(0.10f, KeyEase.Smooth, raise),
                        // cocked: the elbow down, the fist drawn back out to the side at shoulder height, leaning in
                        K(0.30f, KeyEase.Smooth, cock),
                    },
                },
                Swing = new AttackMove
                {
                    Name = "Charged punch", Limb = StrikeLimb.HandR, Hit = 0.10f, Streak = new Vector2(0.03f, 0.20f), Length = 1.02f, Big = true,
                    Keys = new[]
                    {
                        K(0f, KeyEase.Smooth, cock),
                        // the elbow stays low as the arm drives forward past the face, the lead foot driving in
                        K(0.05f, KeyEase.In, P(p =>
                        {
                            p.RootPos = new Vector3(0f, 0f, 0.12f); p.RootYaw = 8f; p.RootPitch = 4f; p.RootRoll = 0f;
                            p.HipsPos = new Vector3(0f, -0.19f, 0f); p.HipsYaw = -2f; p.HipsPitch = 8f; p.HipsRoll = 0f;
                            p.TorsoYaw = 2f; p.TorsoPitch = 14f; p.TorsoRoll = 0f; p.HeadYaw = 0f; p.HeadPitch = -8f;
                            p.ArmR = new Vector3(0.26f, 1.58f, 0.52f); p.TwistR = 35f; p.OpenR = 0f; p.ElbowR = new Vector3(0.458f, -0.814f, 0.356f);
                            p.ArmL = new Vector3(-0.36f, 1.22f, -0.02f); p.TwistL = -25f; p.OpenL = 0.1f;
                            p.LegL = new Vector3(-0.21f, 0.16f, 0.60f); p.FootYawL = 8f; p.FootPitchL = 0f;
                            p.LegR = new Vector3(0.22f, 0.105f, -0.22f); p.FootYawR = 36f; p.FootPitchR = 15f;
                        })),
                        // impact: the fist on the opponent's face (it overreaches, so the arm is straight), the head thrown back,
                        // the left arm hanging out to the side, lunging onto the front foot with the back heel up
                        K(0.10f, KeyEase.Snap, P(p =>
                        {
                            p.RootPos = new Vector3(0f, 0f, 0.40f); p.RootYaw = -22f; p.RootPitch = 3f; p.RootRoll = 0f;
                            p.HipsPos = new Vector3(0f, -0.17f, 0f); p.HipsYaw = -8f; p.HipsPitch = 4f; p.HipsRoll = 0f;
                            p.TorsoYaw = -10f; p.TorsoPitch = -2f; p.TorsoRoll = 4f; p.HeadYaw = 0f; p.HeadPitch = -32f;
                            p.ArmR = new Vector3(0f, 1.92f, 1.20f); p.TwistR = 60f; p.OpenR = 0f;
                            p.ArmL = new Vector3(-0.461f, 1.033f, 0.293f); p.TwistL = -20f; p.OpenL = 0.3f;
                            p.LegL = new Vector3(-0.22f, 0.105f, 0.80f); p.FootYawL = -4f; p.FootPitchL = 0f;
                            p.LegR = new Vector3(0.24f, 0.105f, -0.06f); p.FootYawR = 6f; p.FootPitchR = 32f;
                        })),
                        // the fist sweeps on, up and to the left, the arm straight
                        K(0.16f, KeyEase.Smooth, P(p =>
                        {
                            p.RootPos = new Vector3(0f, 0f, 0.43f); p.RootYaw = -26f; p.RootPitch = 0f; p.RootRoll = 0f;
                            p.HipsPos = new Vector3(-0.03f, -0.17f, 0f); p.HipsYaw = -8f; p.HipsPitch = 2f; p.HipsRoll = 0f;
                            p.TorsoYaw = -10f; p.TorsoPitch = -8f; p.TorsoRoll = 12f; p.HeadYaw = -25f; p.HeadPitch = -38f;
                            p.ArmR = new Vector3(-0.191f, 1.836f, 0.851f); p.TwistR = 55f; p.OpenR = 0f;
                            p.ArmL = new Vector3(-0.519f, 1.096f, 0.102f); p.TwistL = -20f; p.OpenL = 0.2f;
                            p.LegL = new Vector3(-0.23f, 0.105f, 0.80f); p.FootYawL = -8f; p.FootPitchL = 0f;
                            p.LegR = new Vector3(0.27f, 0.105f, -0.04f); p.FootYawR = 3f; p.FootPitchR = 34f;
                        })),
                        // the follow-through: the arm straight across the face and over to the left, held while the opponent flies
                        K(0.24f, KeyEase.Smooth, Follow(-14f, 22f, -50f, -40f, new Vector3(-0.343f, 1.847f, 0.28f))),
                        K(0.52f, KeyEase.Smooth, held),
                        // the arm drops in an arc out in front, then the slump
                        K(0.57f, KeyEase.In, drop),
                        K(0.70f, KeyEase.Snap, slump),
                        K(0.82f, KeyEase.Smooth, Slump(-0.29f, 24f, 43f, 44f, new Vector3(0.116f, 0.629f, 1.027f), new Vector3(-0.372f, 0.652f, 0.842f))),
                        // back into the guard in two steps, so the lunge doesn't skate: the lead foot lifts and comes back...
                        K(0.92f, KeyEase.Smooth, P(p =>
                        {
                            p.RootPos = new Vector3(0f, 0f, 0.16f); p.RootYaw = 2f; p.RootPitch = 2f; p.RootRoll = 0f;
                            p.HipsPos = new Vector3(0f, -0.14f, 0f); p.HipsYaw = 2f; p.HipsPitch = 6f; p.HipsRoll = 0f;
                            p.TorsoYaw = 2f; p.TorsoPitch = 12f; p.TorsoRoll = 0f; p.HeadYaw = 0f; p.HeadPitch = 0f;
                            p.ArmR = new Vector3(0.14f, 1.30f, 0.42f); p.TwistR = 25f; p.OpenR = 0.1f;
                            p.ArmL = new Vector3(-0.10f, 1.38f, 0.34f); p.TwistL = -20f; p.OpenL = 0.05f;
                            p.LegL = new Vector3(-0.19f, 0.24f, 0.46f); p.FootYawL = 10f; p.FootPitchL = 0f;
                            p.LegR = new Vector3(0.30f, 0.105f, -0.02f); p.FootYawR = 14f; p.FootPitchR = 6f;
                        })),
                        // ...lands, and the back foot follows
                        K(0.97f, KeyEase.Smooth, P(p =>
                        {
                            p.RootPos = new Vector3(0f, 0f, 0.08f); p.RootYaw = 7f; p.RootPitch = 1f; p.RootRoll = 0f;
                            p.HipsPos = new Vector3(0f, -0.10f, 0f); p.HipsYaw = 4f; p.HipsPitch = 4.5f; p.HipsRoll = 0f;
                            p.TorsoYaw = 3f; p.TorsoPitch = 9f; p.TorsoRoll = 0f; p.HeadYaw = 0f; p.HeadPitch = -3f;
                            p.ArmR = new Vector3(0.13f, 1.395f, 0.31f); p.TwistR = 22.5f; p.OpenR = 0.05f;
                            p.ArmL = new Vector3(-0.085f, 1.465f, 0.33f); p.TwistL = -20f; p.OpenL = 0.025f;
                            p.LegL = new Vector3(-0.18f, 0.105f, 0.24f); p.FootYawL = 14f; p.FootPitchL = 0f;
                            p.LegR = new Vector3(0.24f, 0.17f, -0.12f); p.FootYawR = 30f; p.FootPitchR = 0f;
                        })),
                        K(1.02f, KeyEase.Smooth, P()),
                    },
                },
            };
            // the power-up and Power Surge, posed and keyed in Blender from the owner's two references
            // (ArtSource/Player/player.blend, actions Rush.charge and Rush.surge; exported with tools/attack_keys_blender.py)
            var charge = new AttackMove
            {
                Name = "Power-up", Limb = StrikeLimb.HandR, Hit = 0f, Streak = new Vector2(-1f, -1f), Length = 0.20f,
                Keys = new[]
                {
                    // the feet hop apart, the fists gather in front of the stomach...
                    K(0f, KeyEase.Smooth, new AttackPose
                    {
                        HipsPos = new Vector3(0f, -0.1f, -0.01f), HipsPitch = 3f, TorsoPitch = 10f, HeadPitch = 14f,
                        ArmL = new Vector3(-0.1841f, 1.3344f, 0.318f), TwistL = 20f,
                        ElbowL = new Vector3(-0.3446f, -0.8842f, -0.3152f), ArmR = new Vector3(0.1841f, 1.3344f, 0.318f),
                        TwistR = -20f, ElbowR = new Vector3(0.3446f, -0.8842f, -0.3152f), LegL = new Vector3(-0.31f, 0.17f, 0.02f),
                        FootYawL = -24f, LegR = new Vector3(0.31f, 0.17f, 0.02f), FootYawR = 24f,
                    }),
                    // ...and the stance lands: wide, knees out, head bowed, elbows down, fists out to the sides. Held while charging.
                    K(0.2f, KeyEase.Snap, new AttackPose
                    {
                        HipsPos = new Vector3(0f, -0.21f, -0.015f), HipsPitch = 6f, TorsoPitch = 22f, HeadPitch = 42f,
                        ArmL = new Vector3(-0.4787f, 1.2284f, 0.2631f), TwistL = 50f, ElbowL = new Vector3(0.3397f, -0.9398f, 0.0372f),
                        ArmR = new Vector3(0.4787f, 1.2284f, 0.2631f), TwistR = -50f,
                        ElbowR = new Vector3(-0.3397f, -0.9398f, 0.0372f), LegL = new Vector3(-0.44f, 0.105f, 0.05f), FootYawL = -44f,
                        LegR = new Vector3(0.44f, 0.105f, 0.05f), FootYawR = 44f,
                    }),
                },
            };
            var surge = new AttackMove
            {
                Name = "Power surge", Limb = StrikeLimb.HandR, Hit = 0f, Streak = new Vector2(-1f, -1f), Length = 1.12f,
                Keys = new[]
                {
                    // a quick dip, the fists chambered low...
                    K(0f, KeyEase.Smooth, new AttackPose
                    {
                        HipsPos = new Vector3(0f, -0.12f, -0.01f), HipsPitch = 4f, TorsoPitch = 12f, HeadPitch = 16f,
                        ArmL = new Vector3(-0.2938f, 1.311f, 0.3389f), TwistL = 20f,
                        ElbowL = new Vector3(-0.1125f, -0.9307f, -0.3481f), ArmR = new Vector3(0.2938f, 1.311f, 0.3389f),
                        TwistR = -20f, ElbowR = new Vector3(0.1125f, -0.9307f, -0.3481f), LegL = new Vector3(-0.31f, 0.17f, -0.02f),
                        FootYawL = -22f, LegR = new Vector3(0.3f, 0.17f, -0.02f), FootYawR = 26f,
                    }),
                    K(0.1f, KeyEase.Smooth, new AttackPose
                    {
                        HipsPos = new Vector3(0f, -0.2f, -0.02f), HipsPitch = 7f, TorsoPitch = 22f, HeadPitch = 32f,
                        ArmL = new Vector3(-0.3977f, 1.167f, 0.3316f), TwistL = 20f, ElbowL = new Vector3(0.0912f, -0.9545f, -0.2838f),
                        ArmR = new Vector3(0.3977f, 1.167f, 0.3316f), TwistR = -20f,
                        ElbowR = new Vector3(-0.0912f, -0.9545f, -0.2838f), LegL = new Vector3(-0.4f, 0.105f, -0.06f), FootYawL = -30f,
                        LegR = new Vector3(0.38f, 0.105f, -0.04f), FootYawR = 36f,
                    }),
                    // ...the burst, a touch past the pose...
                    K(0.2f, KeyEase.Snap, new AttackPose
                    {
                        HipsPos = new Vector3(-0.05f, -0.075f, 0.04f), HipsYaw = 2f, HipsPitch = -7f, TorsoYaw = -4f,
                        TorsoPitch = -18f, TorsoRoll = -5f, HeadYaw = -14f, HeadPitch = -30f,
                        ArmL = new Vector3(-0.4822f, 1.3022f, 0.0938f), TwistL = 30f,
                        ElbowL = new Vector3(0.2913f, -0.556f, -0.7784f), ArmR = new Vector3(0.3762f, 1.4929f, -0.0393f),
                        TwistR = -40f, ElbowR = new Vector3(0.0072f, -0.8441f, -0.5362f), LegL = new Vector3(-0.4f, 0.105f, -0.06f),
                        FootYawL = -30f, LegR = new Vector3(0.38f, 0.105f, -0.04f), FootYawR = 36f,
                    }),
                    // ...and the pose, held: standing tall, chest out, head thrown back, one fist by the shoulder and the other low
                    K(0.38f, KeyEase.Smooth, new AttackPose
                    {
                        HipsPos = new Vector3(-0.06f, -0.08f, 0.03f), HipsYaw = 2f, HipsPitch = -5f, TorsoYaw = -4f, TorsoPitch = -14f,
                        TorsoRoll = -5f, HeadYaw = -14f, HeadPitch = -25f, ArmL = new Vector3(-0.4822f, 1.3022f, 0.0938f),
                        TwistL = 30f, ElbowL = new Vector3(0.2913f, -0.556f, -0.7784f),
                        ArmR = new Vector3(0.3762f, 1.4929f, -0.0393f), TwistR = -40f,
                        ElbowR = new Vector3(0.0072f, -0.8441f, -0.5362f), LegL = new Vector3(-0.4f, 0.105f, -0.06f), FootYawL = -30f,
                        LegR = new Vector3(0.38f, 0.105f, -0.04f), FootYawR = 36f,
                    }),
                    K(0.88f, KeyEase.Smooth, new AttackPose
                    {
                        HipsPos = new Vector3(-0.06f, -0.08f, 0.03f), HipsYaw = 2f, HipsPitch = -5f, TorsoYaw = -4f, TorsoPitch = -14f,
                        TorsoRoll = -5f, HeadYaw = -14f, HeadPitch = -25f, ArmL = new Vector3(-0.4822f, 1.3022f, 0.0938f),
                        TwistL = 30f, ElbowL = new Vector3(0.2913f, -0.556f, -0.7784f),
                        ArmR = new Vector3(0.3762f, 1.4929f, -0.0393f), TwistR = -40f,
                        ElbowR = new Vector3(0.0072f, -0.8441f, -0.5362f), LegL = new Vector3(-0.4f, 0.105f, -0.06f), FootYawL = -30f,
                        LegR = new Vector3(0.38f, 0.105f, -0.04f), FootYawR = 36f,
                    }),
                    // the feet hop back in, into the guard
                    K(1f, KeyEase.Smooth, new AttackPose
                    {
                        RootYaw = 6.6f, HipsPos = new Vector3(-0.027f, -0.069f, 0.0135f), HipsYaw = 4.2f, HipsPitch = -0.6f,
                        TorsoYaw = 0.4f, TorsoPitch = -3f, TorsoRoll = -2.25f, HeadYaw = -6.3f, HeadPitch = -14.55f,
                        ArmL = new Vector3(-0.2555f, 1.4385f, 0.2182f), TwistL = 2.5f,
                        ElbowL = new Vector3(0.1311f, -0.2502f, -0.3503f), ArmR = new Vector3(0.2353f, 1.4913f, 0.0923f), TwistR = -7f,
                        ElbowR = new Vector3(0.0032f, -0.3798f, -0.2413f), LegL = new Vector3(-0.29f, 0.17f, 0.08f), FootYawL = -5.8f,
                        LegR = new Vector3(0.3f, 0.17f, -0.13f), FootYawR = 34.9f,
                    }),
                    K(1.12f, KeyEase.Smooth, P()),
                },
            };
            return new BasicMoveset
            {
                Id = DefaultId, Name = "Rush", Guard = guard, Moves = new[] { left, right, body, power }, Heavy = heavy, Charge = charge, Surge = surge,
            };
        }
    }

    /// <summary>Poses the rig to an <see cref="AttackPose"/> (port of the Attack Bench's applyPose). Writes model rotations only.</summary>
    public static class AttackPoseSolver
    {
        // the base body's torso round the spine (measured, 2.3 m design metres) plus room for the fist beyond the wrist
        private const float TorsoSide = 0.19f, TorsoFront = 0.135f, TorsoBack = 0.12f, FistRoom = 0.07f;

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// How much of the torso's girth there is at u along the spine (0 hips → 1 neck): full in between, fading to nothing over the
        /// first 10 % and the last 15 %. The cylinder used to stop dead at the neck, so a fist at chin height (the run's) was shoved
        /// sideways one frame and left alone the next, and the elbow jumped with it.
        /// </summary>
        private static float Girth(float u) => Smooth(0f, 0.1f, u) * Smooth(1f, 0.85f, u);

        /// <summary>
        /// Moves a wrist target out of the torso: an elliptic cylinder round the posed spine (hips → neck) in the chest's frame.
        /// Every pose and every blend between poses goes through this, so a fist can never sink into the chest or stomach.
        /// </summary>
        public static Vector3 KeepOutOfTorso(CharacterRig rig, Vector3 target, float hs) => KeepOutOfTorsoRoom(rig, target, FistRoom, hs);

        /// <summary>Is p inside the torso (the elliptic cylinder of <see cref="KeepOutOfTorso"/>) grown by room (design metres)?</summary>
        public static bool InsideTorso(CharacterRig rig, Vector3 p, float room, float hs)
        {
            Vector3 a = rig.ModelPosition(CharacterBone.Hips), b = rig.ModelPosition(CharacterBone.Neck), axis = b - a;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-6f) return false;
            float u = Vector3.Dot(p - a, axis) / len2;
            // the girth fades out just past the hips and the neck (an edge here made the elbow step)
            float g = Smooth(-0.05f, 0.05f, u) * Smooth(1.05f, 0.9f, u);
            if (g <= 0f) return false;
            Vector3 o = Quaternion.Inverse(rig.GetModelRotation(CharacterBone.Chest)) * (p - (a + axis * u));
            float rx = (TorsoSide + room) * hs * g, rz = ((o.z >= 0f ? TorsoFront : TorsoBack) + room) * hs * g;
            return (o.x / rx) * (o.x / rx) + (o.z / rz) * (o.z / rz) < 1f;
        }

        /// <summary>
        /// Turns a towards b round <paramref name="axis"/> (both perpendicular to it) by t: an even swing. A plain lerp between two
        /// far-apart aims barely moves and then flips through the middle in a frame (the heavy's elbow whipped up from the guard).
        /// Almost opposite, it goes round by <paramref name="outside"/> (the outside of the arm), not through the body.
        /// </summary>
        private static Vector3 TurnTowards(Vector3 a, Vector3 b, float t, Vector3 axis, Vector3 outside)
        {
            float ang = Mathf.Atan2(Vector3.Dot(axis, Vector3.Cross(a, b)), Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(ang) > 150f)
            {
                float alt = ang - Mathf.Sign(ang) * 360f;
                if (Vector3.Dot(Quaternion.AngleAxis(alt * 0.5f, axis) * a, outside) > Vector3.Dot(Quaternion.AngleAxis(ang * 0.5f, axis) * a, outside)) ang = alt;
            }
            return (Quaternion.AngleAxis(ang * t, axis) * a).normalized;
        }

        /// <summary>
        /// Where the elbow points (a pole for the hinge IK). The rest stance's elbow aim carried along with the arm's swing
        /// (<see cref="CharacterRig.RestArm"/>), so a swinging arm doesn't roll about itself and wring the shoulder; for big
        /// raises (70°-120° from rest and beyond: guard, fist at the chin) it blends to down, out and a little back in the chest's
        /// frame. A pose's own elbow aim (<paramref name="hint"/>, <see cref="AttackPose.ElbowL"/> / <see cref="AttackPose.ElbowR"/>
        /// in the chest's frame, its length the weight) overrides that, turned to round the arm (<see cref="TurnTowards"/>). If the
        /// elbow would sink into the torso (the run's fist under the chin with the body leant far forward), it swings round to the
        /// outside just far enough to clear it (found in tenths, then halved down to the edge, so it slides instead of stepping).
        /// </summary>
        public static Vector3 ElbowPole(CharacterRig rig, int side, Vector3 shoulder, Vector3 fist, Quaternion chest, float hs, Vector3 hint = default)
        {
            var ua = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperArm, side);
            float l1 = rig.BindLocal(ua + 1).magnitude, l2 = rig.BindLocal(ua + 2).magnitude;
            Vector3 to = fist - shoulder;
            float dist = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = to.sqrMagnitude > 1e-10f ? to.normalized : Vector3.down;
            Vector3 Perp(Vector3 v) { Vector3 w = Vector3.ProjectOnPlane(v, dir); return w.sqrMagnitude < 1e-6f ? Vector3.zero : w.normalized; }
            Vector3 outward = Perp(chest * new Vector3(side, -0.25f, 0f));
            if (outward == Vector3.zero) outward = Perp(chest * Vector3.down);
            Vector3 chestPole = Perp(chest * new Vector3(side * 0.5f, -0.7f, -0.25f));
            if (chestPole == Vector3.zero) chestPole = outward;
            rig.RestArm(side, out Vector3 restDir, out Vector3 restElbow);
            restDir = chest * restDir;
            Vector3 carried = Perp(Quaternion.FromToRotation(restDir, dir) * (chest * restElbow));
            if (carried == Vector3.zero) carried = chestPole;
            float swing = Vector3.Angle(restDir, dir);
            float tb = Mathf.Clamp01((swing - 70f) / 50f);
            Vector3 basePole = Vector3.Lerp(carried, chestPole, tb * tb * (3f - 2f * tb)).normalized;
            if (basePole == Vector3.zero) basePole = chestPole;
            float hw = hint.magnitude;
            if (hw > 1e-4f)
            {
                Vector3 aim = Perp(chest * hint);
                if (aim != Vector3.zero) basePole = TurnTowards(basePole, aim, Mathf.Min(1f, hw), dir, outward);
            }
            float angA = Mathf.Acos(Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f));
            Vector3 along = dir * (Mathf.Cos(angA) * l1);
            float off = Mathf.Sin(angA) * l1;
            bool Sinks(float t) => InsideTorso(rig, shoulder + along + TurnTowards(basePole, outward, t, dir, outward) * off, 0.05f, hs);
            float lo = 0f, hi = -1f;
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                if (!Sinks(t)) { hi = t; break; }
                lo = t;
            }
            if (hi > 0f)
                for (int k = 0; k < 6; k++)
                {
                    float m = (lo + hi) * 0.5f;
                    if (Sinks(m)) lo = m; else hi = m;
                }
            return shoulder + TurnTowards(basePole, outward, hi < 0f ? 1f : hi, dir, outward) * hs;
        }

        // the fist, measured on the base body: centre 7 cm past the wrist joint in the hand's frame, ~5.5 cm round (+1 cm margin)
        private static readonly Vector3 FistLocal = new(0.025f, -0.069f, 0.017f);
        private const float FistRadius = 0.065f;

        /// <summary>Centre of a posed fist (model space).</summary>
        public static Vector3 FistCentre(CharacterRig rig, int side, float hs)
        {
            var hand = (CharacterBone)CharacterBones.Side(CharacterBone.LeftHand, side);
            return rig.ModelPosition(hand) + rig.GetModelRotation(hand) * (new Vector3(side * FistLocal.x, FistLocal.y, FistLocal.z) * hs);
        }

        /// <summary>
        /// How far a fist centre must move to clear the body: the shoulder girdle (an ellipsoid round the posed shoulders and upper
        /// chest, 0.27 wide, 0.14 high, 0.14 / 0.13 deep, which the torso cylinder is too narrow for) and the torso cylinder. Zero
        /// when clear. The guard fist used to sink up to 5 cm into the front of its own shoulder during punches. A fist is let out
        /// through the girdle's front, so a guard stays in front of the chest instead of flying out wide; only well behind the
        /// shoulders' plane does it go out along the ellipsoid's radius (back / up / out), blended in between. The plain
        /// front-or-back choice flipped the run's fist at the chin (over the top of the leaning shoulders) to behind the neck and
        /// back every stride, the arm wringing round with it.
        /// </summary>
        public static Vector3 FistClearance(CharacterRig rig, Vector3 c, float hs)
        {
            Quaternion chest = rig.GetModelRotation(CharacterBone.UpperChest);
            Vector3 mid = (rig.ModelPosition(CharacterBone.LeftUpperArm) + rig.ModelPosition(CharacterBone.RightUpperArm)) * 0.5f
                          + chest * (new Vector3(0f, -0.02f, 0.02f) * hs);
            Vector3 o = Quaternion.Inverse(chest) * (c - mid);
            float ax = (0.27f + FistRadius) * hs, ay = (0.14f + FistRadius) * hs, az = ((o.z >= 0f ? 0.14f : 0.13f) + FistRadius) * hs;
            float s = (o.x / ax) * (o.x / ax) + (o.y / ay) * (o.y / ay) + (o.z / az) * (o.z / az);
            Vector3 push = Vector3.zero;
            if (s < 1f)
            {
                float zFront = Mathf.Sqrt(Mathf.Max(0f, 1f - (o.x / ax) * (o.x / ax) - (o.y / ay) * (o.y / ay))) * (0.14f + FistRadius) * hs;
                Vector3 front = new Vector3(0f, 0f, zFront - o.z), radial = o * (1f / Mathf.Sqrt(Mathf.Max(s, 1e-6f)) - 1f);
                push = chest * Vector3.Lerp(radial, front, Smooth(-0.6f * az, -0.2f * az, o.z));
            }
            Vector3 p = c + push;
            return push + (KeepOutOfTorsoRoom(rig, p, FistRadius, hs) - p);
        }

        private static Vector3 KeepOutOfTorsoRoom(CharacterRig rig, Vector3 target, float room, float hs)
        {
            Vector3 a = rig.ModelPosition(CharacterBone.Hips), b = rig.ModelPosition(CharacterBone.Neck), axis = b - a;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-6f) return target;
            float u = Vector3.Dot(target - a, axis) / len2;
            float g = Girth(u);
            if (g <= 0f) return target;
            Vector3 c = a + axis * u;
            Quaternion chest = rig.GetModelRotation(CharacterBone.Chest);
            Vector3 o = Quaternion.Inverse(chest) * (target - c);
            float rx = (TorsoSide + room) * hs * g, rz = ((o.z >= 0f ? TorsoFront : TorsoBack) + room) * hs * g;
            float k = (o.x / rx) * (o.x / rx) + (o.z / rz) * (o.z / rz);
            if (k >= 1f) return target;
            if (k < 1e-6f) o = new Vector3(0f, o.y, rz);
            else { float s = 1f / Mathf.Sqrt(k); o.x *= s; o.z *= s; }
            return c + chest * o;
        }

        /// <summary>
        /// Arm IK with the body in the way: collarbone follows, hinge IK from the posed shoulder, then the fist is checked against
        /// the shoulders and torso (<see cref="FistClearance"/>) and moved out and solved again (up to three passes).
        /// <paramref name="elbowHint"/> = the pose's own elbow aim (chest frame, zero = the solver's).
        /// </summary>
        private static void SolveArm(CharacterRig rig, int side, Vector3 fist, Quaternion chest, float weight, float hs, Quaternion? clavicleFrom, Vector3 elbowHint = default)
        {
            var sh = (CharacterBone)CharacterBones.Side(CharacterBone.LeftShoulder, side);
            var ua = sh + 1;
            Quaternion armU = rig.GetModelRotation(ua), armM = rig.GetModelRotation(ua + 1), armE = rig.GetModelRotation(ua + 2);
            for (int pass = 0; pass < 3; pass++)
            {
                if (clavicleFrom.HasValue) rig.SetModelRotation(sh, clavicleFrom.Value); else rig.Inherit(sh);
                Quaternion before = rig.GetModelRotation(sh);
                rig.FollowArm(side, fist - rig.ModelPosition(ua));
                rig.SetModelRotation(sh, Quaternion.Slerp(before, rig.GetModelRotation(sh), weight));
                // the arm starts each pass from the same rotations, so a partial weight blends from the incoming pose
                rig.SetModelRotation(ua, armU); rig.SetModelRotation(ua + 1, armM); rig.SetModelRotation(ua + 2, armE);
                Vector3 shoulder = rig.ModelPosition(ua);
                rig.SolveTwoBoneHinged(ua, fist, ElbowPole(rig, side, shoulder, fist, chest, hs, elbowHint), weight, Vector3.back);
                Vector3 push = FistClearance(rig, FistCentre(rig, side, hs), hs);
                if (push.magnitude < 0.002f * hs) break;
                fist += push * 1.15f;
            }
        }

        /// <summary>
        /// Where the knee points: the front of the leg, wherever the foot is (in front, under the hip or kicked up behind), turned
        /// with the foot's yaw. The old pole (in front of and above the foot) sat above the hip when the run threw a foot up behind,
        /// and the knee bent backwards.
        /// </summary>
        public static Vector3 KneePole(Vector3 hip, Vector3 foot, float footYaw, float hs)
        {
            Vector3 dir = (foot - hip).normalized, right = Quaternion.AngleAxis(footYaw, Vector3.up) * Vector3.right;
            Vector3 n = Vector3.Cross(dir, right);
            if (n.sqrMagnitude < 1e-6f) n = Quaternion.AngleAxis(footYaw, Vector3.up) * Vector3.forward;
            return (hip + foot) * 0.5f + n.normalized * (0.5f * hs);
        }

        /// <summary>
        /// One arm to a fist target (model space) over whatever pose the rig is in: kept out of the torso, the collarbone follows,
        /// hinge IK from the posed shoulder with <see cref="ElbowPole"/>. For the older action poses (block, charge, heavy, blast,
        /// beam, surge), which used the plain two-bone IK from the bind shoulder and could twist the forearm.
        /// </summary>
        public static void ArmTo(CharacterRig rig, int side, Vector3 target, float weight, float hs)
        {
            if (weight <= 0f) return;
            var sh = (CharacterBone)CharacterBones.Side(CharacterBone.LeftShoulder, side);
            Quaternion chest = rig.GetModelRotation(CharacterBone.UpperChest);
            Vector3 fist = KeepOutOfTorso(rig, target, hs);
            SolveArm(rig, side, fist, chest, weight, hs, rig.GetModelRotation(sh));
            rig.SetFingerCurl(side, 1f);
        }

        /// <summary>One leg to a foot target (model space) with <see cref="KneePole"/>, over the current pose.</summary>
        public static void LegTo(CharacterRig rig, int side, Vector3 target, float weight, float hs)
        {
            if (weight <= 0f) return;
            var ul = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, side);
            rig.SolveTwoBoneHinged(ul, target, KneePole(rig.ModelPosition(ul), target, 0f, hs), weight, Vector3.forward);
        }

        /// <summary>
        /// Overwrites the rig with the attack pose. <paramref name="hs"/> = rig height / 2.3 (design scale). The root offset / turn
        /// is NOT applied here (the caller moves Graphics/Model); limb targets are converted from attack space with it.
        /// </summary>
        public static void Apply(CharacterRig rig, AttackPose P, float hs)
        {
            Quaternion RQ = P.RootRotation, RQi = Quaternion.Inverse(RQ);
            Vector3 RP = P.RootPos * hs;
            Vector3 ToModel(Vector3 a) => RQi * (a * hs - RP);

            // hips + torso (the turn spread over three spine bones)
            rig[CharacterBone.Hips].localPosition = rig.BindLocal(CharacterBone.Hips) + P.HipsPos * hs;
            Quaternion H = Quaternion.AngleAxis(P.HipsYaw, Vector3.up) * Quaternion.AngleAxis(P.HipsPitch, Vector3.right) * Quaternion.AngleAxis(P.HipsRoll, Vector3.forward);
            rig.SetModelRotation(CharacterBone.Hips, H);
            Quaternion T(float f) => Quaternion.AngleAxis(P.TorsoYaw * f, Vector3.up) * Quaternion.AngleAxis(P.TorsoPitch * f, Vector3.right) * Quaternion.AngleAxis(P.TorsoRoll * f, Vector3.forward);
            rig.SetModelRotation(CharacterBone.Spine, H * T(0.35f));
            rig.SetModelRotation(CharacterBone.Chest, H * T(0.7f));
            rig.SetModelRotation(CharacterBone.UpperChest, H * T(1f));
            Quaternion chest = rig.GetModelRotation(CharacterBone.UpperChest);

            // head looks along the attack direction, at most 55° from the chest
            float chestYaw = P.HipsYaw + P.TorsoYaw;
            float headYaw = chestYaw + Mathf.Clamp(P.HeadYaw - P.RootYaw - chestYaw, -55f, 55f);
            Quaternion head = Quaternion.AngleAxis(headYaw, Vector3.up) * Quaternion.AngleAxis(P.HeadPitch, Vector3.right);
            rig.SetModelRotation(CharacterBone.Neck, Quaternion.Slerp(chest, head, 0.5f));
            rig.SetModelRotation(CharacterBone.Head, head);

            // arms: the collarbone follows the arm a little (CharacterRig.FollowArm), then hinge IK from it to the fist (elbows
            // down and out), fist roll over forearm + hand, fist (or the open hand of the run's arm thrown back)
            for (int side = -1; side <= 1; side += 2)
            {
                var sh = (CharacterBone)CharacterBones.Side(CharacterBone.LeftShoulder, side);
                var ua = sh + 1;
                rig.Inherit(sh);
                Vector3 fist = KeepOutOfTorso(rig, ToModel(side < 0 ? P.ArmL : P.ArmR), hs);
                rig.SetModelRotation(ua, chest); rig.SetModelRotation(ua + 1, chest); rig.SetModelRotation(ua + 2, chest);
                SolveArm(rig, side, fist, chest, 1f, hs, null, side < 0 ? P.ElbowL : P.ElbowR);
                float twist = side < 0 ? P.TwistL : P.TwistR;
                Vector3 fore = (rig.ModelPosition(ua + 2) - rig.ModelPosition(ua + 1)).normalized;
                Quaternion roll = Quaternion.AngleAxis(twist * 0.5f, fore);
                rig.SetModelRotation(ua + 1, roll * rig.GetModelRotation(ua + 1));
                rig.SetModelRotation(ua + 2, roll * rig.GetModelRotation(ua + 1));
                rig.SetFingerCurl(side, 1f - Mathf.Clamp01(side < 0 ? P.OpenL : P.OpenR));
            }

            // legs: hinge IK to the foot targets, knees towards the toes; planted foot flat, lifted foot square to the shin;
            // a planted foot up on its toes (pitch > 0, push-off) keeps the toes flat on the floor
            for (int side = -1; side <= 1; side += 2)
            {
                var ul = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, side);
                Vector3 t = side < 0 ? P.LegL : P.LegR;
                float fy = (side < 0 ? P.FootYawL : P.FootYawR) - P.RootYaw, fp = side < 0 ? P.FootPitchL : P.FootPitchR;
                rig.SetModelRotation(ul, H); rig.SetModelRotation(ul + 1, H); rig.SetModelRotation(ul + 2, H);
                Vector3 foot = ToModel(t);
                rig.SolveTwoBoneHinged(ul, foot, KneePole(rig.ModelPosition(ul), foot, fy, hs), 1f, Vector3.forward);
                Quaternion planted = Quaternion.AngleAxis(fy, Vector3.up) * Quaternion.AngleAxis(fp, Vector3.right);
                Quaternion lifted = rig.GetModelRotation(ul + 1) * Quaternion.AngleAxis(fp, Vector3.right);
                float lift = AttackMove.Ease(KeyEase.Smooth, Mathf.Clamp01((t.y - 0.2f) / 0.25f));
                Quaternion footRot = Quaternion.Slerp(planted, lifted, lift);
                rig.SetModelRotation(ul + 2, footRot);
                if (fp > 0f) rig.SetModelRotation(ul + 3, Quaternion.Slerp(Quaternion.AngleAxis(fy, Vector3.up), footRot, lift));
                else rig.Inherit(ul + 3);
            }
        }
    }
}
