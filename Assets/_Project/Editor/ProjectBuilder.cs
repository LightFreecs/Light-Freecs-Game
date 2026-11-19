using System.IO;
using LightMMORPG.Config;
using LightMMORPG.Core;
using UnityEditor;
using UnityEngine;

namespace LightMMORPG.EditorTools
{
    /// <summary>
    /// Generates project assets from code (sessions cannot click in the editor). Idempotent: re-running overwrites.
    /// Menu: LightMMORPG/Build All. Batch: -executeMethod LightMMORPG.EditorTools.ProjectBuilder.BuildAll
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string ProjectRoot = "Assets/_Project";
        public const string ResourcesDir = ProjectRoot + "/Resources";
        public const string AbilitiesDir = ProjectRoot + "/Data/Abilities";
        public const string GameConfigPath = ResourcesDir + "/GameConfig.asset";

        /// <summary>
        /// Saves a regenerated scene, but keeps the previous file when only Unity's random object fileIDs differ,
        /// so re-running BuildAll leaves git clean.
        /// </summary>
        public static void SaveSceneStable(UnityEngine.SceneManagement.Scene scene, string path)
        {
            byte[] before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
            if (before == null) return;
            if (NormalizeYaml(System.Text.Encoding.UTF8.GetString(before)) != NormalizeYaml(File.ReadAllText(path))) return;
            File.WriteAllBytes(path, before);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        // Order-independent fingerprint of a Unity YAML file with all local fileIDs blanked out.
        private static string NormalizeYaml(string text)
        {
            var docs = text.Replace("\r\n", "\n").Split(new[] { "\n--- " }, System.StringSplitOptions.None);
            for (int i = 0; i < docs.Length; i++)
            {
                docs[i] = System.Text.RegularExpressions.Regex.Replace(docs[i], @"&-?\d+", "&#");
                docs[i] = System.Text.RegularExpressions.Regex.Replace(docs[i], @"fileID: -?\d+", "fileID: #");
            }
            System.Array.Sort(docs, System.StringComparer.Ordinal);
            return string.Join("\n---\n", docs);
        }

        [MenuItem("LightMMORPG/Build All")]
        public static void BuildAll()
        {
            Debug.Log("[ProjectBuilder] BuildAll start");
            BuildArt();
            BuildConfig();
            BuildPrefabs();
            BuildArenaScene();
            BuildMainMenuScene();
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectBuilder] BuildAll done");
        }


        [MenuItem("LightMMORPG/Build Config")]
        public static void BuildConfig()
        {
            EnsureFolder(ResourcesDir);
            EnsureFolder(AbilitiesDir);

            var blast = CreateOrLoad<AbilityDefinition>(AbilitiesDir + "/EnergyBlast.asset");
            blast.id = "energy_blast";
            blast.displayName = "Energy Blast";
            blast.kind = AbilityKind.Projectile;
            blast.energyCost = 10f;
            blast.cooldown = 0.35f;
            blast.damageMin = 45f;
            blast.damageMax = 45f;
            blast.maxChargeTime = 0f;
            blast.speed = 40f;
            blast.radius = 0.4f;
            blast.range = 80f;
            blast.duration = 2f;
            blast.damageMultiplier = 1f;
            blast.speedMultiplier = 1f;
            blast.pausesEnergyRegen = false;
            EditorUtility.SetDirty(blast);

            var beam = CreateOrLoad<AbilityDefinition>(AbilitiesDir + "/ChargedBeam.asset");
            beam.id = "charged_beam";
            beam.displayName = "Charged Beam";
            beam.kind = AbilityKind.Beam;
            beam.energyCost = 35f;
            beam.cooldown = 4f;
            beam.damageMin = CombatConstants.BeamMinDamage;
            beam.damageMax = CombatConstants.BeamMaxDamage;
            beam.maxChargeTime = CombatConstants.BeamMaxChargeTime;
            beam.speed = 0f;
            beam.radius = 1f;
            beam.range = 40f;
            beam.duration = 0.4f;
            beam.damageMultiplier = 1f;
            beam.speedMultiplier = 1f;
            beam.pausesEnergyRegen = false;
            EditorUtility.SetDirty(beam);

            var surge = CreateOrLoad<AbilityDefinition>(AbilitiesDir + "/PowerSurge.asset");
            surge.id = "power_surge";
            surge.displayName = "Power Surge";
            surge.kind = AbilityKind.Buff;
            surge.energyCost = 50f;
            surge.cooldown = 20f;
            surge.damageMin = 0f;
            surge.damageMax = 0f;
            surge.maxChargeTime = 0f;
            surge.speed = 0f;
            surge.radius = 0f;
            surge.range = 0f;
            surge.duration = CombatConstants.SurgeDuration;
            surge.damageMultiplier = CombatConstants.SurgeDamageMultiplier;
            surge.speedMultiplier = CombatConstants.SurgeSpeedMultiplier;
            surge.pausesEnergyRegen = true;
            EditorUtility.SetDirty(surge);

            // Keep material/VFX references other builders set; only (re)write stats and ability refs.
            var config = CreateOrLoad<GameConfig>(GameConfigPath);
            var defaults = ScriptableObject.CreateInstance<GameConfig>();
            CopyStats(defaults, config);
            Object.DestroyImmediate(defaults);
            config.energyBlast = blast;
            config.chargedBeam = beam;
            config.powerSurge = surge;
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuilder] BuildConfig: wrote " + GameConfigPath + " and 3 abilities");
        }

        // ---------------- helpers ----------------

        private static void CopyStats(GameConfig from, GameConfig to)
        {
            to.maxHealth = from.maxHealth;
            to.maxEnergy = from.maxEnergy;
            to.energyRegenPerSecond = from.energyRegenPerSecond;
            to.energyChargePerSecond = from.energyChargePerSecond;
            to.maxGuard = from.maxGuard;
            to.knockedOutDuration = from.knockedOutDuration;
            to.walkSpeed = from.walkSpeed;
            to.runSpeed = from.runSpeed;
            to.flySpeed = from.flySpeed;
            to.dashSpeed = from.dashSpeed;
            to.dashDuration = from.dashDuration;
            to.dashCooldown = from.dashCooldown;
            to.flashStepDistance = from.flashStepDistance;
            to.flashStepCooldown = from.flashStepCooldown;
            to.lightComboDamage = (float[])from.lightComboDamage.Clone();
            to.comboWindow = from.comboWindow;
            to.heavyDamage = from.heavyDamage;
            to.heavyCooldown = from.heavyCooldown;
            to.meleeHitRadius = from.meleeHitRadius;
            to.meleeHitForwardOffset = from.meleeHitForwardOffset;
            to.lightHitstun = from.lightHitstun;
            to.heavyHitstun = from.heavyHitstun;
            to.blockMoveMultiplier = from.blockMoveMultiplier;
            to.blockDamageReduction = from.blockDamageReduction;
            to.guardBreakStun = from.guardBreakStun;
        }

        public static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
