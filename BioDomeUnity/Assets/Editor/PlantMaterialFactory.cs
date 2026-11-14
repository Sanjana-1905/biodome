using System.IO;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Creates the extra plant materials and generated textures the new species need. Everything is created only if it is
    /// missing: existing materials (including BioDome_Leaf and BioDome_Stem) and textures are never modified.
    /// </summary>
    internal static class PlantMaterialFactory
    {
        public const string MaterialFolder = "Assets/Art/Materials";
        public const string TextureFolder = "Assets/Art/Textures";

        public const string Frond = "BioDome_Frond";
        public const string LeafGlossy = "BioDome_LeafGlossy";
        public const string Calathea = "BioDome_Calathea";
        public const string Moss = "BioDome_Moss";
        public const string Spathe = "BioDome_Spathe";
        public const string Spadix = "BioDome_Spadix";
        public const string OrchidPetal = "BioDome_OrchidPetal";
        public const string OrchidLip = "BioDome_OrchidLip";
        public const string BirdSepal = "BioDome_BirdSepal";
        public const string BirdPetal = "BioDome_BirdPetal";
        public const string BananaBud = "BioDome_BananaBud";

        private const string VeinTexturePath = "Assets/Art/Textures/BioDome_LeafVeins.png";
        private const string CalatheaTexturePath = "Assets/Art/Textures/BioDome_CalatheaLeaf.png";
        private const string MossTexturePath = "Assets/Art/Textures/BioDome_MossSpeckle.png";

        public static Material Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
        }

        /// <summary>Makes sure every material and texture exists. Returns a short log of what was created.</summary>
        public static string EnsureAll()
        {
            GreenhouseAssetFactory.EnsureFolder(MaterialFolder);
            GreenhouseAssetFactory.EnsureFolder(TextureFolder);
            var log = new System.Text.StringBuilder();

            Texture2D veins = AssetDatabase.LoadAssetAtPath<Texture2D>(VeinTexturePath);
            Texture2D calathea = EnsureTexture(CalatheaTexturePath, 256, 512, CalatheaPixel, log);
            Texture2D moss = EnsureTexture(MossTexturePath, 128, 128, MossPixel, log);

            // Leaves and fronds (colour comes from the generator's per-variant tint; the material carries texture and gloss).
            Ensure(Frond, null, Color.white, 0.28f, false, log); // only ever used by two-sided geometry made with PlantParts, so back faces can be culled
            Ensure(LeafGlossy, veins, Color.white, 0.68f, true, log);
            Ensure(Calathea, calathea, Color.white, 0.38f, true, log);
            Ensure(Moss, moss, Color.white, 0.04f, true, log);

            // Flowers.
            Ensure(Spathe, null, new Color(0.78f, 0.06f, 0.07f), 0.82f, true, log);
            Ensure(Spadix, null, new Color(0.96f, 0.86f, 0.52f), 0.35f, true, log);
            Ensure(OrchidPetal, null, new Color(0.96f, 0.90f, 0.95f), 0.5f, true, log);
            Ensure(OrchidLip, null, new Color(0.78f, 0.12f, 0.50f), 0.42f, true, log);
            Ensure(BirdSepal, null, new Color(0.98f, 0.45f, 0.06f), 0.5f, true, log);
            Ensure(BirdPetal, null, new Color(0.22f, 0.25f, 0.82f), 0.5f, true, log);
            Ensure(BananaBud, null, new Color(0.42f, 0.07f, 0.22f), 0.62f, true, log);
            return log.ToString();
        }

        private static void Ensure(string name, Texture2D map, Color color, float smoothness, bool twoSided, System.Text.StringBuilder log)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Lit shader not found. Is the Universal Render Pipeline package installed?");

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            if (map != null) material.SetTexture("_BaseMap", map);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            if (twoSided) material.SetFloat("_Cull", 0f);
            AssetDatabase.CreateAsset(material, path);
            log.AppendLine($"  created material: {path}");
        }

        private delegate Color PixelFunction(float u, float v);

        private static Texture2D EnsureTexture(string path, int width, int height, PixelFunction pixel, System.Text.StringBuilder log)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var colors = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    colors[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            }
            texture.SetPixels(colors);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"  created texture: {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Calathea leaf: u runs across the blade (0.5 on the midrib), v from the base to the tip.
        /// Pale herringbone stripes sweep out from a pink midrib towards the tip on a deep green leaf, with a dark margin.
        /// </summary>
        private static Color CalatheaPixel(float u, float v)
        {
            float across = Mathf.Abs(u - 0.5f) * 2f; // 0 on the midrib, 1 at the margin
            float phase = v * 12f - across * 2.8f;   // stripes lean towards the tip as they leave the midrib
            float stripe = Mathf.Abs(Mathf.Repeat(phase, 1f) - 0.5f) * 2f;
            float band = Edge(0.3f, 0.7f, stripe);

            var dark = new Color(0.07f, 0.28f, 0.11f);
            var light = new Color(0.58f, 0.80f, 0.46f);
            Color color = Color.Lerp(dark, light, band * 0.9f);

            color = Color.Lerp(color, new Color(0.86f, 0.62f, 0.68f), 1f - Edge(0f, 0.1f, across)); // pink midrib
            color = Color.Lerp(color, dark * 0.75f, Edge(0.86f, 1f, across));                               // dark margin
            color.a = 1f;
            return color;
        }

        /// <summary>GLSL-style smoothstep: 0 below <paramref name="e0"/>, 1 above <paramref name="e1"/>. (Mathf.SmoothStep interpolates values instead.)</summary>
        private static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Soft green speckle for moss: light values with gentle variation that the tint multiplies.</summary>
        private static Color MossPixel(float u, float v)
        {
            float n1 = Mathf.PerlinNoise(u * 12f, v * 12f);
            float n2 = Mathf.PerlinNoise(u * 31f + 7f, v * 31f + 3f);
            float value = Mathf.Clamp01(0.62f + 0.3f * n1 + 0.2f * n2);
            return new Color(value, Mathf.Min(1f, value * 1.04f), value * 0.92f, 1f);
        }
    }
}
