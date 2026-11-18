using LightMMORPG.Characters;
using NUnit.Framework;
using UnityEngine;

namespace LightMMORPG.Tests
{
    public class ToonHairTests
    {
        [TestCase(HairStyle.Light)]
        [TestCase(HairStyle.Messy)]
        public void Style_BuildsValidMesh(HairStyle style)
        {
            var shape = ToonHead.Apply(new ToonHeadSettings());
            var head = ToonHead.BuildMesh(shape);
            var m = ToonHair.Build(shape, head, style);
            Assert.Greater(m.Blades, 15);
            Assert.AreEqual(m.Positions.Count, m.Normals.Count);
            Assert.AreEqual(m.Positions.Count, m.Along.Count);
            Assert.AreEqual(0, m.Triangles.Count % 3);
            foreach (int t in m.Triangles) Assert.That(t, Is.InRange(0, m.Positions.Count - 1));
            foreach (var p in m.Positions)
            {
                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z));
                Assert.Less(p.magnitude, 3f, "hair stays near the head");
            }
            // the base layer covers the crown and faces outward (normal points away from the skull centre)
            int up = 0, top = 0;
            for (int i = 0; i < m.Positions.Count; i++)
            {
                var p = m.Positions[i];
                if (p.y < 0.9f || Mathf.Abs(p.x) > 0.1f || Mathf.Abs(p.z) > 0.1f) continue;
                top++;
                if (m.Normals[i].y > 0f) up++;
            }
            Assert.Greater(top, 0);
            Assert.Greater(up, top / 2, "crown normals point up");
        }

        [Test]
        public void Messy_HidesEars_LightKeepsThem()
        {
            Assert.IsTrue(ToonHair.HidesEars(HairStyle.Messy));
            Assert.IsFalse(ToonHair.HidesEars(HairStyle.Light));
            var light = ProceduralCharacterBuilder.GenerateData(PlayerAppearance.ForPalette(0, new FaceCustomization { hair = PlayerHair.Light }));
            var messy = ProceduralCharacterBuilder.GenerateData(PlayerAppearance.ForPalette(0, new FaceCustomization { hair = PlayerHair.Messy }));
            Assert.Greater(light.VertexCount, 0);
            Assert.Greater(messy.VertexCount, 0);
        }

        [Test]
        public void HairChoice_AppliesAndRoundTrips()
        {
            var r = PlayerAppearance.ForPalette(1, new FaceCustomization { hair = PlayerHair.Messy });
            Assert.AreEqual(HairStyle.Messy, r.hairStyle);
            Assert.AreEqual(HairStyle.Light, PlayerAppearance.ForPalette(1, default).hairStyle);
            var back = PlayerAppearance.FromJson(PlayerAppearance.ToJson(new FaceCustomization { hair = PlayerHair.Messy }));
            Assert.AreEqual(PlayerHair.Messy, back.hair);
            var bad = new FaceCustomization { hair = (PlayerHair)99 }.Sanitized();
            Assert.AreEqual(PlayerHair.Light, bad.hair);
        }

        [Test]
        public void HairColour_AnyColourOnAnyStyle()
        {
            for (int c = 0; c < PlayerAppearance.HairColors.Length; c++)
                foreach (var h in new[] { PlayerHair.Light, PlayerHair.Messy })
                {
                    var r = PlayerAppearance.ForPalette(0, new FaceCustomization { hair = h, hairColor = c });
                    Assert.AreEqual(PlayerAppearance.HairColors[c].color, r.hairColor);
                    Assert.AreEqual(PlayerAppearance.HairColors[c].color, r.hairTipColor);
                }
            Assert.AreEqual(3, PlayerAppearance.FromJson(PlayerAppearance.ToJson(new FaceCustomization { hairColor = 3 })).hairColor);
            Assert.AreEqual(0, new FaceCustomization { hairColor = 99 }.Sanitized().hairColor);
        }

        [Test]
        public void HairPreview_DrawsBothStyles()
        {
            foreach (var h in new[] { PlayerHair.Light, PlayerHair.Messy })
            {
                var tex = LightMMORPG.Menu.HairPreview.Render(PlayerAppearance.ForPalette(0, new FaceCustomization { hair = h, hairColor = 5 }), null);
                int solid = 0;
                foreach (var p in tex.GetPixels()) if (p.a > 0.5f) solid++;
                Assert.Greater(solid, tex.width * tex.height / 6, "the portrait covers a good part of the tile");
                Object.DestroyImmediate(tex);
            }
        }
    }
}
