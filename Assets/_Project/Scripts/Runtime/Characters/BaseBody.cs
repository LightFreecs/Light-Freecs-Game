using System;
using System.IO;
using UnityEngine;

namespace LightMMORPG.Characters
{
    /// <summary>
    /// The fighter's body: the owner's anime base mesh (used with the author's permission), baked into
    /// <c>Resources/Bodies/BaseBody.bytes</c> by <c>tools/bake_base_body.py</c>. Game coordinates (metres, +Y up, +Z forward,
    /// chin at <see cref="Data.ChinY"/>), its own head and neck replaced by a clean neck tube that reaches up inside the Toon
    /// Head (its top turns with the head), arms re-posed to the rig's 40° A-pose so that the game's skinning gives back the
    /// sculpt with the arms down, smooth (heat-diffused) skin weights for every <see cref="CharacterBone"/> (fingers
    /// included), plus trunks and a waistband copied from the body surface with matching weights.
    /// </summary>
    public static class BaseBody
    {
        public const string ResourcePath = "Bodies/BaseBody";

        /// <summary>Height where the neck is measured for the head's collar (just under the chin).</summary>
        public const float CollarY = 1.815f;

        /// <summary>One mesh piece: positions, 4 bone indices + 4 weights per vertex (largest first), triangles.</summary>
        public sealed class Part
        {
            public Vector3[] Positions;
            public byte[] BoneIndex;
            public float[] Weights;
            public int[] Triangles;
            public int VertexCount => Positions.Length;
        }

        public sealed class Data
        {
            /// <summary>Height of the chin (where the Toon Head's chin goes) and z of the neck's centre under the jaw.</summary>
            public float ChinY, NeckZ;
            /// <summary>Waistband height and the bottom of the trunk legs.</summary>
            public float TrunksTop, TrunksBottom;
            /// <summary>The body's neck just under the chin (<see cref="CollarY"/>): half width, front and back from <see cref="NeckZ"/>.</summary>
            public float NeckHalfWidth, NeckFront, NeckBack;
            /// <summary>Bind-pose joint positions in <see cref="CharacterBone"/> order (Head is re-placed per recipe).</summary>
            public Vector3[] Joints;
            public Part Body, Trunks, Band;
        }

        private static Data _data;

        public static bool IsLoaded => _data != null;

        /// <summary>Loads the baked body (main thread: Resources). Call before generating on worker threads.</summary>
        public static void EnsureLoaded()
        {
            if (_data != null) return;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) throw new InvalidOperationException("Missing Resources/" + ResourcePath + ".bytes (the baked base body).");
            _data = Parse(asset.bytes);
        }

        /// <summary>The loaded body (loads it if needed; that part only works on the main thread).</summary>
        public static Data Get()
        {
            if (_data == null) EnsureLoaded();
            return _data;
        }

        public static Data Parse(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (new string(r.ReadChars(4)) != "LBB1") throw new InvalidDataException("Not a baked base body (LBB1).");
            int bones = r.ReadInt32();
            if (bones != CharacterBones.Count) throw new InvalidDataException("Base body has " + bones + " bones, the rig has " + CharacterBones.Count + ".");
            var d = new Data { ChinY = r.ReadSingle(), NeckZ = r.ReadSingle(), TrunksTop = r.ReadSingle(), TrunksBottom = r.ReadSingle(), Joints = new Vector3[bones] };
            for (int i = 0; i < bones; i++) d.Joints[i] = ReadV3(r);
            d.Body = ReadPart(r);
            d.Trunks = ReadPart(r);
            d.Band = ReadPart(r);
            MeasureNeck(d);
            return d;
        }

        private static void MeasureNeck(Data d)
        {
            float w = 0f, f = 0f, b = 0f;
            foreach (var p in d.Body.Positions)
            {
                if (Mathf.Abs(p.y - CollarY) > 0.006f || Mathf.Abs(p.x) > 0.15f) continue;
                w = Mathf.Max(w, Mathf.Abs(p.x));
                f = Mathf.Max(f, p.z - d.NeckZ);
                b = Mathf.Max(b, d.NeckZ - p.z);
            }
            d.NeckHalfWidth = w > 0f ? w : 0.055f;
            d.NeckFront = f > 0f ? f : 0.058f;
            d.NeckBack = b > 0f ? b : 0.06f;
        }

        private static Vector3 ReadV3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static Part ReadPart(BinaryReader r)
        {
            int nv = r.ReadInt32(), nt = r.ReadInt32();
            var p = new Part { Positions = new Vector3[nv], BoneIndex = new byte[nv * 4], Weights = new float[nv * 4], Triangles = new int[nt * 3] };
            for (int i = 0; i < nv; i++) p.Positions[i] = ReadV3(r);
            r.Read(p.BoneIndex, 0, p.BoneIndex.Length);
            for (int i = 0; i < p.Weights.Length; i++) p.Weights[i] = r.ReadSingle();
            for (int i = 0; i < p.Triangles.Length; i++) p.Triangles[i] = r.ReadUInt16();
            return p;
        }
    }
}
