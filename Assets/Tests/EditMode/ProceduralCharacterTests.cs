using LightMMORPG.Characters;
using NUnit.Framework;
using UnityEngine;

namespace LightMMORPG.Tests
{
    public sealed class ProceduralCharacterTests
    {
        private static ProceduralCharacterBuilder.GeneratedCharacter Gen(int i) => ProceduralCharacterBuilder.GenerateData(CharacterPresets.Distinct(i));

        [Test]
        public void AllPresets_ProduceValidSkinnedMeshData([Values(0, 1, 2, 3)] int preset)
        {
            var g = Gen(preset);
            Assert.Greater(g.VertexCount, 5000, "suspiciously small mesh");
            Assert.Less(g.VertexCount, ProceduralCharacterBuilder.VertexBudget, "vertex budget");
            Assert.AreEqual(g.Positions.Length, g.Normals.Length);
            Assert.AreEqual(g.Positions.Length, g.Uvs.Length);
            Assert.AreEqual(g.Positions.Length, g.Weights.Length);
            for (int v = 0; v < g.VertexCount; v++)
            {
                Vector3 p = g.Positions[v], n = g.Normals[v];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z), "position NaN at " + v);
                Assert.IsFalse(float.IsNaN(n.x + n.y + n.z), "normal NaN at " + v);
                Assert.AreEqual(1f, n.magnitude, 0.01f, "normal not unit at " + v);
                var w = g.Weights[v];
                Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 1e-3f, "weights at " + v);
                Assert.That(w.boneIndex0, Is.InRange(0, CharacterBones.Count - 1));
                Assert.That(w.boneIndex3, Is.InRange(0, CharacterBones.Count - 1));
            }
            Assert.AreEqual(g.Slots.Count, g.Triangles.Length);
            int tris = 0;
            foreach (var list in g.Triangles)
            {
                Assert.AreEqual(0, list.Count % 3);
                foreach (int idx in list) Assert.That(idx, Is.InRange(0, g.VertexCount - 1));
                tris += list.Count / 3;
            }
            Assert.Greater(tris, g.VertexCount, "closed surfaces have ~2 tris per vertex");
            // stands on the ground, roughly at the recipe height (hair may add on top)
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in g.Positions) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            Assert.AreEqual(0f, minY, 0.05f, "feet on the ground");
            Assert.Greater(maxY, g.Height * 0.95f);
            Assert.Less(maxY, g.Height * 1.35f);
        }

        [Test]
        public void SameRecipe_IsDeterministic()
        {
            var a = Gen(0);
            var b = Gen(0);
            Assert.AreEqual(a.VertexCount, b.VertexCount);
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.AreEqual(a.Positions[i], b.Positions[i]);
                Assert.AreEqual(a.Weights[i], b.Weights[i]);
            }
            for (int s = 0; s < a.Triangles.Length; s++) CollectionAssert.AreEqual(a.Triangles[s], b.Triangles[s]);
            CollectionAssert.AreEqual(a.FacePixels, b.FacePixels);
        }

        [Test]
        public void DifferentSeed_ChangesHair()
        {
            var r = CharacterPresets.Distinct(0);
            var a = ProceduralCharacterBuilder.GenerateData(r);
            r.seed += 1;
            var b = ProceduralCharacterBuilder.GenerateData(r);
            bool differs = a.VertexCount != b.VertexCount;
            for (int i = 0; !differs && i < a.VertexCount; i++) differs = a.Positions[i] != b.Positions[i];
            Assert.IsTrue(differs);
        }

        [Test]
        public void Build_CreatesNamedRigAndSkinnedRenderer()
        {
            var go = ProceduralCharacterBuilder.Build(CharacterPresets.ForPalette(1));
            try
            {
                var rig = go.GetComponent<CharacterRig>();
                Assert.IsNotNull(rig);
                for (int i = 0; i < CharacterBones.Count; i++)
                {
                    var t = rig[(CharacterBone)i];
                    Assert.IsNotNull(t);
                    Assert.AreEqual(((CharacterBone)i).ToString(), t.name);
                    int parent = CharacterBones.Parent(i);
                    if (parent >= 0) Assert.AreSame(rig[(CharacterBone)parent], t.parent);
                }
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.AreEqual(CharacterBones.Count, smr.bones.Length);
                Assert.AreEqual(smr.sharedMesh.subMeshCount, smr.sharedMaterials.Length);
                Assert.AreEqual(CharacterBones.Count, smr.sharedMesh.bindposes.Length);
                // posing keeps everything finite and the hands below the shoulders
                rig.ApplyRestPose();
                rig.SolveTwoBone(CharacterBone.RightUpperArm, new Vector3(0.1f, 1.5f, 1.2f), new Vector3(0.6f, 1.2f, -0.7f), 1f);
                Vector3 hand = rig[CharacterBone.RightHand].position;
                Assert.IsFalse(float.IsNaN(hand.x + hand.y + hand.z));
                Assert.Greater(hand.z, 0.4f, "IK punch reaches forward");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BoneParents_PrecedeChildren()
        {
            for (int i = 0; i < CharacterBones.Count; i++) Assert.Less(CharacterBones.Parent(i), i);
            Assert.AreEqual((int)CharacterBone.RightHand, CharacterBones.Side(CharacterBone.LeftHand, 1));
            Assert.AreEqual((int)CharacterBone.RightToes, CharacterBones.Side(CharacterBone.LeftToes, 1));
            Assert.AreEqual((int)CharacterBone.RightLittleIntermediate, CharacterBones.Side(CharacterBone.LeftLittleIntermediate, 1));
        }
    }
}
