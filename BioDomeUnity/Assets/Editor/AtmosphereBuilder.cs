using System.Text;
using BioDome.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Atmosphere of the conservatory: warm sunlight, a golden-green ambient, gentle haze, a soft post-processing profile (a copy made for this scene, the shared template
    /// profile is not touched) and a few cheap, soft mist banks. No extra real-time lights. Does not save the scene (ConservatorySetup does).
    /// </summary>
    public static class AtmosphereBuilder
    {
        public const string RootName = "BioDome_Atmosphere";
        private const string ProfilePath = "Assets/Settings/BioDome_Atmosphere.asset";
        private const string TexturePath = "Assets/Art/Textures/Mist_Soft.png";

        public static string Build()
        {
            var log = new StringBuilder();

            // --- Sun and ambient ------------------------------------------------------
            GameObject sunObject = GameObject.Find("Sun");
            if (sunObject != null)
            {
                var sun = sunObject.GetComponent<Light>();
                sun.color = new Color(1f, 0.92f, 0.78f);       // warm natural sunlight
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
                EditorUtility.SetDirty(sun);
            }
            var env = Object.FindFirstObjectByType<EnvironmentController>();
            if (env != null)
            {
                var so = new SerializedObject(env);
                so.FindProperty("maxSunIntensity").floatValue = 1.6f;
                so.FindProperty("startTimeOfDayHours").floatValue = 10f; // start in bright morning light, not at dawn
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.78f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.66f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.42f, 0.31f);
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.fog = false; // clear air: global fog made the conservatory look murky
            log.AppendLine("  sun warm (1.0, 0.92, 0.78), peak intensity 1.6, trilight ambient, fog OFF, start time 10:00");

            // --- Post-processing profile (own copy) --------------------------------------
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.CopyAsset("Assets/Settings/SampleSceneProfile.asset", ProfilePath))
                { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
                else profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            }
            if (profile != null)
            {
                if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
                bloom.active = true; bloom.threshold.Override(1.05f); bloom.intensity.Override(0.28f); bloom.scatter.Override(0.68f); bloom.tint.Override(new Color(1f, 0.95f, 0.85f));
                if (!profile.TryGet(out Tonemapping tone)) tone = profile.Add<Tonemapping>(true);
                tone.active = true; tone.mode.Override(TonemappingMode.ACES);
                if (!profile.TryGet(out ColorAdjustments color)) color = profile.Add<ColorAdjustments>(true);
                color.active = true; color.postExposure.Override(0.25f); color.contrast.Override(8f); color.saturation.Override(12f); color.colorFilter.Override(new Color(1f, 0.975f, 0.92f));
                if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>(true);
                vignette.active = true; vignette.intensity.Override(0.2f); vignette.smoothness.Override(0.45f);
                if (profile.TryGet(out MotionBlur blur)) blur.active = false;
                EditorUtility.SetDirty(profile);

                var volume = Object.FindFirstObjectByType<Volume>();
                if (volume != null) { volume.sharedProfile = profile; EditorUtility.SetDirty(volume); log.AppendLine("  post-processing: ACES tonemapping, soft warm bloom, colour grade, light vignette (profile copy: BioDome_Atmosphere)"); }
            }

            // --- Mist ----------------------------------------------------------------------
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            Material mist = MistMaterial();
            if (mist == null) { log.AppendLine("  (mist skipped: URP particle shader not found)"); return log.ToString(); }

            // Low banks drifting over the planted areas (kept few and soft: transparent overdraw is the expensive part on a laptop GPU).
            var banks = new[]
            {
                new Vector3(-9f, 0.45f, 6.5f), new Vector3(-9f, 0.45f, -6.5f), new Vector3(2f, 0.45f, 6.5f), new Vector3(2f, 0.45f, -6.5f),
                new Vector3(26f, 0.45f, 6.6f), new Vector3(26f, 0.45f, -6.6f), new Vector3(40f, 0.45f, 4.5f), new Vector3(40f, 0.45f, -4.5f),
            };
            // Ground mist banks are no longer created (they read as haze); only the small fountain spray remains.
            for (int i = 0; i < 0 /* banks disabled */; i++)
                MistSystem(root.transform, "MistBank_" + (i + 1), banks[i], mist, new Vector3(9f, 0.5f, 4f), 0.9f, 20f, 3.2f, 5.0f, 0.09f);
            // Fountain haze.
            MistSystem(root.transform, "FountainMist", new Vector3(ConservatoryExpansion.PlazaX, 0.55f, 0f), mist, new Vector3(2.6f, 0.2f, 2.6f), 2.5f, 5f, 0.9f, 1.6f, 0.10f);
            log.AppendLine("  mist: fountain spray only (ground mist banks removed)");
            return log.ToString();
        }

        private static Material MistMaterial()
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (tex == null)
            {
                GreenhouseAssetFactory.EnsureFolder("Assets/Art/Textures");
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a); // smoothstep: soft edge, no visible quad
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                System.IO.File.WriteAllBytes(TexturePath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(TexturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            }
            return PlantKit.TransparentMaterial("Assets/Art/Materials/Experiment/Mist.mat", new Color(0.92f, 0.97f, 0.9f, 1f), "Universal Render Pipeline/Particles/Unlit", tex, true);
        }

        private static void MistSystem(Transform parent, string name, Vector3 pos, Material mat, Vector3 box, float rate, float life, float sizeMin, float sizeMax, float alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = life;
            main.startSpeed = 0.06f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, alpha);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;

            var emission = ps.emission; emission.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;

            var colour = ps.colorOverLifetime; colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;

            var velocity = ps.velocityOverLifetime; velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); velocity.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f); velocity.y = new ParticleSystem.MinMaxCurve(0f, 0.02f); // all three in the same (two-constants) mode

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 5f;
            ps.Play();
        }
    }
}
