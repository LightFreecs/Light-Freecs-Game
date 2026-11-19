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
            o.LegL = Vector3.LerpUnclamped(a.LegL, b.LegL, t); o.LegR = Vector3.LerpUnclamped(a.LegR, b.LegR, t);
            o.FootYawL = L(a.FootYawL, b.FootYawL); o.FootYawR = L(a.FootYawR, b.FootYawR);
            o.FootPitchL = L(a.FootPitchL, b.FootPitchL); o.FootPitchR = L(a.FootPitchR, b.FootPitchR);
            float L(float x, float y) => x + (y - x) * t;
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

            // arms: shoulders follow the chest, hinge IK to the fist (elbows down and out), fist roll over forearm + hand, fist
            for (int side = -1; side <= 1; side += 2)
            {
                var sh = (CharacterBone)CharacterBones.Side(CharacterBone.LeftShoulder, side);
                var ua = sh + 1;
                rig.Inherit(sh);
                rig.SetModelRotation(ua, chest); rig.SetModelRotation(ua + 1, chest); rig.SetModelRotation(ua + 2, chest);
                Vector3 shoulder = rig.ModelPosition(ua);
                Vector3 pole = shoulder + chest * (new Vector3(side * 0.5f, -0.7f, -0.25f) * hs);
                Vector3 fist = KeepOutOfTorso(rig, ToModel(side < 0 ? P.ArmL : P.ArmR), hs);
                rig.SolveTwoBoneHinged(ua, fist, pole, 1f, Vector3.back);
                float twist = side < 0 ? P.TwistL : P.TwistR;
                Vector3 fore = (rig.ModelPosition(ua + 2) - rig.ModelPosition(ua + 1)).normalized;
                Quaternion roll = Quaternion.AngleAxis(twist * 0.5f, fore);
                rig.SetModelRotation(ua + 1, roll * rig.GetModelRotation(ua + 1));
                rig.SetModelRotation(ua + 2, roll * rig.GetModelRotation(ua + 1));
                rig.SetFingerCurl(side, 1f);
            }

            // legs: hinge IK to the foot targets, knees towards the toes; planted foot flat, lifted foot square to the shin
            for (int side = -1; side <= 1; side += 2)
            {
                var ul = (CharacterBone)CharacterBones.Side(CharacterBone.LeftUpperLeg, side);
                Vector3 t = side < 0 ? P.LegL : P.LegR;
                float fy = (side < 0 ? P.FootYawL : P.FootYawR) - P.RootYaw, fp = side < 0 ? P.FootPitchL : P.FootPitchR;
                rig.SetModelRotation(ul, H); rig.SetModelRotation(ul + 1, H); rig.SetModelRotation(ul + 2, H);
                Vector3 foot = ToModel(t);
                Vector3 pole = foot + Quaternion.AngleAxis(fy, Vector3.up) * (new Vector3(0f, 0f, 0.9f) * hs) + new Vector3(0f, 0.55f, 0f) * hs;
                rig.SolveTwoBoneHinged(ul, foot, pole, 1f, Vector3.forward);
                Quaternion planted = Quaternion.AngleAxis(fy, Vector3.up) * Quaternion.AngleAxis(fp, Vector3.right);
                Quaternion lifted = rig.GetModelRotation(ul + 1) * Quaternion.AngleAxis(fp, Vector3.right);
                float lift = AttackMove.Ease(KeyEase.Smooth, Mathf.Clamp01((t.y - 0.2f) / 0.25f));
                rig.SetModelRotation(ul + 2, Quaternion.Slerp(planted, lifted, lift));
                rig.Inherit(ul + 3);
            }
        }
    }
}
