using System;
using System.IO;
using BioDome.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Creates the shared textures and URP materials used by the generated greenhouse.
    /// Everything is generated in code (no external art). Existing assets are reused so hand edits survive
    /// a rebuild; pass overwrite = true to restore the defaults.
    /// </summary>
    internal static class GreenhouseAssetFactory
    {
        public const string MaterialsFolder = "Assets/Art/Materials";
        public const string TexturesFolder = "Assets/Art/Textures";
        public const string MeshFolder = "Assets/Art/Procedural";
        public const string SettingsFolder = "Assets/ScriptableObjects/Settings";

        // --- Folders -------------------------------------------------------------

        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // --- Materials resolved through the settings asset ------------------------

        /// <summary>
        /// Fills every empty material slot of <paramref name="s"/> with a default asset.
        /// With reset = true every slot is replaced by (and the default asset reconfigured to) the default.
        /// </summary>
        public static void ResolveMaterials(GreenhouseSettings s, bool reset)
        {
            // Precondition: callers must pass a live, saved asset (loaded after any scene change). Fail loudly
            // with the real cause instead of letting SetDirty throw later.
            if (s == null || !EditorUtility.IsPersistent(s))
                throw new ArgumentException(
                    $"{nameof(GreenhouseSettings)} must be a valid asset on disk. Reload it with AssetDatabase.LoadAssetAtPath after any scene change.",
                    nameof(s));

            EnsureFolder(MaterialsFolder);
            EnsureFolder(TexturesFolder);

            Texture2D pavers = Texture("BioDome_Pavers", 256, PaversPixel, reset);
            Texture2D soil = Texture("BioDome_Soil", 128, SoilPixel, reset);
            Texture2D gravel = Texture("BioDome_Gravel", 256, GravelPixel, reset);
            Texture2D grass = Texture("BioDome_Grass", 256, GrassPixel, reset);
            Texture2D wood = Texture("BioDome_Wood", 128, WoodPixel, reset);
            Texture2D stone = Texture("BioDome_Stone", 128, StonePixel, reset);

            if (reset || s.steel == null)
                s.steel = Lit("BioDome_Steel", new Color(0.2f, 0.24f, 0.23f), 0.85f, 0.6f, null, 1f, reset);
            if (reset || s.glass == null)
                s.glass = Transparent("BioDome_Glass", new Color(0.7f, 0.88f, 0.9f, 0.12f), 0.95f, reset);
            if (reset || s.stone == null)
                s.stone = Lit("BioDome_Stone", new Color(0.92f, 0.92f, 0.9f), 0f, 0.25f, stone, 1f / 2f, reset);
            if (reset || s.timber == null)
                s.timber = Lit("BioDome_Timber", Color.white, 0f, 0.3f, wood, 1f / 1.5f, reset);
            if (reset || s.soil == null)
                s.soil = Lit("BioDome_Soil", Color.white, 0f, 0.08f, soil, 1f / 2f, reset);
            if (reset || s.pavers == null)
                s.pavers = Lit("BioDome_Pavers", Color.white, 0f, 0.2f, pavers, 1f / 2.4f, reset);
            if (reset || s.gravel == null)
                s.gravel = Lit("BioDome_Gravel", Color.white, 0f, 0.1f, gravel, 1f / 3f, reset);
            if (reset || s.ground == null)
                s.ground = Lit("BioDome_Ground", Color.white, 0f, 0.05f, grass, 1f / 6f, reset);
            if (reset || s.water == null)
                s.water = Transparent("BioDome_Water", new Color(0.12f, 0.42f, 0.48f, 0.75f), 0.95f, reset);
            if (reset || s.pipe == null)
                s.pipe = Lit("BioDome_Pipe", new Color(0.1f, 0.2f, 0.32f), 0f, 0.45f, null, 1f, reset);
            if (reset || s.lampBulb == null)
                s.lampBulb = Emissive("BioDome_LampBulb", new Color(1f, 0.95f, 0.8f), new Color(1f, 0.82f, 0.55f) * 3f, reset);

            EditorUtility.SetDirty(s);
        }

        public static Material Sky(bool overwrite)
        {
            EnsureFolder(MaterialsFolder);
            const string path = MaterialsFolder + "/BioDome_Sky.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !overwrite) return m;

            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;

            bool created = m == null;
            if (created) m = new Material(shader);
            m.shader = shader;
            m.SetFloat("_SunSize", 0.045f);
            m.SetFloat("_AtmosphereThickness", 0.9f);
            m.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.6f));
            m.SetColor("_GroundColor", new Color(0.42f, 0.4f, 0.36f));
            m.SetFloat("_Exposure", 1.25f);
            Save(m, path, created);
            return m;
        }

        public static Material WaterDroplet(bool overwrite)
        {
            EnsureFolder(MaterialsFolder);
            const string path = MaterialsFolder + "/BioDome_WaterDroplet.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !overwrite) return m;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return null;

            bool created = m == null;
            if (created) m = new Material(shader);
            m.shader = shader;
            m.SetColor("_BaseColor", new Color(0.85f, 0.95f, 1f, 0.7f));
            MakeTransparent(m, false);
            Save(m, path, created);
            return m;
        }

        // --- Material builders ----------------------------------------------------

        private static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D map, float tiling, bool overwrite)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !overwrite) return m;

            bool created = m == null;
            if (created) m = new Material(LitShader());
            m.shader = LitShader();
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (map != null)
            {
                m.SetTexture("_BaseMap", map);
                m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            }
            Save(m, path, created);
            return m;
        }

        private static Material Transparent(string name, Color color, float smoothness, bool overwrite)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !overwrite) return m;

            bool created = m == null;
            if (created) m = new Material(LitShader());
            m.shader = LitShader();
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            MakeTransparent(m, true);
            Save(m, path, created);
            return m;
        }

        private static Material Emissive(string name, Color color, Color emission, bool overwrite)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !overwrite) return m;

            bool created = m == null;
            if (created) m = new Material(LitShader());
            m.shader = LitShader();
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.3f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Save(m, path, created);
            return m;
        }

        /// <summary>Switches a URP Lit / Particles material to the Transparent surface type with alpha blending.</summary>
        private static void MakeTransparent(Material m, bool doubleSided)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Shader LitShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader not found. Is the Universal Render Pipeline package installed?");
            return shader;
        }

        private static void Save(Material m, string path, bool created)
        {
            if (created) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// Blur fix: the ground / prop textures were generated at only 128-256 px and tiled over 1.5-2.4 m, so close to the floor, beds and benches they were magnified into
        /// soft blocks. This regenerates them from the same pixel functions at 4x the resolution (same look, sharp detail), keeping every material reference (same file, same GUID),
        /// and turns on trilinear filtering with 8x anisotropy for all BioDome textures so they stay sharp at grazing angles.
        /// </summary>
        [MenuItem("BioDome/Rendering/Upgrade Textures To High Resolution")]
        public static void UpgradeTexturesHighRes()
        {
            Texture("BioDome_Pavers", 1024, PaversPixel, true);
            Texture("BioDome_Soil", 512, SoilPixel, true);
            Texture("BioDome_Gravel", 1024, GravelPixel, true);
            Texture("BioDome_Grass", 1024, GrassPixel, true);
            Texture("BioDome_Wood", 512, WoodPixel, true);
            Texture("BioDome_Stone", 512, StonePixel, true);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("BioDome textures upgraded to high resolution.");
        }

        public static void UpgradeTexturesBatch()
        {
            UpgradeTexturesHighRes();
            EditorApplication.Exit(0);
        }

        // --- Textures -------------------------------------------------------------

        private static Texture2D Texture(string name, int size, Func<float, float, Color> pixel, bool overwrite)
        {
            string path = $"{TexturesFolder}/{name}.png";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null && !overwrite) return existing;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            File.WriteAllBytes(Path.GetFullPath(path), png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Each function receives u, v in 0..1 and must tile seamlessly.

        private static Color PaversPixel(float u, float v)
        {
            const int tiles = 4;
            int row = Mathf.FloorToInt(v * tiles);
            float offset = (row % 2) * 0.5f; // staggered rows (4 columns, so the half-tile shift still wraps)
            float su = u * tiles + offset;
            int col = Mathf.FloorToInt(su);
            float fu = su - col;
            float fv = v * tiles - row;

            float edge = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
            if (edge < 0.04f) return new Color(0.33f, 0.31f, 0.28f);

            float tone = 0.72f + 0.28f * Hash(Wrap(col, tiles), row, 11);
            float grain = 0.93f + 0.14f * Noise(u, v, 32, 32, 5);
            return new Color(0.78f, 0.72f, 0.62f) * (tone * grain);
        }

        private static Color SoilPixel(float u, float v)
        {
            float n = 0.55f * Noise(u, v, 8, 8, 3) + 0.3f * Noise(u, v, 24, 24, 4) + 0.15f * Noise(u, v, 64, 64, 5);
            // Specks come from smooth tileable noise (not per-cell hashes, which show up as hard square blocks when magnified).
            float speck = 1f + 0.4f * Mathf.SmoothStep(0.70f, 0.90f, Noise(u, v, 128, 128, 9));
            return new Color(0.105f, 0.08f, 0.058f) * (0.6f + 0.8f * n) * speck; // dark, rich potting soil (was a lighter brown that dominated the beds)
        }

        private static Color GravelPixel(float u, float v)
        {
            float n = 0.5f * Noise(u, v, 12, 12, 2) + 0.5f * Noise(u, v, 48, 48, 6);
            float pebble = 0.6f * Noise(u, v, 128, 128, 21) + 0.4f * Noise(u, v, 256, 256, 22); // smooth fine grain: no square cells
            return new Color(0.62f, 0.58f, 0.52f) * (0.7f + 0.3f * n + 0.25f * pebble);
        }

        private static Color GrassPixel(float u, float v)
        {
            float n = 0.5f * Noise(u, v, 6, 6, 8) + 0.3f * Noise(u, v, 20, 20, 9) + 0.2f * Noise(u, v, 80, 80, 10);
            Color dry = new Color(0.36f, 0.38f, 0.18f);
            Color lush = new Color(0.2f, 0.36f, 0.14f);
            return Color.Lerp(dry, lush, n);
        }

        private static Color WoodPixel(float u, float v)
        {
            // Grain runs along u; low frequency along u, high along v.
            float grain = 0.6f * Noise(u, v, 2, 24, 14) + 0.4f * Noise(u, v, 4, 64, 15);
            float plank = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 4f)) < 0.04f ? 0.55f : 1f;
            return new Color(0.5f, 0.34f, 0.2f) * (0.65f + 0.5f * grain) * plank;
        }

        private static Color StonePixel(float u, float v)
        {
            float n = 0.5f * Noise(u, v, 6, 6, 31) + 0.3f * Noise(u, v, 20, 20, 32) + 0.2f * Noise(u, v, 64, 64, 33);
            float speck = 1f - 0.2f * Mathf.SmoothStep(0.70f, 0.88f, Noise(u, v, 128, 128, 34));
            return new Color(0.58f, 0.58f, 0.56f) * (0.7f + 0.5f * n) * speck;
        }

        // --- Tileable value noise -------------------------------------------------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        private static int Wrap(int value, int period)
        {
            return ((value % period) + period) % period;
        }

        /// <summary>Smooth value noise on a lattice of fx by fy cells that repeats every texture.</summary>
        private static float Noise(float u, float v, int fx, int fy, int seed)
        {
            float x = u * fx;
            float y = v * fy;
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float tx = x - x0;
            float ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);

            int xa = Wrap(x0, fx), xb = Wrap(x0 + 1, fx);
            int ya = Wrap(y0, fy), yb = Wrap(y0 + 1, fy);

            float top = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), tx);
            float bottom = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), tx);
            return Mathf.Lerp(top, bottom, ty);
        }
    }
}
