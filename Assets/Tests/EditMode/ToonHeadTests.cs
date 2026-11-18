using LightMMORPG.Characters;
using NUnit.Framework;
using UnityEngine;

namespace LightMMORPG.Tests
{
    public sealed class ToonHeadTests
    {
        [Test]
        public void AllShapePresets_BuildValidMesh([Values(0, 1, 2, 3, 4, 5, 6, 7)] int preset)
        {
            var shape = ToonHead.Apply(ToonHeadPresets.Get(preset));
            var m = ToonHead.BuildMesh(shape);
            int n = m.Positions.Count;
            Assert.Greater(n, 4000);
            Assert.AreEqual(n, m.Normals.Count);
            Assert.AreEqual(n, m.FaceCoords.Count);
            Assert.AreEqual(n, m.NeckBlend.Count);
            Assert.AreEqual(0, m.Triangles.Count % 3);
            Assert.AreEqual(m.Triangles.Count / 3, m.HeadTriangleCount + m.EarTriangleCount);
            foreach (int i in m.Triangles) Assert.That(i, Is.InRange(0, n - 1));
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = m.Positions[i], nn = m.Normals[i], fc = m.FaceCoords[i];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsInfinity(p.x + p.y + p.z), "position NaN at " + i);
                Assert.AreEqual(1f, nn.magnitude, 0.01f, "normal not unit at " + i);
                Assert.That(fc.z, Is.InRange(0f, 1f), "face mask");
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            Assert.AreEqual(shape.Top, maxY, 0.02f, "crown at the top");
            Assert.Less(minY, -0.1f, "neck below the chin");
            // the face looks along +Z: the nose tip is the frontmost point at the centre
            Vector3 nose = ToonHead.Surface(shape, 0f, shape.NoseY);
            Assert.Greater(nose.z, ToonHead.Surface(shape, 0f, shape.NoseY + 0.12f).z);
        }

        [Test]
        public void OutwardNormals_PointAwayFromTheHeadCentre()
        {
            var shape = ToonHead.Apply(new ToonHeadSettings());
            var m = ToonHead.BuildMesh(shape);
            var c = new Vector3(0f, shape.CrY, 0f);
            int outward = 0, checkedCount = 0;
            for (int i = 0; i < m.Positions.Count; i++)
            {
                if (m.Positions[i].y < 0.45f || m.FaceCoords[i].z <= 0f && m.Positions[i].y < 0.7f) continue;   // skull only
                checkedCount++;
                if (Vector3.Dot(m.Normals[i], m.Positions[i] - c) > 0f) outward++;
            }
            Assert.Greater(checkedCount, 500);
            Assert.Greater(outward, checkedCount * 0.98f);
            // Unity winding (clockwise seen from outside): the first skull triangle's geometric normal agrees with the vertex normals
            int a = m.Triangles[0], b = m.Triangles[1], d = m.Triangles[2];
            Vector3 geo = Vector3.Cross(m.Positions[b] - m.Positions[a], m.Positions[d] - m.Positions[a]);
            Assert.Greater(Vector3.Dot(geo, m.Normals[a] + m.Normals[b] + m.Normals[d]), 0f);
        }

        [Test]
        public void Sliders_ChangeTheShape()
        {
            var baseShape = ToonHead.Apply(new ToonHeadSettings());
            var longFace = ToonHead.Apply(new ToonHeadSettings { faceLength = 1.2f });
            Assert.Greater(longFace.Top, baseShape.Top);
            Assert.Greater(longFace.EyeY, baseShape.EyeY, "eyes move up when the lower face gets longer");
            var wide = ToonHead.Apply(new ToonHeadSettings { headWidth = 1.1f });
            ToonHead.FaceAt(baseShape, 0.5f, out float w0, out _, out _, out _);
            ToonHead.FaceAt(wide, 0.5f, out float w1, out _, out _, out _);
            Assert.Greater(w1, w0 * 1.08f);
        }

        [Test]
        public void FacePainter_PaintsBothEyesMirrored([Values(ToonEyeStyle.Drawn, ToonEyeStyle.Tall, ToonEyeStyle.Wide)] ToonEyeStyle style)
        {
            var shape = ToonHead.Apply(new ToonHeadSettings { eyeStyle = style });
            var px = ToonFacePainter.Paint(shape, style, new Color(0.9f, 0.5f, 0.1f), Color.black, new Color(1f, 0.85f, 0.72f));
            int N = ToonFacePainter.Size;
            Assert.AreEqual(N * N, px.Length);
            // eye centre (right side) is painted, the cheek below is clear
            Vector2 eye = ToonFacePainter.Uv(new Vector3(shape.EyeX, shape.EyeY, 0f)) * N;
            Vector2 cheek = ToonFacePainter.Uv(new Vector3(shape.EyeX, shape.EyeY - 0.17f, 0f)) * N;
            Assert.Greater(px[(int)eye.y * N + (int)eye.x].a, 0.9f, "eye painted");
            Assert.Less(px[(int)cheek.y * N + (int)cheek.x].a, 0.01f, "cheek clear");
            // mirror symmetry of the coverage
            int asym = 0, painted = 0;
            for (int y = 0; y < N; y += 4)
                for (int x = 0; x < N / 2; x += 4)
                {
                    float a = px[y * N + x].a, b = px[y * N + (N - 1 - x)].a;
                    if (a > 0.5f || b > 0.5f) painted++;
                    if (Mathf.Abs(a - b) > 0.5f) asym++;
                }
            Assert.Greater(painted, 200);
            Assert.Less(asym, painted * 0.03f);
        }

        [Test]
        public void ShadowMap_GetsDarkerTowardTheFarSide()
        {
            var s = ToonHead.Apply(new ToonHeadSettings());
            var map = ToonHead.ShadowMap(s);
            int S = ToonHead.ShadowMapSize;
            Assert.AreEqual(S * S, map.Length);
            int row = Mathf.RoundToInt((0.55f - ToonHead.ShadowV0) / ToonHead.ShadowVSpan * (S - 1));
            Assert.Less(map[row * S + 10], map[row * S + S / 2]);
            Assert.Less(map[row * S + S / 2], map[row * S + S - 10]);
        }

        [Test]
        public void RecipeClone_CopiesTheHeadSettings()
        {
            var r = TestRecipes.Variant(0);
            var c = r.Clone();
            c.toonHead.headWidth = 1.1f;
            Assert.AreEqual(1f, r.toonHead.headWidth);
            Assert.AreNotEqual(r.CacheKey(), c.CacheKey());
        }

        [Test]
        public void Character_HasToonFaceData()
        {
            var g = ProceduralCharacterBuilder.GenerateData(TestRecipes.Variant(1));
            Assert.AreEqual(g.VertexCount, g.FaceCoords.Length);
            Assert.AreEqual(g.VertexCount, g.Tangents.Length);
            Assert.AreEqual(ToonHead.ShadowMapSize * ToonHead.ShadowMapSize, g.ShadowPixels.Length);
            Assert.AreEqual(ToonFacePainter.Size * ToonFacePainter.Size, g.FacePixels.Length);
            int face = g.Slots.FindIndex(sl => sl.Kind == ProceduralCharacterBuilder.SlotKind.Face);
            Assert.GreaterOrEqual(face, 0);
            Assert.Greater(g.Triangles[face].Count / 3, 8000, "the toon head is on the face slot");
            int masked = 0;
            foreach (var f in g.FaceCoords) if (f.z > 0.5f) masked++;
            Assert.Greater(masked, 300);
        }

        [Test]
        public void Customization_SlidersAndEyeReachTheToonHead()
        {
            var f = new FaceCustomization { eyeStyle = ToonEyeStyle.HandMade };
            f.sliders.jawWidth = 1f;
            f.sliders.noseSize = -1f;
            f.sliders.eyeSize = 0.5f;
            var r = PlayerAppearance.ForPalette(2, f);
            Assert.AreEqual(ToonEyeStyle.HandMade, r.toonHead.eyeStyle);
            Assert.AreEqual(1.4f, r.toonHead.jawWidth, 1e-5f);
            Assert.AreEqual(0f, r.toonHead.noseSize, 1e-5f);
            Assert.AreEqual(1.125f, r.toonHead.eyeSize, 1e-5f);
            Assert.AreEqual(1f, r.toonHead.headWidth, 1e-5f, "untouched sliders keep the base head");
            Assert.AreNotEqual(CharacterPresets.ForPalette(2).CacheKey(), r.CacheKey());
            Assert.AreEqual(1f, CharacterPresets.Hero().toonHead.jawWidth, "the base hero is not modified");
        }

        [Test]
        public void Customization_EveryEyeOnAnyPalette([Values(ToonEyeStyle.Drawn, ToonEyeStyle.Tall, ToonEyeStyle.Wide, ToonEyeStyle.HandMade)] ToonEyeStyle eye,
            [Values(0, 3)] int palette)
        {
            var r = PlayerAppearance.ForPalette(palette, new FaceCustomization { eyeStyle = eye });
            Assert.AreEqual(eye, r.toonHead.eyeStyle);
        }

        [Test]
        public void Customization_JsonRoundTripAndSanitize()
        {
            var f = new FaceCustomization { eyeStyle = ToonEyeStyle.Wide };
            for (int i = 0; i < FaceSliders.Count; i++) f.sliders[i] = (i % 5 - 2) * 0.3f;
            var back = PlayerAppearance.FromJson(PlayerAppearance.ToJson(f));
            Assert.AreEqual(ToonEyeStyle.Wide, back.eyeStyle);
            for (int i = 0; i < FaceSliders.Count; i++) Assert.AreEqual(f.sliders[i], back.sliders[i], 1e-6f, "slider " + i);
            Assert.AreEqual(PlayerAppearance.Sliders.Length, FaceSliders.Count);

            var bad = new FaceCustomization { eyeStyle = (ToonEyeStyle)99 };
            bad.sliders.chinWidth = 7f;
            bad.sliders.earFlare = float.NaN;
            bad = bad.Sanitized();
            Assert.AreEqual(ToonEyeStyle.Drawn, bad.eyeStyle);
            Assert.AreEqual(1f, bad.sliders.chinWidth);
            Assert.AreEqual(0f, bad.sliders.earFlare);
        }

        [Test]
        public void Customization_SliderExtremes_GiveValidCharacters([Values(-1f, 1f)] float v)
        {
            var f = new FaceCustomization { eyeStyle = ToonEyeStyle.Tall };
            for (int i = 0; i < FaceSliders.Count; i++) f.sliders[i] = v;
            var g = ProceduralCharacterBuilder.GenerateData(PlayerAppearance.ForPalette(0, f));
            Assert.Less(g.VertexCount, ProceduralCharacterBuilder.VertexBudget);
            for (int k = 0; k < g.VertexCount; k++)
            {
                Vector3 p = g.Positions[k], n = g.Normals[k];
                Assert.IsFalse(float.IsNaN(p.x + p.y + p.z) || float.IsNaN(n.x + n.y + n.z), "NaN at " + k);
            }
        }
    }
}
