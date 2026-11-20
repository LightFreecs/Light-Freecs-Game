using UnityEditor;
using UnityEngine;

namespace LightMMORPG.EditorTools
{
    /// <summary>
    /// Import settings for the boar (Art/Models/Boar/Boar.fbx, made by ArtSource/Boar/build_boar.py): generic rig, no root
    /// motion (the clips play in place; the "root" bone only moves in Death, to lay the boar on its side), metres (1 unit =
    /// 1 m, ~0.6 m at the shoulder), no camera / light import, and clips named after the Blender actions ("BoarRig|Run" →
    /// "Run"). Idle, Walk and Run loop; Paw, Attack (tusk hit on frame 9 = 0.3 s), Hit and Death play once. Materials stay
    /// embedded for now (the hostile-entity work swaps in ToonLit).
    /// </summary>
    public sealed class BoarModelPostprocessor : AssetPostprocessor
    {
        private static bool IsBoar(string path) => path.Replace('\\', '/').Contains("/Art/Models/Boar/");

        private void OnPreprocessModel()
        {
            if (!IsBoar(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.resampleCurves = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBoar(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                int bar = c.name.LastIndexOf('|');
                if (bar >= 0) c.name = c.name.Substring(bar + 1);
                c.loopTime = c.name == "Idle" || c.name == "Walk" || c.name == "Run";
                c.loopPose = false;
            }
            mi.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject g)
        {
            if (!IsBoar(assetPath)) return;
            // sanity log: the snout must point along +Z (the game's forward) and the hooves stand on y = 0
            var head = FindDeep(g.transform, "head");
            var r = g.GetComponentInChildren<SkinnedMeshRenderer>();
            if (head != null && r != null)
                Debug.Log($"[Boar] imported: head at {g.transform.InverseTransformPoint(head.position)}, mesh bounds {r.bounds}, " +
                          $"{r.sharedMesh.vertexCount} verts, {r.bones.Length} bones");
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
