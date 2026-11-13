using System;
using System.IO;
using BioDome.Procedural;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Creates the shared plant materials and the procedural leaf texture (midrib and lateral veins).
    /// Existing assets are reused so hand edits survive; pass overwrite = true to restore defaults.
    /// </summary>
    internal static class PlantAssetFactory
    {
        public const string LeafMaterialPath = "Assets/Art/Materials/BioDome_Leaf.mat";
        public const string StemMaterialPath = "Assets/Art/Materials/BioDome_Stem.mat";
        public const string LeafTexturePath = "Assets/Art/Textures/BioDome_LeafVeins.png";

        public static void EnsureMaterials(bool overwrite, out Material leaf, out Material stem)
        {
            GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.MaterialsFolder);
            GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.TexturesFolder);

            Texture2D veins = LeafTexture(overwrite);
            leaf = LoadOrCreate(LeafMaterialPath, overwrite, m =>
            {
                m.SetColor("_BaseColor", new Color(0.16f, 0.42f, 0.14f));
                m.SetTexture("_BaseMap", veins);
                m.SetFloat("_Smoothness", 0.38f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Cull", 0f); // two-sided, so single-surface leaves also work
            });
            stem = LoadOrCreate(StemMaterialPath, overwrite, m =>
            {
                m.SetColor("_BaseColor", new Color(0.28f, 0.4f, 0.17f));
                m.SetFloat("_Smoothness", 0.3f);
                m.SetFloat("_Metallic", 0f);
            });
        }

        private static Material LoadOrCreate(string path, bool overwrite, Action<Material> configure)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && !overwrite) return m;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader not found. Is the Universal Render Pipeline package installed?");

            bool created = m == null;
            if (created) m = new Material(shader);
            m.shader = shader;
            configure(m);

            if (created) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }

        // --- Leaf vein texture ------------------------------------------------------

        private static Texture2D LeafTexture(bool overwrite)
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(LeafTexturePath);
            if (existing != null && !overwrite) return existing;

            const int width = 256;
            const int height = 512;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = LeafPixel((x + 0.5f) / width, (y + 0.5f) / height);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            File.WriteAllBytes(Path.GetFullPath(LeafTexturePath), png);
            AssetDatabase.ImportAsset(LeafTexturePath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(LeafTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(LeafTexturePath);
        }

        /// <summary>
        /// u across the blade (0.5 = midrib), v from base (0) to tip (1). The texture is a light grey-green
        /// detail map: the material colour (tinted per variant) multiplies it, so veins read as lighter lines.
        /// Lateral veins follow the same slant as the geometry's splits, which sit between them.
        /// </summary>
        private static Color LeafPixel(float u, float v)
        {
            float t = Mathf.Abs(u * 2f - 1f);

            float mottling = 0.55f * Noise(u, v, 6, 12, 3) + 0.45f * Noise(u, v, 18, 36, 4);
            float baseTone = 0.74f + 0.14f * mottling;
            baseTone *= Mathf.Lerp(1f, 0.88f, t * t * t); // slightly darker towards the margin

            float vein = 0f;

            // Midrib, thicker at the base.
            float midribWidth = Mathf.Lerp(0.016f, 0.005f, v);
            float d = Mathf.Abs(u - 0.5f);
            if (d < midribWidth) vein = Mathf.Max(vein, 1f - d / midribWidth);

            // Lateral veins between the splits.
            if (t > 0.02f)
            {
                for (int k = 0; k < LeafMeshGenerator.LateralVeinCount; k++)
                {
                    float line = (k + 0.5f) / LeafMeshGenerator.LateralVeinCount + LeafMeshGenerator.VeinSlant * t;
                    float width = 0.007f * (1f - 0.6f * t) + 0.002f;
                    float dv = Mathf.Abs(v - line);
                    if (dv < width) vein = Mathf.Max(vein, 1f - dv / width);
                }
            }

            float tone = Mathf.Lerp(baseTone, 1f, vein * 0.9f);
            return new Color(tone * 0.96f, tone, tone * 0.9f, 1f);
        }

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

        private static float Noise(float u, float v, int fx, int fy, int seed)
        {
            float x = u * fx, y = v * fy;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            int xa = Wrap(x0, fx), xb = Wrap(x0 + 1, fx), ya = Wrap(y0, fy), yb = Wrap(y0 + 1, fy);
            float top = Mathf.Lerp(Hash(xa, ya, seed), Hash(xb, ya, seed), tx);
            float bottom = Mathf.Lerp(Hash(xa, yb, seed), Hash(xb, yb, seed), tx);
            return Mathf.Lerp(top, bottom, ty);
        }
    }
}
