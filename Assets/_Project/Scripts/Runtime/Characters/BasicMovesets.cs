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
            o.LegL = a.LegL * wa + b.LegL * wb + c.LegL * wc + d.LegL * wd; o.LegR = a.LegR * wa + b.LegR * wb + c.LegR * wc + d.LegR * wd;
            o.FootYawL = S(a.FootYawL, b.FootYawL, c.FootYawL, d.FootYawL); o.FootYawR = S(a.FootYawR, b.FootYawR, c.FootYawR, d.FootYawR);
            o.FootPitchL = S(a.FootPitchL, b.FootPitchL, c.FootPitchL, d.FootPitchL); o.FootPitchR = S(a.FootPitchR, b.FootPitchR, c.FootPitchR, d.FootPitchR);
            float S(float x, float y, float z, float w) => x * wa + y * wb + z * wc + w * wd;
        }

        /// <summary>Left ↔ right: the same pose done by the other side (the second step of a walk or run cycle).</summary>
        public AttackPose Mirrored()
        {
            static Vector3 M(Vector3 v) => new(-v.x, v.y, v.z);
            return new AttackPose
            {
                RootPos = M(RootPos), RootYaw = -RootYaw, RootPitch = RootPitch, RootRoll = -RootRoll,
                HipsPos = M(HipsPos), HipsYaw = -HipsYaw, HipsPitch = HipsPitch, HipsRoll = -HipsRoll,
                TorsoYaw = -TorsoYaw, TorsoPitch = TorsoPitch, TorsoRoll = -TorsoRoll,
                HeadYaw = -HeadYaw, HeadPitch = HeadPitch,
                ArmL = M(ArmR), ArmR = M(ArmL), TwistL = -TwistR, TwistR = -TwistL, OpenL = OpenR, OpenR = OpenL,
                LegL = M(LegR), LegR = M(LegL), FootYawL = -FootYawR, FootYawR = -FootYawL, FootPitchL = FootPitchR, FootPitchR = FootPitchL,
            };
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
    }

    /// <summary>A basic-attack moveset: the guard plus one move per combo hit (played in order, wrapping).</summary>
    public sealed class BasicMoveset
    {
        public string Id, Name;
        public AttackPose Guard;
        public AttackMove[] Moves;

        public AttackMove Move(int comboIndex) => Moves[((comboIndex % Moves.Length) + Moves.Length) % Moves.Length];
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
        /// right power straight (finisher). Numbers = the Attack Bench (the owner dropped the kick finisher).
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
                    K(0.03f, KeyEase.Smooth, P(p => { p.RootYaw = 14f; p.ArmL = new Vector3(-0.10f, 1.52f, 0.25f); })),
                    K(0.07f, KeyEase.Snap, P(p =>
                    {
                        p.RootYaw = 26f; p.RootPos = new Vector3(0f, 0f, 0.06f); p.HipsYaw = 6f; p.HipsPitch = 7f; p.TorsoYaw = 6f; p.TorsoPitch = 15f;
                        p.ArmL = new Vector3(-0.02f, 1.55f, 1.00f); p.TwistL = -60f; p.ArmR = new Vector3(0.14f, 1.46f, 0.12f); p.TwistR = 20f;
                        p.LegL = new Vector3(-0.18f, 0.105f, 0.30f);
                    })),
                    K(0.15f, KeyEase.Smooth, P(p =>
                    {
                        p.RootYaw = 25f; p.RootPos = new Vector3(0f, 0f, 0.06f); p.HipsYaw = 6f; p.HipsPitch = 7f; p.TorsoYaw = 6f; p.TorsoPitch = 15f;
                        p.ArmL = new Vector3(-0.03f, 1.55f, 0.94f); p.TwistL = -55f; p.ArmR = new Vector3(0.14f, 1.46f, 0.12f); p.TwistR = 20f;
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
                    K(0.05f, KeyEase.Smooth, P(p => { p.RootYaw = 20f; p.HipsPos = new Vector3(0f, -0.09f, 0f); p.ArmR = new Vector3(0.17f, 1.44f, 0.08f); })),
                    K(0.10f, KeyEase.Snap, P(p =>
                    {
                        p.RootYaw = -22f; p.RootPos = new Vector3(0f, 0f, 0.16f); p.HipsYaw = -10f; p.HipsPitch = 9f; p.HipsPos = new Vector3(0f, -0.10f, 0f);
                        p.TorsoYaw = -12f; p.TorsoPitch = 18f;
                        p.ArmR = new Vector3(0.03f, 1.56f, 1.10f); p.TwistR = 60f; p.ArmL = new Vector3(-0.13f, 1.46f, 0.14f); p.TwistL = -20f;
                        p.LegR = new Vector3(0.20f, 0.105f, -0.06f); p.FootYawR = 5f; p.FootPitchR = 25f; p.LegL = new Vector3(-0.18f, 0.105f, 0.40f);
                    })),
                    K(0.24f, KeyEase.Smooth, P(p =>
                    {
                        p.RootYaw = -21f; p.RootPos = new Vector3(0f, 0f, 0.16f); p.HipsYaw = -10f; p.HipsPitch = 9f; p.HipsPos = new Vector3(0f, -0.10f, 0f);
                        p.TorsoYaw = -12f; p.TorsoPitch = 18f;
                        p.ArmR = new Vector3(0.04f, 1.56f, 1.04f); p.TwistR = 55f; p.ArmL = new Vector3(-0.13f, 1.46f, 0.14f); p.TwistL = -20f;
                        p.LegR = new Vector3(0.20f, 0.105f, -0.06f); p.FootYawR = 5f; p.FootPitchR = 25f; p.LegL = new Vector3(-0.18f, 0.105f, 0.40f);
                    })),
                    K(0.40f, KeyEase.Smooth, P()),
                },
            };
            return new BasicMoveset { Id = DefaultId, Name = "Rush", Guard = guard, Moves = new[] { left, right, body, power } };
        }
    }

    /// <summary>Poses the rig to an <see cref="AttackPose"/> (port of the Attack Bench's applyPose). Writes model rotations only.</summary>
    public static class AttackPoseSolver
    {
        // the base body's torso round the spine (measured, 2.3 m design metres) plus room for the fist beyond the wrist
        private const float TorsoSide = 0.19f, TorsoFront = 0.135f, TorsoBack = 0.12f, FistRoom = 0.07f;

        /// <summary>
        /// Moves a wrist target out of the torso: an elliptic cylinder round the posed spine (hips → neck) in the chest's frame.
        /// Every pose and every blend between poses goes through this, so a fist can never sink into the chest or stomach.
        /// </summary>
        public static Vector3 KeepOutOfTorso(CharacterRig rig, Vector3 target, float hs)
        {
            Vector3 a = rig.ModelPosition(CharacterBone.Hips), b = rig.ModelPosition(CharacterBone.Neck), axis = b - a;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-6f) return target;
            float u = Vector3.Dot(target - a, axis) / len2;
            if (u < 0f || u > 1f) return target;
            Vector3 c = a + axis * u;
            Quaternion chest = rig.GetModelRotation(CharacterBone.Chest);
            Vector3 o = Quaternion.Inverse(chest) * (target - c);
            float rx = (TorsoSide + FistRoom) * hs, rz = ((o.z >= 0f ? TorsoFront : TorsoBack) + FistRoom) * hs;
            float k = (o.x / rx) * (o.x / rx) + (o.z / rz) * (o.z / rz);
            if (k >= 1f) return target;
            if (k < 1e-6f) o = new Vector3(0f, o.y, rz);
            else { float s = 1f / Mathf.Sqrt(k); o.x *= s; o.z *= s; }
            return c + chest * o;
        }

        /// <summary>Is p inside the torso (the elliptic cylinder of <see cref="KeepOutOfTorso"/>) grown by room (design metres)?</summary>
        public static bool InsideTorso(CharacterRig rig, Vector3 p, float room, float hs)
        {
            Vector3 a = rig.ModelPosition(CharacterBone.Hips), b = rig.ModelPosition(CharacterBone.Neck), axis = b - a;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-6f) return false;
            float u = Vector3.Dot(p - a, axis) / len2;
            if (u < -0.05f || u > 1.05f) return false;
            Vector3 o = Quaternion.Inverse(rig.GetModelRotation(CharacterBone.Chest)) * (p - (a + axis * u));
            float rx = (TorsoSide + room) * hs, rz = ((o.z >= 0f ? TorsoFront : TorsoBack) + room) * hs;
            return (o.x / rx) * (o.x / rx) + (o.z / rz) * (o.z / rz) < 1f;
        }

        /// <summary>
        /// Where the elbow points (a pole for the hinge IK): down, out and a little back in the chest's frame; if that would
        /// sink the elbow into the torso (a fist tucked in front of the chest, the run's fist under the chin with the body leant
        /// far forward), the elbow swings out to the side, step by step, until it clears.
        /// </summary>
        public static Vector3 ElbowPole(CharacterRig rig, int side, Vector3 shoulder, Vector3 fist, Quaternion chest, float hs)
        {
            var ua = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperArm, side);
            float l1 = rig.BindLocal(ua + 1).magnitude, l2 = rig.BindLocal(ua + 2).magnitude;
            Vector3 to = fist - shoulder;
            float dist = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = to.sqrMagnitude > 1e-10f ? to.normalized : Vector3.down;
            Vector3 Perp(Vector3 v) { Vector3 w = Vector3.ProjectOnPlane(v, dir); return w.sqrMagnitude < 1e-6f ? Vector3.zero : w.normalized; }
            Vector3 outward = Perp(chest * new Vector3(side, -0.25f, 0f));
            if (outward == Vector3.zero) outward = Perp(chest * Vector3.down);
            Vector3 basePole = Perp(chest * new Vector3(side * 0.5f, -0.7f, -0.25f));
            if (basePole == Vector3.zero) basePole = outward;
            float angA = Mathf.Acos(Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f));
            Vector3 d = basePole;
            for (int i = 0; i <= 10; i++)
            {
                d = Vector3.Lerp(basePole, outward, i / 10f).normalized;
                Vector3 elbow = shoulder + dir * (Mathf.Cos(angA) * l1) + d * (Mathf.Sin(angA) * l1);
                if (!InsideTorso(rig, elbow, 0.05f, hs)) break;
            }
            return shoulder + d * hs;
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
            var ua = sh + 1;
            Quaternion chest = rig.GetModelRotation(CharacterBone.UpperChest);
            Quaternion before = rig.GetModelRotation(sh);
            Vector3 fist = KeepOutOfTorso(rig, target, hs);
            rig.FollowArm(side, fist - rig.ModelPosition(ua));
            rig.SetModelRotation(sh, Quaternion.Slerp(before, rig.GetModelRotation(sh), weight));
            Vector3 shoulder = rig.ModelPosition(ua);
            rig.SolveTwoBoneHinged(ua, fist, ElbowPole(rig, side, shoulder, fist, chest, hs), weight, Vector3.back);
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
                rig.FollowArm(side, fist - rig.ModelPosition(ua));
                Quaternion clavicle = rig.GetModelRotation(sh);
                rig.SetModelRotation(ua, clavicle); rig.SetModelRotation(ua + 1, clavicle); rig.SetModelRotation(ua + 2, clavicle);
                Vector3 shoulder = rig.ModelPosition(ua);
                rig.SolveTwoBoneHinged(ua, fist, ElbowPole(rig, side, shoulder, fist, chest, hs), 1f, Vector3.back);
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
