using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BioDome.Environment;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Top-down footprint check of the open greenhouse scene. It reads the real geometry (every triangle of the paths, beds, benches, stones,
    /// fountain, lamps, irrigation rails, frame and entrance) and every plant, rasterises them onto a 0.1 m grid, and finds where new
    /// zones fit with walking clearance. It changes nothing: it only reads the scene and writes pictures and a report.
    ///
    /// Menu: BioDome > Layout > Verify Footprints. Command line: -executeMethod BioDome.EditorTools.SceneFootprintAnalyzer.RunBatch -footOut DIR [-footScene PATH].
    /// </summary>
    public static class SceneFootprintAnalyzer
    {
        public const float Cell = 0.1f;

        [Flags]
        public enum Kind : byte
        {
            None = 0,
            Walkway = 1,
            Bed = 2,
            Prop = 4,       // benches, stones, lamp posts
            Structure = 8,  // frame, entrance, plinth
            Water = 16,
            Plant = 32,
            Rail = 64       // irrigation rails and pipes
        }

        public sealed class Spec
        {
            public string Name;
            public string Kind; // "Pod" or "Collection"
            public float Width, Depth;
            public Vector2 Seed; // the zone is placed as close to this point as it fits (a corner it should cluster around)
        }

        public sealed class Placement
        {
            public string Spec;
            public string Kind;
            public string Region;
            public Vector2 Center;
            public Vector2 Size;
            public int Index;
        }

        public sealed class Result
        {
            public Rect Bounds;
            public int Nx, Nz;
            public Kind[] Grid;
            public bool[] Blocked;
            public List<Placement> Placements = new List<Placement>();
            public Dictionary<string, float> FreeAreaByRegion = new Dictionary<string, float>();
            public Dictionary<string, int> CellCounts = new Dictionary<string, int>();
            public int PlantCount;
            public int Requested;
        }

        // Walking clearance kept around every solid thing, and gap kept between new zones.
        public static float SolidBuffer = 0.7f;
        public static float WalkwayBuffer = 0.15f;
        public static float WallBuffer = 0.9f;
        public static float ZoneGap = 0.8f;

        [MenuItem("BioDome/Layout/Verify Footprints")]
        public static void RunFromMenu()
        {
            string folder = EditorUtility.SaveFolderPanel("Save footprint report", Directory.GetCurrentDirectory(), "FootprintReport");
            if (string.IsNullOrEmpty(folder)) return;
            Result result = Analyze(DefaultSpecs());
            WriteOutputs(result, folder);
            Debug.Log("BioDome: footprint report written to " + folder + "\n" + Summarise(result));
        }

        public static void RunBatch()
        {
            int exit = 0;
            try
            {
                string outDir = Arg("-footOut") ?? "FootprintReport";
                string scene = Arg("-footScene") ?? "Assets/Scenes/BioDome_Working.unity";
                Directory.CreateDirectory(outDir);
                EditorSceneManager.OpenScene(scene);

                if (Arg("-footSweep") != null)
                {
                    var configs = new (float w, float d, float gap, float buffer)[]
                    {
                        (3.0f, 2.6f, 0.8f, 0.7f), (3.0f, 2.6f, 0.6f, 0.7f), (2.8f, 2.4f, 0.6f, 0.7f), (2.6f, 2.4f, 0.6f, 0.7f), (3.0f, 2.6f, 0.6f, 0.6f), (2.8f, 2.4f, 0.6f, 0.6f)
                    };
                    foreach (var c in configs)
                    {
                        ZoneGap = c.gap; SolidBuffer = c.buffer;
                        Result sweep = Analyze(DefaultSpecs(c.w, c.d));
                        int podsPlaza = 0, podsFoyer = 0, collPlaza = 0, collFoyer = 0;
                        foreach (Placement pl in sweep.Placements)
                        {
                            bool plaza = pl.Region.StartsWith("plaza");
                            if (pl.Kind == "Pod") { if (plaza) podsPlaza++; else podsFoyer++; }
                            else { if (plaza) collPlaza++; else collFoyer++; }
                        }
                        Debug.Log($"SWEEP pod {c.w}x{c.d} gap {c.gap} buffer {c.buffer}: placed {sweep.Placements.Count}/16 | pods plaza {podsPlaza} foyer {podsFoyer} | collections plaza {collPlaza} foyer {collFoyer}");
                    }
                    EditorApplication.Exit(0);
                    return;
                }
                ZoneGap = float.TryParse(Arg("-footGap"), out float g) ? g : ZoneGap;
                SolidBuffer = float.TryParse(Arg("-footBuffer"), out float b) ? b : SolidBuffer;

                float podW = float.TryParse(Arg("-footPodW"), out float pw) ? pw : 3.0f, podD = float.TryParse(Arg("-footPodD"), out float pd) ? pd : 2.6f;
                Result result = Analyze(DefaultSpecs(podW, podD));
                WriteOutputs(result, outDir);
                Debug.Log("FOOTPRINT\n" + Summarise(result));
            }
            catch (Exception e)
            {
                Debug.LogError("FOOTPRINT FAILED: " + e);
                exit = 2;
            }
            EditorApplication.Exit(exit);
        }

        /// <summary>
        /// The zones we want to place: 5 climate pods and 11 species collections, each sized for its mature plants.
        /// Pods cluster at the two east corners, collections at the foyer corners (then wherever room is left near them).
        /// </summary>
        public static List<Spec> DefaultSpecs(float podWidth = 3.0f, float podDepth = 2.6f, bool podsAtPlazaOnly = false)
        {
            var ne = new Vector2(19.4f, 11.5f);
            var se = new Vector2(19.4f, -11.5f);
            var foyerN = new Vector2(-19.4f, 11.5f);
            var foyerS = new Vector2(-19.4f, -11.5f);
            var specs = new List<Spec>();

            // Climate pods (Monstera x5): 3.0 x 2.6 m holds four plants at a natural spacing.
            Vector2[] podSeeds = { ne, se, ne, se, ne };
            for (int i = 0; i < 5; i++)
                specs.Add(new Spec { Name = "Pod " + (i + 1), Kind = "Pod", Width = podWidth, Depth = podDepth, Seed = podSeeds[i] });

            // Species collections: size from each species' mature canopy.
            (string name, float w, float d)[] collections =
            {
                ("Banana", 3.0f, 2.6f), ("Areca Palm", 2.6f, 2.2f), ("Bird of Paradise", 2.6f, 2.2f), ("Monstera", 2.6f, 2.0f),
                ("Philodendron", 2.2f, 1.8f), ("Boston Fern", 2.0f, 1.6f), ("Calathea", 1.8f, 1.4f), ("Anthurium", 1.6f, 1.4f),
                ("Orchid", 1.4f, 1.2f), ("Moss", 1.4f, 1.2f), ("Ground cover", 1.4f, 1.2f)
            };
            for (int i = 0; i < collections.Length; i++)
                specs.Add(new Spec { Name = collections[i].name, Kind = "Collection", Width = collections[i].w, Depth = collections[i].d, Seed = i % 2 == 0 ? foyerN : foyerS });
            return specs;
        }

        // --- Analysis --------------------------------------------------------------------------------

        public static Result Analyze(List<Spec> specs)
        {
            var settings = AssetDatabase.LoadAssetAtPath<GreenhouseSettings>("Assets/ScriptableObjects/Settings/GreenhouseSettings.asset");
            float halfL = (settings != null ? settings.length : 40f) * 0.5f;
            float halfW = (settings != null ? settings.width : 25f) * 0.5f;

            var r = new Result
            {
                Bounds = new Rect(-halfL, -halfW, halfL * 2f, halfW * 2f),
                Nx = Mathf.RoundToInt(halfL * 2f / Cell),
                Nz = Mathf.RoundToInt(halfW * 2f / Cell)
            };
            r.Grid = new Kind[r.Nx * r.Nz];

            foreach (MeshFilter filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                if (filter.GetComponentInParent<PlantGenerator>() != null) continue; // plants are handled separately

                Kind kind;
                float maxHeight;
                switch (filter.gameObject.name)
                {
                    case "Pathways": kind = Kind.Walkway; maxHeight = 0.5f; break;
                    case "PlantingBeds": kind = Kind.Bed; maxHeight = 1.5f; break;
                    case "Benches":
                    case "Stones":
                    case "Fixtures": kind = Kind.Prop; maxHeight = 2.6f; break;
                    case "WaterFeature":
                    case "Water": kind = Kind.Water; maxHeight = 2.6f; break;
                    case "Irrigation": kind = Kind.Rail; maxHeight = 2.6f; break;
                    case "Frame":
                    case "Entrance":
                    case "Plinth": kind = Kind.Structure; maxHeight = 2.6f; break;
                    default: continue; // glass, ground, anything else is not a footprint
                }
                Rasterise(r, filter.sharedMesh, filter.transform.localToWorldMatrix, kind, maxHeight);
            }

            foreach (PlantGenerator plant in UnityEngine.Object.FindObjectsByType<PlantGenerator>())
            {
                var controller = plant.GetComponent<PlantController>();
                float radius = controller != null && controller.Data != null ? controller.Data.MatureCanopyWidth * 0.35f : 0.4f;
                MarkDisc(r, plant.transform.position.x, plant.transform.position.z, radius, Kind.Plant);
                r.PlantCount++;
            }

            BuildBlockedMask(r);
            r.Requested = specs.Count;
            Pack(r, specs);
            CountCells(r);
            return r;
        }

        private static void Rasterise(Result r, Mesh mesh, Matrix4x4 matrix, Kind kind, float maxHeight)
        {
            Vector3[] vertices = mesh.vertices;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(vertices[triangles[i]]);
                    Vector3 b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    Vector3 c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                    if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > maxHeight) continue; // overhead, not in the way
                    FillTriangle(r, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), kind);
                }
            }
        }

        private static void FillTriangle(Result r, Vector2 a, Vector2 b, Vector2 c, Kind kind)
        {
            float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minZ = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), maxZ = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - r.Bounds.xMin) / Cell)), x1 = Mathf.Min(r.Nx - 1, Mathf.FloorToInt((maxX - r.Bounds.xMin) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - r.Bounds.yMin) / Cell)), z1 = Mathf.Min(r.Nz - 1, Mathf.FloorToInt((maxZ - r.Bounds.yMin) / Cell));

            float denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(r.Bounds.xMin + (x + 0.5f) * Cell, r.Bounds.yMin + (z + 0.5f) * Cell);
                    bool inside;
                    if (Mathf.Abs(denominator) < 1e-9f)
                    {
                        inside = true; // a sliver: mark its cells so thin rails are not lost
                    }
                    else
                    {
                        float w1 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / denominator;
                        float w2 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / denominator;
                        float w3 = 1f - w1 - w2;
                        const float e = -0.05f; // a little generous so edges and thin rails are caught
                        inside = w1 >= e && w2 >= e && w3 >= e;
                    }
                    if (inside) r.Grid[z * r.Nx + x] |= kind;
                }
            }
        }

        private static void MarkDisc(Result r, float cx, float cz, float radius, Kind kind)
        {
            int rc = Mathf.CeilToInt(radius / Cell);
            int ix = Mathf.FloorToInt((cx - r.Bounds.xMin) / Cell), iz = Mathf.FloorToInt((cz - r.Bounds.yMin) / Cell);
            for (int dz = -rc; dz <= rc; dz++)
            {
                for (int dx = -rc; dx <= rc; dx++)
                {
                    if (dx * dx + dz * dz > rc * rc) continue;
                    int x = ix + dx, z = iz + dz;
                    if (x < 0 || z < 0 || x >= r.Nx || z >= r.Nz) continue;
                    r.Grid[z * r.Nx + x] |= kind;
                }
            }
        }

        /// <summary>Cells where a new zone must not be: solid things plus a walking buffer, walkways plus a hair of margin, and the outer walls.</summary>
        private static void BuildBlockedMask(Result r)
        {
            r.Blocked = new bool[r.Nx * r.Nz];
            const Kind solid = Kind.Bed | Kind.Prop | Kind.Structure | Kind.Water | Kind.Plant | Kind.Rail;
            int solidR = Mathf.CeilToInt(SolidBuffer / Cell);
            int walkR = Mathf.CeilToInt(WalkwayBuffer / Cell);
            int wallR = Mathf.CeilToInt(WallBuffer / Cell);

            for (int z = 0; z < r.Nz; z++)
            {
                for (int x = 0; x < r.Nx; x++)
                {
                    Kind k = r.Grid[z * r.Nx + x];
                    if (x < wallR || z < wallR || x >= r.Nx - wallR || z >= r.Nz - wallR) r.Blocked[z * r.Nx + x] = true;
                    if ((k & solid) != 0) Dilate(r, x, z, solidR);
                    else if ((k & Kind.Walkway) != 0) Dilate(r, x, z, walkR);
                }
            }
        }

        private static void Dilate(Result r, int x, int z, int radius)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                int zz = z + dz;
                if (zz < 0 || zz >= r.Nz) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int xx = x + dx;
                    if (xx < 0 || xx >= r.Nx) continue;
                    r.Blocked[zz * r.Nx + xx] = true;
                }
            }
        }

        private static string RegionOf(Result r, Vector2 center)
        {
            if (center.x < -15f) return "foyer";
            if (center.x > 9f) return center.y >= 0f ? "plaza-north" : "plaza-south";
            return "middle";
        }

        /// <summary>Places each zone as close to its seed corner as it fits, keeping a gap between zones. Larger zones are given in priority order.</summary>
        private static void Pack(Result r, List<Spec> specs)
        {
            bool[] blocked = (bool[])r.Blocked.Clone();
            int gap = Mathf.CeilToInt(ZoneGap / Cell);
            int index = 0;

            foreach (Spec spec in specs)
            {
                bool done = false;
                for (int rot = 0; rot < 2 && !done; rot++) { } // (orientation is tried below, per position)

                float bestDistance = float.MaxValue;
                int bestX = -1, bestZ = -1;
                bool bestRotated = false;

                for (int z = 0; z < r.Nz; z += 2)
                {
                    for (int x = 0; x < r.Nx; x += 2)
                    {
                        for (int rot = 0; rot < 2; rot++)
                        {
                            float w = rot == 1 ? spec.Depth : spec.Width, d = rot == 1 ? spec.Width : spec.Depth;
                            int wc = Mathf.RoundToInt(w / Cell), dc = Mathf.RoundToInt(d / Cell);
                            if (x + wc > r.Nx || z + dc > r.Nz) continue;

                            var center = new Vector2(r.Bounds.xMin + (x + wc * 0.5f) * Cell, r.Bounds.yMin + (z + dc * 0.5f) * Cell);
                            float distance = (center - spec.Seed).sqrMagnitude;
                            if (distance >= bestDistance) continue;
                            if (!RectFree(blocked, r.Nx, x, z, wc, dc)) continue;

                            bestDistance = distance; bestX = x; bestZ = z; bestRotated = rot == 1;
                        }
                    }
                }

                if (bestX < 0) continue; // does not fit anywhere

                float pw = bestRotated ? spec.Depth : spec.Width, pd = bestRotated ? spec.Width : spec.Depth;
                int pwc = Mathf.RoundToInt(pw / Cell), pdc = Mathf.RoundToInt(pd / Cell);
                var placedCenter = new Vector2(r.Bounds.xMin + (bestX + pwc * 0.5f) * Cell, r.Bounds.yMin + (bestZ + pdc * 0.5f) * Cell);
                r.Placements.Add(new Placement
                {
                    Spec = spec.Name,
                    Kind = spec.Kind,
                    Region = RegionOf(r, placedCenter),
                    Center = placedCenter,
                    Size = new Vector2(pw, pd),
                    Index = ++index
                });

                for (int zz = Mathf.Max(0, bestZ - gap); zz < Mathf.Min(r.Nz, bestZ + pdc + gap); zz++)
                    for (int xx = Mathf.Max(0, bestX - gap); xx < Mathf.Min(r.Nx, bestX + pwc + gap); xx++)
                        blocked[zz * r.Nx + xx] = true;
            }
        }

        private static bool RectFree(bool[] blocked, int nx, int x0, int z0, int w, int d)
        {
            for (int z = z0; z < z0 + d; z++)
                for (int x = x0; x < x0 + w; x++)
                    if (blocked[z * nx + x]) return false;
            return true;
        }

        private static void CountCells(Result r)
        {
            var counts = new Dictionary<string, int>();
            void Add(string key) { counts.TryGetValue(key, out int n); counts[key] = n + 1; }
            float free = 0f;
            for (int z = 0; z < r.Nz; z++)
            {
                for (int x = 0; x < r.Nx; x++)
                {
                    Kind k = r.Grid[z * r.Nx + x];
                    foreach (Kind flag in new[] { Kind.Walkway, Kind.Bed, Kind.Prop, Kind.Structure, Kind.Water, Kind.Plant, Kind.Rail })
                        if ((k & flag) != 0) Add(flag.ToString());
                    if (!r.Blocked[z * r.Nx + x])
                    {
                        free += Cell * Cell;
                        var center = new Vector2(r.Bounds.xMin + (x + 0.5f) * Cell, r.Bounds.yMin + (z + 0.5f) * Cell);
                        string region = RegionOf(r, center);
                        r.FreeAreaByRegion.TryGetValue(region, out float area);
                        r.FreeAreaByRegion[region] = area + Cell * Cell;
                    }
                }
            }
            r.CellCounts = counts;
            r.FreeAreaByRegion["total"] = free;
        }

        // --- Output --------------------------------------------------------------------------------

        public static string Summarise(Result r)
        {
            var text = new StringBuilder();
            text.AppendLine($"Grid {r.Nx} x {r.Nz} cells of {Cell} m over x {r.Bounds.xMin:0.0}..{r.Bounds.xMax:0.0}, z {r.Bounds.yMin:0.0}..{r.Bounds.yMax:0.0}. {r.PlantCount} plants read.");
            foreach (var pair in r.CellCounts) text.AppendLine($"  {pair.Key}: {pair.Value * Cell * Cell:0.0} m2");
            text.AppendLine("Free floor (after walking buffers):");
            foreach (var pair in r.FreeAreaByRegion) text.AppendLine($"  {pair.Key}: {pair.Value:0.0} m2");

            var perKind = new Dictionary<string, int>();
            foreach (Placement p in r.Placements)
            {
                perKind.TryGetValue(p.Kind + "/" + p.Region, out int n);
                perKind[p.Kind + "/" + p.Region] = n + 1;
                text.AppendLine($"  #{p.Index} {p.Spec} [{p.Kind}] in {p.Region}: centre ({p.Center.x:0.00}, {p.Center.y:0.00}) size {p.Size.x:0.0} x {p.Size.y:0.0} m");
            }
            foreach (var pair in perKind) text.AppendLine($"  total {pair.Key}: {pair.Value}");
            text.AppendLine($"  zones placed: {r.Placements.Count} of {r.Requested}");
            return text.ToString();
        }

        private static void WriteOutputs(Result r, string folder)
        {
            File.WriteAllText(Path.Combine(folder, "footprint_report.txt"), Summarise(r));
            File.WriteAllBytes(Path.Combine(folder, "footprint_map.png"), DrawMap(r).EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, "topdown_photo.png"), RenderTopDown(r, false).EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, "topdown_photo_with_zones.png"), RenderTopDown(r, true).EncodeToPNG());
        }

        private const int PixelsPerMetre = 40;

        private static Vector2Int ToPixel(Result r, float x, float z)
        {
            return new Vector2Int(Mathf.RoundToInt((x - r.Bounds.xMin) * PixelsPerMetre), Mathf.RoundToInt((z - r.Bounds.yMin) * PixelsPerMetre));
        }

        private static Texture2D DrawMap(Result r)
        {
            int width = Mathf.RoundToInt(r.Bounds.width * PixelsPerMetre), height = Mathf.RoundToInt(r.Bounds.height * PixelsPerMetre);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color32[width * height];

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    int cx = Mathf.Min(r.Nx - 1, Mathf.FloorToInt(px / (float)PixelsPerMetre / Cell));
                    int cz = Mathf.Min(r.Nz - 1, Mathf.FloorToInt(py / (float)PixelsPerMetre / Cell));
                    Kind k = r.Grid[cz * r.Nx + cx];
                    Color c = new Color(0.86f, 0.86f, 0.84f); // open floor
                    if (!r.Blocked[cz * r.Nx + cx]) c = new Color(0.78f, 0.93f, 0.78f); // free for a new zone (after buffers)
                    if ((k & Kind.Walkway) != 0) c = new Color(0.93f, 0.85f, 0.64f);
                    if ((k & Kind.Bed) != 0) c = new Color(0.55f, 0.38f, 0.24f);
                    if ((k & Kind.Water) != 0) c = new Color(0.35f, 0.6f, 0.9f);
                    if ((k & Kind.Plant) != 0) c = new Color(0.2f, 0.55f, 0.25f);
                    if ((k & (Kind.Prop | Kind.Rail)) != 0) c = new Color(0.8f, 0.2f, 0.2f);
                    if ((k & Kind.Structure) != 0) c = new Color(0.25f, 0.25f, 0.3f);
                    pixels[py * width + px] = c;
                }
            }
            tex.SetPixels32(pixels);
            DrawPlacements(r, tex);
            tex.Apply();
            return tex;
        }

        private static void DrawPlacements(Result r, Texture2D tex)
        {
            foreach (Placement p in r.Placements)
            {
                Color colour = p.Kind == "Pod" ? new Color(0.85f, 0.1f, 0.75f) : new Color(0.1f, 0.35f, 0.95f);
                Vector2Int min = ToPixel(r, p.Center.x - p.Size.x * 0.5f, p.Center.y - p.Size.y * 0.5f);
                Vector2Int max = ToPixel(r, p.Center.x + p.Size.x * 0.5f, p.Center.y + p.Size.y * 0.5f);
                for (int x = min.x; x <= max.x; x++) for (int t = 0; t < 3; t++) { Set(tex, x, min.y + t, colour); Set(tex, x, max.y - t, colour); }
                for (int y = min.y; y <= max.y; y++) for (int t = 0; t < 3; t++) { Set(tex, min.x + t, y, colour); Set(tex, max.x - t, y, colour); }
                Vector2Int mid = ToPixel(r, p.Center.x, p.Center.y);
                DrawNumber(tex, p.Index, mid.x, mid.y, 4, colour);
            }
        }

        private static void Set(Texture2D tex, int x, int y, Color c)
        {
            if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) tex.SetPixel(x, y, c);
        }

        // A tiny 3x5 digit font so the pictures carry numbers without any font asset.
        private static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111"
        };

        private static void DrawNumber(Texture2D tex, int number, int cx, int cy, int scale, Color colour)
        {
            string s = number.ToString();
            int total = s.Length * 4 * scale - scale;
            int x0 = cx - total / 2, y0 = cy - 5 * scale / 2;
            for (int i = 0; i < s.Length; i++)
            {
                string glyph = Digits[s[i] - '0'];
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (glyph[gy * 3 + gx] == '1')
                            for (int sy = 0; sy < scale; sy++)
                                for (int sx = 0; sx < scale; sx++)
                                    Set(tex, x0 + (i * 4 + gx) * scale + sx, y0 + (4 - gy) * scale + sy, colour);
            }
        }

        /// <summary>A real top-down photograph with the roof cut away (orthographic camera whose near plane starts below the roof).</summary>
        private static Texture2D RenderTopDown(Result r, bool withZones)
        {
            int width = Mathf.RoundToInt(r.Bounds.width * PixelsPerMetre), height = Mathf.RoundToInt(r.Bounds.height * PixelsPerMetre);
            var go = new GameObject("TopDownCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = r.Bounds.height * 0.5f;
            camera.aspect = r.Bounds.width / r.Bounds.height;
            camera.transform.position = new Vector3(r.Bounds.center.x, 11f, r.Bounds.center.y);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = 5.2f; // hides everything above about 5.8 m, which removes the roof
            camera.farClipPlane = 13f;

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(go);

            if (withZones) DrawPlacements(r, tex);
            tex.Apply();
            return tex;
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
