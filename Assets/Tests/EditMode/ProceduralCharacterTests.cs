using LightMMORPG.Characters;
using NUnit.Framework;
using UnityEngine;

namespace LightMMORPG.Tests
{
    public sealed class ProceduralCharacterTests
    {
        private static ProceduralCharacterBuilder.GeneratedCharacter Gen(int i) => ProceduralCharacterBuilder.GenerateData(TestRecipes.Variant(i));

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
            var r = TestRecipes.Variant(0);
            var a = ProceduralCharacterBuilder.GenerateData(r);
            r.seed += 1;
            var b = ProceduralCharacterBuilder.GenerateData(r);
            bool differs = a.VertexCount != b.VertexCount;
            for (int i = 0; !differs && i < a.VertexCount; i++) differs = a.Positions[i] != b.Positions[i];
            Assert.IsTrue(differs);
        }

        [Test]
        public void ClassicPartPresets_AreTheOriginalHead()
        {
            var f = FaceParts.Resolve(new CharacterRecipe());
            Assert.AreEqual(0.088f, f.Jaw.CheekWidth, 1e-6f);
            Assert.AreEqual(0.074f, f.Jaw.TopWidth, 1e-6f);
            Assert.AreEqual(-0.006f, f.Jaw.BottomY, 1e-6f);
            Assert.AreEqual(-0.008f, f.Chin.Y, 1e-6f);
            Assert.AreEqual(FaceParts.ClassicNoseTipY, f.Nose.Tip.y, 1e-6f);
            Assert.AreEqual(20f, f.Ear.Flare, 1e-6f);
        }

        [Test]
        public void PartPresets_AndSliderExtremes_GiveValidHeads(
            [Values(JawPreset.Classic, JawPreset.Head1, JawPreset.Square, JawPreset.Pointed)] JawPreset jaw,
            [Values(false, true)] bool extremes)
        {
            var r = TestRecipes.Variant(1);
            int i = (int)jaw;
            r.jawPreset = jaw;
            r.chinPreset = (ChinPreset)i;
            r.nosePreset = (NosePreset)i;
            r.earPreset = (EarPreset)i;
            if (extremes)
            {
                bool hi = i % 2 == 0;
                r.jawWidth = hi ? 1.2f : 0.85f; r.jawLength = hi ? 0.015f : -0.012f; r.jawForward = hi ? 0.02f : -0.02f; r.jawThickness = hi ? 1.4f : 0.7f;
                r.chinSize = hi ? 1.5f : 0.6f; r.chinLength = hi ? 0.012f : -0.008f; r.chinForward = hi ? 0.015f : -0.015f;
                r.noseSize = hi ? 1.6f : 0.6f; r.noseLength = hi ? 1.5f : 0.6f; r.noseHeight = hi ? 0.012f : -0.012f;
                r.earSize = hi ? 1.5f : 0.7f; r.earHeight = hi ? 0.03f : -0.03f; r.earFlare = hi ? 30f : -20f;
            }
            var g = ProceduralCharacterBuilder.GenerateData(r);
            Assert.Less(g.VertexCount, ProceduralCharacterBuilder.VertexBudget);
            for (int v = 0; v < g.VertexCount; v++)
            {
                Vector3 p = g.Positions[v], n = g.Normals[v];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsNaN(n.x + n.y + n.z), "NaN at " + v);
                Assert.AreEqual(1f, n.magnitude, 0.01f, "normal not unit at " + v);
            }
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
        public void BaseMeshBody_SlidersAndRigStayValid([Values(-1f, 0f, 1f)] float v)
        {
            var f = new FaceCustomization();
            for (int i = FaceSliders.HeadCount; i < FaceSliders.Count; i++) f.sliders[i] = v;
            var recipe = PlayerAppearance.ForPalette(2, f);
            Assert.AreEqual(BodyModel.BaseMesh, recipe.body);
            var g = ProceduralCharacterBuilder.GenerateData(recipe);
            Assert.Less(g.VertexCount, ProceduralCharacterBuilder.VertexBudget);
            Assert.Greater(g.VertexCount, BaseBody.Get().Body.VertexCount, "body + trunks + head + hair");
            float minY = float.MaxValue;
            for (int i = 0; i < g.VertexCount; i++)
            {
                Vector3 p = g.Positions[i], n = g.Normals[i];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsNaN(n.x + n.y + n.z), "NaN at " + i);
                Assert.AreEqual(1f, n.magnitude, 0.01f, "normal at " + i);
                var w = g.Weights[i];
                Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 1e-3f, "weights at " + i);
                minY = Mathf.Min(minY, p.y);
            }
            Assert.AreEqual(0f, minY, 0.03f, "feet on the ground");
            // the body's chin meets the Toon Head's chin, and the hands hang off the wrists
            var bb = BaseBody.Get();
            float s = recipe.height / ProceduralCharacterBuilder.RefHeight;
            Assert.AreEqual(bb.ChinY * s, g.BonePositions[(int)CharacterBone.Head].y - 0.027f * recipe.headSize * s, 0.005f);
            Assert.Less(g.BonePositions[(int)CharacterBone.RightIndexIntermediate].y, g.BonePositions[(int)CharacterBone.RightHand].y);
            Assert.Greater(g.BonePositions[(int)CharacterBone.RightHand].x, 0.3f, "right arm on +X");
        }

        [Test]
        public void BaseMeshBody_WeightsFollowTheirLimbs()
        {
            var bb = BaseBody.Get();
            var body = bb.Body;
            int hand = 0, foot = 0, neckTop = 0;
            for (int v = 0; v < body.VertexCount; v++)
            {
                Vector3 p = body.Positions[v];
                int b = body.BoneIndex[v * 4];
                // the neck tube carries on up inside the Toon Head and turns with it there
                if (p.y > 1.95f) { Assert.AreEqual((int)CharacterBone.Head, b, "neck top on the head at " + v); neckTop++; }
                // the lowest points are feet / toes; the right hand hangs out at +X in the A-pose
                if (p.y < 0.03f)
                {
                    var bone = (CharacterBone)b;
                    Assert.IsTrue(bone == CharacterBone.LeftFoot || bone == CharacterBone.LeftToes || bone == CharacterBone.RightFoot || bone == CharacterBone.RightToes, "sole on " + bone + " at " + v);
                    foot++;
                }
                if (p.x > 0.55f) { Assert.GreaterOrEqual(b, (int)CharacterBone.RightLowerArm, "right hand area at " + v); hand++; }
            }
            Assert.Greater(foot, 20);
            Assert.Greater(hand, 20);
            Assert.Greater(neckTop, 20, "the neck reaches up inside the head");
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
