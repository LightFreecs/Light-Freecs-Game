using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>One key of a walk or run cycle: a full-body pose at a point of the step (u 0..1).</summary>
    public sealed class GaitKey
    {
        public float U;
        public AttackPose Pose;
    }

    /// <summary>Which way a gait travels in the fighter's own frame; its planted feet slide back along this axis.</summary>
    public enum GaitAxis : byte { Forward, Back, Right }

    /// <summary>
    /// One gait (a walk or a run) of a <see cref="LocomotionStyle"/>, from the Locomotion Bench (docs/bench/locomotion-bench.html).
    /// Poses are <see cref="AttackPose"/>s in the fighter's own space (+Z forward, metres of the 2.3 m design body, scaled by
    /// rig height / 2.3 at play time). The keys cover ONE step led by the right foot, which touches down at u = 0; the left
    /// step is the mirror, so a cycle is two steps (phase 0..2). Gaits whose two steps differ (the sidestep: one foot reaches
    /// out, the other closes in) key the whole cycle instead (<see cref="WholeCycle"/>: u 0..2, the left foot lands at 1).
    /// Keys pass through Catmull-Rom splines (the limbs never stop at a key). The planted foot is laid on the ground exactly:
    /// from touchdown (<see cref="Touch"/>, along <see cref="Axis"/>) it slides back <see cref="StepLength"/> per step for
    /// <see cref="Stance"/> steps, which at <see cref="Speed"/> is the ground's speed. At other speeds the stride scales by
    /// √(speed / Speed) and the cadence takes the rest, so feet never skate.
    /// </summary>
    public sealed class GaitCycle
    {
        public string Name;
        /// <summary>Speed the keys are made for (m/s, design body).</summary>
        public float Speed;
        /// <summary>Metres per step at <see cref="Speed"/> (Speed / StepLength = steps per second).</summary>
        public float StepLength;
        /// <summary>Steps a foot stays down from its touchdown: below 1 there is a leap (run), above 1 both feet are down for a while (walk).</summary>
        public float Stance;
        /// <summary>Where the (right) foot touches down along <see cref="Axis"/>: design metres from the root, + = the way the gait travels.</summary>
        public float Touch;
        /// <summary>The way the gait travels: forward (walk, run), back (backpedal, backwards run) or right (sidestep).</summary>
        public GaitAxis Axis = GaitAxis.Forward;
        /// <summary>The keys cover both steps (u 0..2, right foot down at 0, left at 1) instead of the right step plus its mirror.</summary>
        public bool WholeCycle;
        /// <summary>Whole-cycle gaits: where the left foot touches down along <see cref="Axis"/> (the right one uses <see cref="Touch"/>).</summary>
        public float TouchL;
        /// <summary>The right foot's step, u ascending in [0, 1) (whole-cycle gaits: both steps, u in [0, 2)).</summary>
        public GaitKey[] Keys;

        private AttackPose[] _mirror;
        private bool _built;
        // the stride scales each foot's reach about these: the root for one-step gaits, the middle of each foot's slide for the
        // sidestep (so the feet keep their width at other speeds)
        private float _centreR, _centreL;

        public float StepsPerSecond => Speed / StepLength;

        /// <summary>Stride scale at a speed (design m/s): √(speed / Speed), kept within 0.55..1.25.</summary>
        public float StrideScale(float speed) => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(speed, 0.01f) / Speed), 0.55f, 1.25f);

        internal GaitCycle Build()
        {
            if (WholeCycle)
            {
                _centreR = Touch - StepLength * Stance * 0.5f;
                _centreL = TouchL - StepLength * Stance * 0.5f;
            }
            else
            {
                _mirror = new AttackPose[Keys.Length];
                for (int i = 0; i < Keys.Length; i++) _mirror[i] = Keys[i].Pose.Mirrored();
            }
            _built = true;
            return this;
        }

        private int KeyCount => WholeCycle ? Keys.Length : 2 * Keys.Length;

        private float KeyT(int i)
        {
            int n = Keys.Length;
            if (WholeCycle) return Keys[((i % n) + n) % n].U + 2f * Mathf.FloorToInt(i / (float)n);
            int N = 2 * n, m = ((i % N) + N) % N, cycle = Mathf.FloorToInt(i / (float)N);
            return (m < n ? Keys[m].U : 1f + Keys[m - n].U) + 2f * cycle;
        }

        private AttackPose KeyPose(int i)
        {
            int n = Keys.Length;
            if (WholeCycle) return Keys[((i % n) + n) % n].Pose;
            int N = 2 * n, m = ((i % N) + N) % N;
            return m < n ? Keys[m].Pose : _mirror[m - n];
        }

        /// <summary>The pose at <paramref name="phase"/> (0..2: right step, then left step) with the stride scaled by <paramref name="stride"/>.</summary>
        public void Sample(float phase, float stride, AttackPose o)
        {
            if (!_built) Build();
            int N = KeyCount;
            float p = Mathf.Repeat(phase, 2f);
            int i = N - 1;
            for (int j = 0; j < N; j++) if (KeyT(j) <= p && p < KeyT(j + 1)) { i = j; break; }
            float t0 = KeyT(i - 1), t1 = KeyT(i), t2 = KeyT(i + 1), t3 = KeyT(i + 2);
            float d = t2 - t1, s = (p - t1) / d, s2 = s * s, s3 = s2 * s;
            float h00 = 2f * s3 - 3f * s2 + 1f, h10 = s3 - 2f * s2 + s, h01 = -2f * s3 + 3f * s2, h11 = s3 - s2;
            float a = d / (t2 - t0), b = d / (t3 - t1);
            AttackPose.Combine(KeyPose(i - 1), KeyPose(i), KeyPose(i + 1), KeyPose(i + 2), -h10 * a, h00 - h11 * b, h01 + h10 * a, h11 * b, o);
            // planted feet move back with the ground along the axis (right foot down from phase 0, left from 1); the stride
            // scales the reach
            o.LegR = Plant(o.LegR, Mathf.Repeat(p, 2f), Touch, _centreR, stride);
            o.LegL = Plant(o.LegL, Mathf.Repeat(p - 1f, 2f), WholeCycle ? TouchL : Touch, _centreL, stride);
        }

        private Vector3 Plant(Vector3 foot, float since, float touch, float centre, float stride)
        {
            float u = Axis == GaitAxis.Right ? foot.x : Axis == GaitAxis.Back ? -foot.z : foot.z;
            if (since <= Stance) u = touch - StepLength * since;
            u = centre + (u - centre) * stride;
            if (Axis == GaitAxis.Right) foot.x = u;
            else if (Axis == GaitAxis.Back) foot.z = -u;
            else foot.z = u;
            return foot;
        }
    }

    /// <summary>
    /// A way of moving: its walk (the default) and its run (Shift), plus the locked-on gaits that keep the fighter facing the
    /// opponent: a sidestep to the right (to the left = its mirror), a backpedal and a backwards run. Running sideways turns
    /// the run itself (PlayerProceduralAnimator). Fighters pick one by id, like a basic-attack moveset.
    /// </summary>
    public sealed class LocomotionStyle
    {
        public string Id, Name;
        public GaitCycle Walk, Run;
        /// <summary>Locked on: sidestep to the right, backpedal, backwards run. Null = the walk / run turned to the direction instead.</summary>
        public GaitCycle WalkSide, WalkBack, RunBack;
    }

    /// <summary>
    /// The locomotion (walk / run) styles. Built for several running styles: everything that moves a fighter asks for a style
    /// by id (<see cref="Get"/>), and a new style is a new entry in <see cref="All"/> (made on the Locomotion Bench first).
    /// Today there is one, <see cref="Shonen"/>, the default for every fighter.
    /// </summary>
    public static class LocomotionStyles
    {
        public const string DefaultId = "shonen";

        /// <summary>
        /// "Shonen Sprint", from the owner's reference clip: the run leans ~50° forward and stays low, the back leg is thrown
        /// up level with the hips, one fist tucked under the chin and the other arm thrown straight back with the hand open,
        /// six steps a second at 8 m/s (the clip's rhythm; its foot-down time is shortened so the planted foot keeps up with
        /// the ground). The walk is an upright, easy hero walk with relaxed half-open hands, 2.75 steps a second at 2.2 m/s.
        /// Numbers = the Locomotion Bench.
        /// </summary>
        public static readonly LocomotionStyle Shonen = BuildShonen();

        public static readonly IReadOnlyList<LocomotionStyle> All = new[] { Shonen };

        private static readonly Dictionary<string, LocomotionStyle> ById = BuildIndex();

        public static LocomotionStyle Get(string id) => id != null && ById.TryGetValue(id, out var s) ? s : Shonen;

        private static Dictionary<string, LocomotionStyle> BuildIndex()
        {
            var d = new Dictionary<string, LocomotionStyle>();
            foreach (var s in All) d[s.Id] = s;
            return d;
        }

        private static GaitKey K(AttackPose basePose, float u, Action<AttackPose> edit)
        {
            var p = basePose.Clone();
            edit(p);
            return new GaitKey { U = u, Pose = p };
        }

        private static LocomotionStyle BuildShonen()
        {
            var wb = new AttackPose { HipsPos = new Vector3(0f, -0.06f, 0f), HipsPitch = 3f, TorsoPitch = 3f, HeadPitch = -2f, OpenL = 0.35f, OpenR = 0.35f };
            var walk = new GaitCycle
            {
                Name = "Walk", Speed = 2.2f, StepLength = 0.8f, Stance = 1.15f, Touch = 0.38f,
                Keys = new[]
                {
                    // right heel strikes on a straight leg, the left foot rolls up onto its toes
                    K(wb, 0f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.075f, 0f); p.HipsYaw = -5f; p.TorsoYaw = 8f;
                        p.ArmL = new Vector3(-0.25f, 1.10f, 0.30f); p.TwistL = -10f; p.ArmR = new Vector3(0.30f, 1.12f, -0.20f); p.TwistR = 6f;
                        p.LegR = new Vector3(0.10f, 0.115f, 0.38f); p.FootPitchR = -14f; p.LegL = new Vector3(-0.10f, 0.15f, -0.42f); p.FootPitchL = 22f;
                    }),
                    K(wb, 0.15f, p =>
                    {
                        p.HipsPos = new Vector3(0.008f, -0.08f, 0f); p.HipsYaw = -4f; p.HipsRoll = 1.5f; p.TorsoYaw = 7f; p.TorsoRoll = -0.8f;
                        p.ArmL = new Vector3(-0.26f, 1.10f, 0.28f); p.TwistL = -10f; p.ArmR = new Vector3(0.30f, 1.12f, -0.18f); p.TwistR = 6f;
                        p.LegR = new Vector3(0.10f, 0.105f, 0.26f); p.LegL = new Vector3(-0.10f, 0.19f, -0.54f); p.FootPitchL = 35f;
                    }),
                    K(wb, 0.38f, p =>
                    {
                        p.HipsPos = new Vector3(0.018f, -0.045f, 0f); p.HipsYaw = -1f; p.HipsRoll = 3.5f; p.TorsoYaw = 2f; p.TorsoRoll = -1.5f;
                        p.ArmL = new Vector3(-0.30f, 1.09f, 0.13f); p.TwistL = -6f; p.ArmR = new Vector3(0.31f, 1.10f, -0.02f); p.TwistR = 4f;
                        p.LegR = new Vector3(0.10f, 0.105f, 0.076f); p.LegL = new Vector3(-0.10f, 0.29f, -0.24f); p.FootPitchL = 28f;
                    }),
                    // the left foot passes under the hips, the hips ride highest over the right foot
                    K(wb, 0.6f, p =>
                    {
                        p.HipsPos = new Vector3(0.02f, -0.028f, 0f); p.HipsYaw = 2f; p.HipsRoll = 4f; p.TorsoYaw = -3f; p.TorsoRoll = -1.8f;
                        p.ArmL = new Vector3(-0.31f, 1.10f, 0.02f); p.TwistL = -2f; p.ArmR = new Vector3(0.30f, 1.09f, 0.10f); p.TwistR = -2f;
                        p.LegR = new Vector3(0.10f, 0.105f, -0.10f); p.LegL = new Vector3(-0.10f, 0.24f, 0.06f); p.FootPitchL = 4f;
                    }),
                    K(wb, 0.82f, p =>
                    {
                        p.HipsPos = new Vector3(0.012f, -0.05f, 0f); p.HipsYaw = 4f; p.HipsRoll = 2f; p.TorsoYaw = -6f; p.TorsoRoll = -1f;
                        p.ArmL = new Vector3(-0.31f, 1.12f, -0.15f); p.TwistL = 4f; p.ArmR = new Vector3(0.26f, 1.09f, 0.25f); p.TwistR = -8f;
                        p.LegR = new Vector3(0.10f, 0.12f, -0.276f); p.FootPitchR = 9f; p.LegL = new Vector3(-0.10f, 0.165f, 0.30f); p.FootPitchL = -10f;
                    }),
                },
            }.Build();

            var rb = new AttackPose { HipsPos = new Vector3(0f, -0.09f, 0f), HipsPitch = 27f, TorsoPitch = 50f, HeadPitch = 6f };
            var run = new GaitCycle
            {
                Name = "Run", Speed = 8f, StepLength = 4f / 3f, Stance = 0.45f, Touch = 0.22f,
                Keys = new[]
                {
                    // the right foot lands just ahead of the hips; the left leg is thrown straight back level with the hips,
                    // the left fist tucked under the chin, the right arm straight back with the hand open (clip: drawings 4 / 8)
                    K(rb, 0f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.095f, 0f); p.HipsYaw = -8f; p.TorsoYaw = 12f;
                        p.ArmL = new Vector3(-0.08f, 1.30f, 0.50f); p.TwistL = -30f; p.ArmR = new Vector3(0.22f, 1.42f, -0.26f); p.OpenR = 1f;
                        p.LegR = new Vector3(0.10f, 0.105f, 0.22f); p.FootPitchR = -6f; p.LegL = new Vector3(-0.10f, 0.95f, -0.92f);
                    }),
                    // landing: the knees soak it up, the back knee folds with the foot still high (drawings 5 / 6)
                    K(rb, 0.2f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.13f, 0f); p.HipsYaw = -5f; p.TorsoYaw = 9f;
                        p.ArmL = new Vector3(-0.09f, 1.28f, 0.48f); p.TwistL = -30f; p.ArmR = new Vector3(0.22f, 1.40f, -0.23f); p.OpenR = 1f;
                        p.LegR = new Vector3(0.10f, 0.105f, -0.047f); p.LegL = new Vector3(-0.10f, 0.86f, -0.56f); p.FootPitchL = 10f;
                    }),
                    // push-off: the right leg straight behind on its toes, the left knee through in front, the arms swap low (drawings 2 / 7)
                    K(rb, 0.45f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.075f, 0f); p.HipsYaw = 1f; p.TorsoYaw = -1f;
                        p.ArmL = new Vector3(-0.25f, 1.10f, 0.17f); p.TwistL = -10f; p.OpenL = 0.3f; p.ArmR = new Vector3(0.26f, 1.16f, -0.06f); p.TwistR = 5f; p.OpenR = 0.5f;
                        p.LegR = new Vector3(0.10f, 0.17f, -0.38f); p.FootPitchR = 32f; p.LegL = new Vector3(-0.10f, 0.33f, 0.10f); p.FootPitchL = 8f;
                    }),
                    // the short leap: the right foot kicks up behind, the left reaches for the floor
                    K(rb, 0.72f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.06f, 0f); p.HipsYaw = 6f; p.TorsoYaw = -9f;
                        p.ArmL = new Vector3(-0.24f, 1.30f, -0.20f); p.OpenL = 0.85f; p.ArmR = new Vector3(0.12f, 1.24f, 0.42f); p.TwistR = 25f; p.OpenR = 0.1f;
                        p.LegR = new Vector3(0.10f, 0.64f, -0.72f); p.LegL = new Vector3(-0.10f, 0.30f, 0.31f); p.FootPitchL = -4f;
                    }),
                },
            }.Build();

            // ---- locked on: the fighter keeps facing the opponent ----

            // sidestep to the right (left = the mirror): a low boxer's shuffle, the feet never cross. The right foot reaches out
            // and lands wide, the left one closes in next to it as the right one lifts; a short float between the steps. The
            // two steps differ, so the keys cover the whole cycle; the planted foot slides along x. 5.5 steps a second at 2.2 m/s.
            var sb = new AttackPose { HipsPos = new Vector3(0f, -0.13f, 0f), HipsPitch = 9f, TorsoPitch = 5f, TorsoRoll = -2f, HeadPitch = -7f, OpenL = 0.2f, OpenR = 0.2f, TwistL = -8f, TwistR = 8f };
            var side = new GaitCycle
            {
                Name = "Sidestep", Speed = 2.2f, StepLength = 0.40f, Stance = 0.75f, Touch = 0.38f, TouchL = -0.08f,
                Axis = GaitAxis.Right, WholeCycle = true,
                Keys = new[]
                {
                    // the right foot lands wide, the left one has just pushed off
                    K(sb, 0f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.138f, 0f); p.HipsRoll = -1f; p.TorsoRoll = -1.5f;
                        p.ArmL = new Vector3(-0.26f, 1.18f, 0.22f); p.ArmR = new Vector3(0.27f, 1.17f, 0.21f);
                        p.LegR = new Vector3(0.38f, 0.105f, 0.03f); p.FootYawR = 8f; p.FootPitchR = 2f; p.LegL = new Vector3(-0.35f, 0.15f, 0.01f); p.FootYawL = -6f; p.FootPitchL = 12f;
                    }),
                    K(sb, 0.25f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.142f, 0f); p.HipsRoll = -1.5f; p.TorsoRoll = -1.2f;
                        p.ArmL = new Vector3(-0.26f, 1.175f, 0.22f); p.ArmR = new Vector3(0.27f, 1.165f, 0.21f);
                        p.LegR = new Vector3(0.28f, 0.105f, 0.03f); p.FootYawR = 8f; p.LegL = new Vector3(-0.26f, 0.20f, 0.02f); p.FootYawL = -6f; p.FootPitchL = 8f;
                    }),
                    K(sb, 0.5f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.13f, 0f); p.HipsRoll = -1f; p.TorsoRoll = -1.5f;
                        p.ArmL = new Vector3(-0.255f, 1.18f, 0.22f); p.ArmR = new Vector3(0.265f, 1.175f, 0.21f);
                        p.LegR = new Vector3(0.18f, 0.105f, 0.03f); p.FootYawR = 8f; p.LegL = new Vector3(-0.15f, 0.18f, 0.03f); p.FootYawL = -6f; p.FootPitchL = 4f;
                    }),
                    // the right foot lifts as the left one comes down next to it (a touch past its spot, so it lands with the floor)
                    K(sb, 0.75f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.12f, 0f); p.TorsoRoll = -2f;
                        p.ArmL = new Vector3(-0.255f, 1.185f, 0.22f); p.ArmR = new Vector3(0.26f, 1.18f, 0.21f);
                        p.LegR = new Vector3(0.08f, 0.12f, 0.03f); p.FootYawR = 8f; p.FootPitchR = 10f; p.LegL = new Vector3(-0.06f, 0.13f, 0.03f); p.FootYawL = -6f; p.FootPitchL = 1f;
                    }),
                    K(sb, 1f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.13f, 0f); p.HipsRoll = 0.5f; p.TorsoRoll = -2.3f;
                        p.ArmL = new Vector3(-0.255f, 1.18f, 0.22f); p.ArmR = new Vector3(0.265f, 1.175f, 0.21f);
                        p.LegR = new Vector3(0.12f, 0.19f, 0.04f); p.FootYawR = 8f; p.FootPitchR = 8f; p.LegL = new Vector3(-0.08f, 0.105f, 0.03f); p.FootYawL = -6f;
                    }),
                    // the right foot reaches out, the weight on the left
                    K(sb, 1.25f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.138f, 0f); p.HipsRoll = 1.5f; p.TorsoRoll = -2.8f;
                        p.ArmL = new Vector3(-0.255f, 1.175f, 0.22f); p.ArmR = new Vector3(0.275f, 1.17f, 0.21f);
                        p.LegR = new Vector3(0.22f, 0.22f, 0.05f); p.FootYawR = 8f; p.FootPitchR = 6f; p.LegL = new Vector3(-0.18f, 0.105f, 0.03f); p.FootYawL = -6f;
                    }),
                    K(sb, 1.5f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.13f, 0f); p.HipsRoll = 1.5f; p.TorsoRoll = -2.8f;
                        p.ArmL = new Vector3(-0.26f, 1.18f, 0.22f); p.ArmR = new Vector3(0.28f, 1.175f, 0.21f);
                        p.LegR = new Vector3(0.33f, 0.19f, 0.05f); p.FootYawR = 8f; p.FootPitchR = 3f; p.LegL = new Vector3(-0.28f, 0.105f, 0.03f); p.FootYawL = -6f;
                    }),
                    // the left foot pushes off (heel up) as the right one comes down, a touch past its spot
                    K(sb, 1.75f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.118f, 0f); p.HipsRoll = 0.5f; p.TorsoRoll = -2.3f;
                        p.ArmL = new Vector3(-0.26f, 1.185f, 0.22f); p.ArmR = new Vector3(0.28f, 1.18f, 0.21f);
                        p.LegR = new Vector3(0.40f, 0.13f, 0.04f); p.FootYawR = 8f; p.FootPitchR = 2f; p.LegL = new Vector3(-0.38f, 0.125f, 0.02f); p.FootYawL = -6f; p.FootPitchL = 14f;
                    }),
                },
            }.Build();

            // backpedal: quicker, shorter steps than the walk, knees bent, a slight lean towards the opponent. The foot lands
            // behind on its toes, the heel comes down, and it leaves the floor in front heel last. 4.2 steps a second at 2.2 m/s.
            var bb = new AttackPose { HipsPos = new Vector3(0f, -0.09f, 0f), HipsPitch = 6f, TorsoPitch = 3f, HeadPitch = -5f, OpenL = 0.35f, OpenR = 0.35f };
            var back = new GaitCycle
            {
                Name = "Backpedal", Speed = 2.2f, StepLength = 0.52f, Stance = 1.12f, Touch = 0.29f, Axis = GaitAxis.Back,
                Keys = new[]
                {
                    // the right foot lands behind on its toes, the left one is about to leave the floor in front, heel last
                    K(bb, 0f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.10f, 0f); p.HipsYaw = 5f; p.TorsoYaw = -7f;
                        p.ArmL = new Vector3(-0.29f, 1.11f, -0.09f); p.TwistL = 4f; p.ArmR = new Vector3(0.28f, 1.10f, 0.20f); p.TwistR = 6f;
                        p.LegR = new Vector3(0.10f, 0.125f, -0.29f); p.FootPitchR = 16f; p.LegL = new Vector3(-0.10f, 0.105f, 0.23f); p.FootPitchL = -6f;
                    }),
                    K(bb, 0.12f, p =>
                    {
                        p.HipsPos = new Vector3(0.004f, -0.105f, 0f); p.HipsYaw = 4.5f; p.HipsRoll = 1f; p.TorsoYaw = -6.5f; p.TorsoRoll = -0.5f;
                        p.ArmL = new Vector3(-0.29f, 1.11f, -0.08f); p.TwistL = 4f; p.ArmR = new Vector3(0.28f, 1.10f, 0.19f); p.TwistR = 6f;
                        p.LegR = new Vector3(0.10f, 0.108f, -0.228f); p.FootPitchR = 3f; p.LegL = new Vector3(-0.10f, 0.11f, 0.292f); p.FootPitchL = -12f;
                    }),
                    // the left foot swings back under the hips, knee bent
                    K(bb, 0.4f, p =>
                    {
                        p.HipsPos = new Vector3(0.014f, -0.08f, 0f); p.HipsYaw = 1.5f; p.HipsRoll = 3f; p.TorsoYaw = -2f; p.TorsoRoll = -1.4f;
                        p.ArmL = new Vector3(-0.30f, 1.09f, 0f); p.TwistL = 2f; p.ArmR = new Vector3(0.29f, 1.09f, 0.11f); p.TwistR = 3f;
                        p.LegR = new Vector3(0.10f, 0.105f, -0.082f); p.LegL = new Vector3(-0.10f, 0.20f, 0.12f);
                    }),
                    K(bb, 0.7f, p =>
                    {
                        p.HipsPos = new Vector3(0.016f, -0.074f, 0f); p.HipsYaw = -2f; p.HipsRoll = 3f; p.TorsoYaw = 3f; p.TorsoRoll = -1.4f;
                        p.ArmL = new Vector3(-0.30f, 1.09f, 0.09f); p.TwistL = -2f; p.ArmR = new Vector3(0.30f, 1.09f, 0.02f);
                        p.LegR = new Vector3(0.10f, 0.105f, 0.074f); p.LegL = new Vector3(-0.10f, 0.19f, -0.10f); p.FootPitchL = 10f;
                    }),
                    // the left foot reaches back on its toes
                    K(bb, 0.9f, p =>
                    {
                        p.HipsPos = new Vector3(0.008f, -0.09f, 0f); p.HipsYaw = -4f; p.HipsRoll = 1.5f; p.TorsoYaw = 6f; p.TorsoRoll = -0.8f;
                        p.ArmL = new Vector3(-0.29f, 1.10f, 0.16f); p.TwistL = -5f; p.ArmR = new Vector3(0.29f, 1.10f, -0.05f); p.TwistR = -3f;
                        p.LegR = new Vector3(0.10f, 0.105f, 0.178f); p.FootPitchR = -2f; p.LegL = new Vector3(-0.10f, 0.145f, -0.25f); p.FootPitchL = 16f;
                    }),
                },
            }.Build();

            // running backwards: low quick bounds on the balls of the feet. The foot leaves the floor in front, swings back under
            // the hips with the knee bent and reaches back on its toes to land; fists up and pumping, a slight lean towards the
            // opponent. 7 steps a second at 8 m/s.
            var kb = new AttackPose { HipsPos = new Vector3(0f, -0.12f, 0f), HipsPitch = 10f, TorsoPitch = 8f, HeadPitch = -11f };
            var runBack = new GaitCycle
            {
                Name = "Run backwards", Speed = 8f, StepLength = 8f / 7f, Stance = 0.42f, Touch = 0.24f, Axis = GaitAxis.Back,
                Keys = new[]
                {
                    // the right foot lands behind on its toes, the left one swings back under the hips
                    K(kb, 0f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.12f, 0f); p.HipsYaw = 7f; p.TorsoYaw = -9f;
                        p.ArmL = new Vector3(-0.25f, 1.13f, -0.14f); p.ArmR = new Vector3(0.20f, 1.32f, 0.30f); p.TwistR = 10f;
                        p.LegR = new Vector3(0.10f, 0.135f, -0.24f); p.FootPitchR = 24f; p.LegL = new Vector3(-0.10f, 0.30f, -0.08f); p.FootPitchL = 30f;
                    }),
                    // soaking up the landing
                    K(kb, 0.2f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.15f, 0f); p.HipsYaw = 4f; p.TorsoYaw = -5f;
                        p.ArmL = new Vector3(-0.25f, 1.17f, -0.06f); p.ArmR = new Vector3(0.22f, 1.28f, 0.22f); p.TwistR = 8f;
                        p.LegR = new Vector3(0.10f, 0.11f, -0.011f); p.FootPitchR = 8f; p.LegL = new Vector3(-0.10f, 0.28f, -0.20f); p.FootPitchL = 30f;
                    }),
                    // pushing off from in front, the left foot reaching back
                    K(kb, 0.42f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.11f, 0f);
                        p.ArmL = new Vector3(-0.24f, 1.22f, 0.06f); p.ArmR = new Vector3(0.25f, 1.21f, 0.08f); p.TwistR = 3f;
                        p.LegR = new Vector3(0.10f, 0.12f, 0.24f); p.FootPitchR = 6f; p.LegL = new Vector3(-0.10f, 0.22f, -0.28f); p.FootPitchL = 28f;
                    }),
                    // the short leap back
                    K(kb, 0.7f, p =>
                    {
                        p.HipsPos = new Vector3(0f, -0.095f, 0f); p.HipsYaw = -4f; p.TorsoYaw = 5f;
                        p.ArmL = new Vector3(-0.21f, 1.28f, 0.22f); p.TwistL = -8f; p.ArmR = new Vector3(0.26f, 1.15f, -0.08f);
                        p.LegR = new Vector3(0.10f, 0.25f, 0.10f); p.FootPitchR = 25f; p.LegL = new Vector3(-0.10f, 0.16f, -0.29f); p.FootPitchL = 26f;
                    }),
                },
            }.Build();

            return new LocomotionStyle { Id = DefaultId, Name = "Shonen Sprint", Walk = walk, Run = run, WalkSide = side, WalkBack = back, RunBack = runBack };
        }
    }
}
