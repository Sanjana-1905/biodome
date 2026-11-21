using System.Collections.Generic;
using System.IO;
using BioDome.CameraSystem;
using BioDome.Environment;
using BioDome.Procedural;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Builds the BioDome greenhouse procedurally inside Unity and saves it as Assets/Scenes/BioDome_Main.unity.
    /// Menu: BioDome > Build Greenhouse Scene. Dimensions and materials come from a GreenhouseSettings asset.
    /// Geometry is generated with MeshData / MeshShapes and merged into a few meshes (one per material group),
    /// stored as assets in Assets/Art/Procedural so the scene file stays small.
    /// Layout: X = length (entrance at -X), Z = width, Y = up, origin at the centre of the floor.
    /// </summary>
    public static class GreenhouseSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/BioDome_Main.unity";
        private const string RootName = "BioDome_Generated";
        private const string SettingsPath = GreenhouseAssetFactory.SettingsFolder + "/GreenhouseSettings.asset";

        private const float FloorOffset = 0.03f; // paving sits slightly above the floor

        // --- Menu -----------------------------------------------------------------

        [MenuItem("BioDome/Build Greenhouse Scene")]
        public static void BuildGreenhouseScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Opening or creating a scene in Single mode unloads unused assets. A ScriptableObject that was only
            // held by a C# variable is destroyed by that cleanup, so the settings asset must be loaded AFTER the
            // scene change, never before it.
            Scene scene = OpenOrCreateScene();

            GameObject existing = GameObject.Find(RootName);
            if (existing != null && !EditorUtility.DisplayDialog(
                    "Rebuild greenhouse",
                    $"The scene already contains '{RootName}'. Rebuilding deletes it and regenerates it from the settings.\n\n" +
                    "Objects you added elsewhere in the scene (plants, cameras, lights) are kept. Changes made inside the generated hierarchy are lost.",
                    "Rebuild", "Cancel"))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("BioDome", "Building greenhouse...", 0.3f);
                GreenhouseSettings settings = LoadOrCreateSettings(); // fresh, valid reference for the whole build
                Build(settings, scene);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("BioDome/Select Greenhouse Settings")]
        public static void SelectSettings()
        {
            GreenhouseSettings settings = LoadOrCreateSettings();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("BioDome/Reset Greenhouse Materials To Defaults")]
        public static void ResetMaterials()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reset materials",
                    "This restores the default greenhouse materials and textures and assigns them in the settings asset. " +
                    "Edits you made to those default material assets are overwritten.",
                    "Reset", "Cancel"))
            {
                return;
            }

            GreenhouseSettings settings = LoadOrCreateSettings();
            GreenhouseAssetFactory.ResolveMaterials(settings, true);
            GreenhouseAssetFactory.Sky(true);
            GreenhouseAssetFactory.WaterDroplet(true);
            AssetDatabase.SaveAssets();
            Debug.Log("BioDome: greenhouse materials reset to defaults.");
        }

        // --- Layout ---------------------------------------------------------------

        /// <summary>All derived dimensions in one place, computed from the settings.</summary>
        private sealed class Layout
        {
            public readonly float L, W, H, WallH, Plinth, HalfL, HalfW, Rise;
            public readonly int Bays, RoofRows, Cols, BraceEvery;
            public readonly float BayLen, ColW, DoorHalf, Y1, PathW, PlazaR, PlazaX, XStart, XEnd;
            public readonly int Rows, BedsPerRow;
            public readonly float RowW, ZStart, ZEnd, BedLen, BedGap, Aisle, BedH, BorderW, ZMain;
            public readonly float[] FrameX;

            public Layout(GreenhouseSettings s)
            {
                L = Mathf.Max(12f, s.length);
                W = Mathf.Max(10f, s.width);
                WallH = Mathf.Max(3f, s.wallHeight);
                H = Mathf.Max(s.height, WallH + 1.5f);
                Plinth = Mathf.Clamp(s.plinthHeight, 0.1f, 1.5f);
                HalfL = L * 0.5f;
                HalfW = W * 0.5f;
                Rise = H - WallH;

                Bays = Mathf.Max(2, Mathf.RoundToInt(L / Mathf.Max(1.5f, s.bayLength)));
                BayLen = L / Bays;
                FrameX = new float[Bays + 1];
                for (int i = 0; i <= Bays; i++) FrameX[i] = -HalfL + i * BayLen;

                RoofRows = Mathf.Clamp(s.roofRows, 2, 8);
                BraceEvery = Mathf.Max(1, s.braceEveryNBays);

                Cols = 2 * Mathf.Max(3, Mathf.RoundToInt(W / (2f * Mathf.Max(1f, s.panelWidth))));
                ColW = W / Cols;
                DoorHalf = ColW; // the entrance is the two central end-wall columns
                Y1 = Mathf.Clamp(s.doorHeight, 2.5f, WallH - 1f);

                PathW = Mathf.Min(s.pathWidth, 2f * DoorHalf - 0.4f);
                PlazaR = Mathf.Clamp(s.plazaRadius, 3.2f, Mathf.Max(3.2f, HalfW - 3f));
                PlazaX = HalfL - PlazaR - 2f;
                XStart = -HalfL + s.entranceMargin;
                XEnd = PlazaX - PlazaR - 0.6f;

                BedGap = s.bedGap;
                Aisle = s.aisleWidth;
                BedH = s.bedHeight;
                BorderW = s.borderPlanterWidth;
                ZStart = PathW * 0.5f + Aisle;
                ZEnd = HalfW - 0.35f - BorderW - 1f;
                ZMain = ZStart - 0.3f;

                int rows = s.bedRowsPerSide;
                float rowW = 0f;
                while (rows > 0)
                {
                    rowW = (ZEnd - ZStart - (rows - 1) * Aisle) / rows;
                    if (rowW >= 1.2f) break;
                    rows--;
                }
                Rows = rows;
                RowW = rows > 0 ? Mathf.Min(rowW, 4f) : 0f;

                int n = s.bedsPerRow;
                float bedLen = 0f;
                while (n > 0)
                {
                    bedLen = (XEnd - XStart - (n - 1) * BedGap) / n;
                    if (bedLen >= 2f) break;
                    n--;
                }
                BedsPerRow = n;
                BedLen = n > 0 ? bedLen : 0f;
            }

            public bool HasBeds => Rows > 0 && BedsPerRow > 0;

            public float RoofY(float z)
            {
                return WallH + Rise * (1f - Mathf.Min(1f, Mathf.Abs(z) / HalfW));
            }

            /// <summary>Point on a roof slope: side = -1 or 1, t = 0 at the eave to 1 at the ridge.</summary>
            public Vector3 RoofPoint(int side, float t, float x)
            {
                return new Vector3(x, WallH + Rise * t, side * HalfW * (1f - t));
            }

            public float BedX0(int column)
            {
                return XStart + column * (BedLen + BedGap);
            }

            public float RowCenterZ(int row)
            {
                return ZStart + RowW * 0.5f + row * (RowW + Aisle);
            }
        }

        private struct ZoneInfo
        {
            public string Name;
            public Vector3 Center;
            public Vector2 Size;
        }

        // --- Build ----------------------------------------------------------------

        private static void Build(GreenhouseSettings settings, Scene scene)
        {
            GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.MeshFolder);
            GreenhouseAssetFactory.ResolveMaterials(settings, false);
            var l = new Layout(settings);

            // Remove the previous generated hierarchy (and only that) so rebuilds never duplicate objects.
            GameObject old;
            while ((old = GameObject.Find(RootName)) != null) Object.DestroyImmediate(old);

            var root = new GameObject(RootName);
            Transform structure = Child(root.transform, "Structure");
            Transform interior = Child(root.transform, "Interior");
            Transform props = Child(root.transform, "Props");

            // Structure
            MeshData glassData = BuildGlass(l);
            Make("Frame", structure, "Greenhouse_Frame", BuildFrame(l), new[] { settings.steel }, collider: true);
            Make("Plinth", structure, "Greenhouse_Plinth", BuildPlinth(l), new[] { settings.stone }, collider: true);
            Make("Entrance", structure, "Greenhouse_Entrance", BuildEntrance(l), new[] { settings.steel, settings.stone }, collider: true);
            GameObject glass = Make("Glass", structure, "Greenhouse_Glass", glassData, new[] { settings.glass }, collider: false);
            DisableShadows(glass);

            // Interior
            GameObject ground = Make("Ground", interior, "Greenhouse_Ground", BuildGround(l), new[] { settings.ground, settings.gravel }, collider: false);
            var groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.center = new Vector3(0f, -0.5f, 0f);
            groundCollider.size = new Vector3(l.L + 80f, 1f, l.W + 80f);

            Make("Pathways", interior, "Greenhouse_Pathways", BuildPaths(l), new[] { settings.pavers, settings.stone }, collider: false);

            var zones = new List<ZoneInfo>();
            Make("PlantingBeds", interior, "Greenhouse_Beds", BuildBeds(l, zones), new[] { settings.timber, settings.soil, settings.stone }, collider: true);
            BuildZoneObjects(interior, zones);

            // Props
            Make("Irrigation", props, "Greenhouse_Irrigation", BuildIrrigation(l), new[] { settings.pipe }, collider: false);

            MeshData water;
            GameObject feature = Make("WaterFeature", props, "Greenhouse_WaterFeature", BuildWaterFeature(l, out water), new[] { settings.stone }, collider: true);
            GameObject waterSurface = Make("Water", feature.transform, "Greenhouse_Water", water, new[] { settings.water }, collider: false);
            DisableShadows(waterSurface);
            BuildFountainSpray(feature.transform, l);

            Make("Benches", props, "Greenhouse_Benches", BuildBenches(l, settings), new[] { settings.timber, settings.steel }, collider: true);
            Make("Stones", props, "Greenhouse_Stones", BuildStones(l, settings), new[] { settings.stone }, collider: true);
            BuildLighting(props, l, settings);

            SetupSceneEnvironment(l);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log($"BioDome: greenhouse built ({l.L:0.#} x {l.W:0.#} x {l.H:0.#}, {l.Bays} bays) and saved to {ScenePath}.");
        }

        // --- Structural frame (steel) ---------------------------------------------

        private static MeshData BuildFrame(Layout l)
        {
            var d = new MeshData();
            float tieY = l.WallH - 0.15f;

            for (int i = 0; i <= l.Bays; i++)
            {
                float x = l.FrameX[i];

                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * l.HalfW;
                    // Pillar with base plate.
                    MeshShapes.AddBox(d, new Vector3(x, l.WallH * 0.5f + 0.04f, z), new Vector3(0.3f, l.WallH - 0.08f, 0.36f), Quaternion.identity);
                    MeshShapes.AddBox(d, new Vector3(x, 0.04f, z), new Vector3(0.6f, 0.08f, 0.6f), Quaternion.identity);

                    // Rafter from eave to ridge.
                    Beam(d, new Vector3(x, l.WallH, z), new Vector3(x, l.H, 0f), 0.18f, 0.3f);
                    // Knee brace from pillar to tie beam.
                    Beam(d, new Vector3(x, l.WallH - 2.2f, z), new Vector3(x, tieY, s * (l.HalfW - 2.2f)), 0.14f, 0.14f);

                    // Truss web: post and diagonal to the rafter at quarter span.
                    float zm = s * l.HalfW * 0.5f;
                    float ym = l.RoofY(zm) - 0.08f;
                    Beam(d, new Vector3(x, tieY, zm), new Vector3(x, ym, zm), 0.12f, 0.12f);
                    Beam(d, new Vector3(x, tieY, 0f), new Vector3(x, ym, zm), 0.12f, 0.12f);
                }

                Beam(d, new Vector3(x, tieY, -l.HalfW), new Vector3(x, tieY, l.HalfW), 0.18f, 0.3f); // tie beam
                Beam(d, new Vector3(x, tieY, 0f), new Vector3(x, l.H - 0.1f, 0f), 0.16f, 0.16f);     // king post
            }

            // Longitudinal members.
            float x0 = -l.HalfL - 0.15f;
            float x1 = l.HalfL + 0.15f;
            Beam(d, new Vector3(x0, l.H, 0f), new Vector3(x1, l.H, 0f), 0.3f, 0.34f); // ridge beam

            for (int s = -1; s <= 1; s += 2)
            {
                Beam(d, new Vector3(x0, l.WallH, s * l.HalfW), new Vector3(x1, l.WallH, s * l.HalfW), 0.28f, 0.3f); // eave beam
                Beam(d, new Vector3(x0, l.Y1, s * l.HalfW), new Vector3(x1, l.Y1, s * l.HalfW), 0.14f, 0.16f);      // wall transom

                for (int k = 1; k < l.RoofRows; k++) // purlins
                {
                    float t = k / (float)l.RoofRows;
                    Beam(d, l.RoofPoint(s, t, x0), l.RoofPoint(s, t, x1), 0.12f, 0.14f);
                }
            }

            // Glazing bars and mullions at the middle of every bay.
            for (int b = 0; b < l.Bays; b++)
            {
                float xm = l.FrameX[b] + l.BayLen * 0.5f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Beam(d, l.RoofPoint(s, 0f, xm), l.RoofPoint(s, 1f, xm), 0.06f, 0.08f);
                    float h = l.WallH - l.Plinth;
                    MeshShapes.AddBox(d, new Vector3(xm, l.Plinth + h * 0.5f, s * l.HalfW), new Vector3(0.07f, h, 0.07f), Quaternion.identity);
                }
            }

            // X-bracing in selected bays (walls and lower roof).
            for (int b = 0; b < l.Bays; b++)
            {
                if (b % l.BraceEvery != 0 && b != l.Bays - 1) continue;

                float xa = l.FrameX[b];
                float xb = l.FrameX[b + 1];
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * l.HalfW;
                    Beam(d, new Vector3(xa, l.Plinth, z), new Vector3(xb, l.Y1, z), 0.07f, 0.07f);
                    Beam(d, new Vector3(xa, l.Y1, z), new Vector3(xb, l.Plinth, z), 0.07f, 0.07f);

                    float tMid = 0.5f;
                    Beam(d, l.RoofPoint(s, 0f, xa), l.RoofPoint(s, tMid, xb), 0.07f, 0.07f);
                    Beam(d, l.RoofPoint(s, 0f, xb), l.RoofPoint(s, tMid, xa), 0.07f, 0.07f);
                }
            }

            // End walls: transom and mullions (the west end has the entrance).
            for (int e = -1; e <= 1; e += 2)
            {
                float x = e * l.HalfL;
                Beam(d, new Vector3(x, l.Y1, -l.HalfW), new Vector3(x, l.Y1, l.HalfW), 0.14f, 0.16f);

                for (int k = 1; k < l.Cols; k++)
                {
                    float z = -l.HalfW + k * l.ColW;
                    float top = l.RoofY(z) - 0.05f;
                    float bottom = l.Plinth;
                    float thickness = 0.07f;

                    if (e < 0)
                    {
                        float az = Mathf.Abs(z);
                        if (az < l.DoorHalf - 0.01f) bottom = l.Y1;                    // inside the doorway: above the header only
                        else if (Mathf.Abs(az - l.DoorHalf) < 0.01f) { bottom = 0f; thickness = 0.24f; } // door jamb
                    }

                    float h = top - bottom;
                    if (h <= 0.05f) continue;
                    MeshShapes.AddBox(d, new Vector3(x, bottom + h * 0.5f, z), new Vector3(thickness, h, thickness), Quaternion.identity);
                }
            }

            return d;
        }

        // --- Glass ----------------------------------------------------------------

        private static MeshData BuildGlass(Layout l)
        {
            var d = new MeshData();
            int halfBays = l.Bays * 2;
            float step = l.L / halfBays;

            // Long walls: 2 rows (plinth to transom, transom to eave).
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * l.HalfW;
                Vector3 outward = new Vector3(0f, 0f, s);
                for (int j = 0; j < halfBays; j++)
                {
                    float xa = -l.HalfL + j * step;
                    float xb = xa + step;
                    WallPanel(d, new Vector3(xa, l.Plinth, z), new Vector3(xb, l.Plinth, z), l.Y1 - l.Plinth, l.Y1 - l.Plinth, outward);
                    WallPanel(d, new Vector3(xa, l.Y1, z), new Vector3(xb, l.Y1, z), l.WallH - l.Y1, l.WallH - l.Y1, outward);
                }
            }

            // End walls: 3 rows, the top one following the gable.
            for (int e = -1; e <= 1; e += 2)
            {
                float x = e * l.HalfL;
                Vector3 outward = new Vector3(e, 0f, 0f);
                for (int k = 0; k < l.Cols; k++)
                {
                    float za = -l.HalfW + k * l.ColW;
                    float zb = za + l.ColW;
                    bool doorway = e < 0 && Mathf.Abs((za + zb) * 0.5f) < l.DoorHalf;

                    if (!doorway)
                        WallPanel(d, new Vector3(x, l.Plinth, za), new Vector3(x, l.Plinth, zb), l.Y1 - l.Plinth, l.Y1 - l.Plinth, outward);

                    WallPanel(d, new Vector3(x, l.Y1, za), new Vector3(x, l.Y1, zb), l.WallH - l.Y1, l.WallH - l.Y1, outward);
                    WallPanel(d, new Vector3(x, l.WallH, za), new Vector3(x, l.WallH, zb), l.RoofY(za) - l.WallH, l.RoofY(zb) - l.WallH, outward);
                }
            }

            // Roof: both slopes, half-bay columns by roof rows.
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 outward = new Vector3(0f, l.HalfW, s * l.Rise).normalized;
                for (int j = 0; j < halfBays; j++)
                {
                    float xa = -l.HalfL + j * step;
                    float xb = xa + step;
                    for (int k = 0; k < l.RoofRows; k++)
                    {
                        float t0 = k / (float)l.RoofRows;
                        float t1 = (k + 1) / (float)l.RoofRows;
                        MeshShapes.AddQuadFacing(d, l.RoofPoint(s, t0, xa), l.RoofPoint(s, t1, xa), l.RoofPoint(s, t1, xb), l.RoofPoint(s, t0, xb), outward);
                    }
                }
            }

            // Entrance porch roof.
            float zp = l.DoorHalf + 0.25f;
            float wallTop = l.Y1 + 0.9f;
            float frontY = l.Y1 + 0.6f;
            float px = -l.HalfL - 2.6f;
            MeshShapes.AddQuadFacing(d,
                new Vector3(-l.HalfL, wallTop, -zp), new Vector3(px, frontY, -zp),
                new Vector3(px, frontY, zp), new Vector3(-l.HalfL, wallTop, zp), Vector3.up);

            return d;
        }

        /// <summary>A vertical panel from bottom edge a-b upward by heightA (at a) and heightB (at b).</summary>
        private static void WallPanel(MeshData d, Vector3 a, Vector3 b, float heightA, float heightB, Vector3 outward)
        {
            if (heightA <= 0.01f && heightB <= 0.01f) return;
            MeshShapes.AddQuadFacing(d, a, a + Vector3.up * heightA, b + Vector3.up * heightB, b, outward);
        }

        // --- Plinth (stone) -------------------------------------------------------

        private static MeshData BuildPlinth(Layout l)
        {
            var d = new MeshData();
            const float t = 0.45f;
            float h = l.Plinth;

            for (int s = -1; s <= 1; s += 2)
                MeshShapes.AddBox(d, new Vector3(0f, h * 0.5f, s * l.HalfW), new Vector3(l.L + t, h, t), Quaternion.identity);

            // East end: solid. West end: split around the entrance.
            MeshShapes.AddBox(d, new Vector3(l.HalfL, h * 0.5f, 0f), new Vector3(t, h, l.W + t), Quaternion.identity);

            float segment = l.HalfW - l.DoorHalf + t * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                float zc = s * (l.DoorHalf + (l.HalfW - l.DoorHalf) * 0.5f + t * 0.25f);
                MeshShapes.AddBox(d, new Vector3(-l.HalfL, h * 0.5f, zc), new Vector3(t, h, segment), Quaternion.identity);
            }
            return d;
        }

        // --- Entrance (steel porch, stone threshold) ------------------------------

        private static MeshData BuildEntrance(Layout l)
        {
            var d = new MeshData();
            float zp = l.DoorHalf + 0.25f;
            float wallTop = l.Y1 + 0.9f;
            float frontY = l.Y1 + 0.6f;
            float px = -l.HalfL - 2.6f;

            for (int s = -1; s <= 1; s += 2)
            {
                MeshShapes.AddBox(d, new Vector3(px, frontY * 0.5f, s * zp), new Vector3(0.14f, frontY, 0.14f), Quaternion.identity, 0);
                MeshShapes.AddBeam(d, new Vector3(px, frontY, s * zp), new Vector3(-l.HalfL, wallTop, s * zp), 0.12f, 0.16f, 0);
            }
            MeshShapes.AddBeam(d, new Vector3(px, frontY, -zp), new Vector3(px, frontY, zp), 0.12f, 0.16f, 0);

            // Stone threshold across the doorway.
            MeshShapes.AddBox(d, new Vector3(-l.HalfL, 0.03f, 0f), new Vector3(0.6f, 0.06f, l.DoorHalf * 2f), Quaternion.identity, 1);
            return d;
        }

        // --- Ground ---------------------------------------------------------------

        private static MeshData BuildGround(Layout l)
        {
            var d = new MeshData();
            float ex = (l.L + 80f) * 0.5f;
            float ez = (l.W + 80f) * 0.5f;
            const float outerY = -0.03f;
            MeshShapes.AddQuadFacing(d,
                new Vector3(-ex, outerY, -ez), new Vector3(-ex, outerY, ez),
                new Vector3(ex, outerY, ez), new Vector3(ex, outerY, -ez), Vector3.up, 0);

            float fx = l.HalfL - 0.2f;
            float fz = l.HalfW - 0.2f;
            MeshShapes.AddQuadFacing(d,
                new Vector3(-fx, 0f, -fz), new Vector3(-fx, 0f, fz),
                new Vector3(fx, 0f, fz), new Vector3(fx, 0f, -fz), Vector3.up, 1);
            return d;
        }

        // --- Pathways (pavers, stone kerbs) ---------------------------------------

        private static MeshData BuildPaths(Layout l)
        {
            var d = new MeshData();
            float y = FloorOffset;
            float half = l.PathW * 0.5f;

            // Main axis: from outside the entrance to the plaza.
            const float approach = 10f;
            float plazaEdge = l.PlazaX - Mathf.Sqrt(Mathf.Max(0.01f, l.PlazaR * l.PlazaR - half * half));
            float xStart = -l.HalfL - approach;
            MeshShapes.AddQuadFacing(d,
                new Vector3(xStart, y, -half), new Vector3(xStart, y, half),
                new Vector3(plazaEdge + 0.05f, y, half), new Vector3(plazaEdge + 0.05f, y, -half), Vector3.up, 0);

            // Kerbs either side of the main path.
            const float kerb = 0.16f;
            for (int s = -1; s <= 1; s += 2)
            {
                float zc = s * (half + kerb * 0.5f);
                MeshShapes.AddBox(d, new Vector3((xStart + plazaEdge) * 0.5f, 0.05f, zc),
                    new Vector3(plazaEdge - xStart, 0.1f, kerb), Quaternion.identity, 1);
            }

            // Plaza disc and its kerb ring, open towards the path.
            MeshShapes.AddDisc(d, new Vector3(l.PlazaX, y, 0f), l.PlazaR, 64, 0);
            float gap = Mathf.Asin(Mathf.Clamp01((half + kerb) / l.PlazaR)) * Mathf.Rad2Deg;
            MeshShapes.AddRing(d, new Vector3(l.PlazaX, 0f, 0f), l.PlazaR, l.PlazaR + 0.2f, 0.12f, 64, 1, 180f + gap, 540f - gap);

            // Cross paths through the gaps between beds.
            if (l.HasBeds)
            {
                float zStart = half + kerb;
                float zEnd = l.RowCenterZ(l.Rows - 1) + l.RowW * 0.5f;
                for (int c = 0; c < l.BedsPerRow - 1; c++)
                {
                    float gx = l.BedX0(c) + l.BedLen + l.BedGap * 0.5f;
                    float hw = l.BedGap * 0.5f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        MeshShapes.AddQuadFacing(d,
                            new Vector3(gx - hw, y, s * zStart), new Vector3(gx - hw, y, s * zEnd),
                            new Vector3(gx + hw, y, s * zEnd), new Vector3(gx + hw, y, s * zStart), Vector3.up, 0);
                    }
                }
            }
            return d;
        }

        // --- Beds and planting zones ----------------------------------------------

        private static MeshData BuildBeds(Layout l, List<ZoneInfo> zones)
        {
            var d = new MeshData();

            if (l.HasBeds)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    for (int r = 0; r < l.Rows; r++)
                    {
                        float zc = s * l.RowCenterZ(r);
                        for (int c = 0; c < l.BedsPerRow; c++)
                        {
                            float xc = l.BedX0(c) + l.BedLen * 0.5f;
                            AddBed(d, xc, zc, l.BedLen, l.RowW, l.BedH, 0, zones, $"Bed_{(s > 0 ? "N" : "S")}{r + 1}_{c + 1}");
                        }
                    }
                }
            }

            // Border planters along both long walls.
            float xa = -l.HalfL + 3f;
            float xb = l.HalfL - 0.7f;
            float zBorder = l.HalfW - 0.35f - l.BorderW * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                AddBed(d, (xa + xb) * 0.5f, s * zBorder, xb - xa, l.BorderW, l.BedH * 0.9f, 2, zones, $"Border_{(s > 0 ? "N" : "S")}");
            }
            return d;
        }

        /// <summary>Raised bed: four wall boxes plus a soil block. borderSubmesh 0 = timber, 2 = stone.</summary>
        private static void AddBed(MeshData d, float cx, float cz, float lenX, float lenZ, float h, int borderSubmesh,
            List<ZoneInfo> zones, string zoneName)
        {
            const float t = 0.12f;
            float soilTop = h - 0.06f;

            for (int s = -1; s <= 1; s += 2)
            {
                MeshShapes.AddBox(d, new Vector3(cx, h * 0.5f, cz + s * (lenZ * 0.5f - t * 0.5f)), new Vector3(lenX, h, t), Quaternion.identity, borderSubmesh);
                MeshShapes.AddBox(d, new Vector3(cx + s * (lenX * 0.5f - t * 0.5f), h * 0.5f, cz), new Vector3(t, h, lenZ - 2f * t), Quaternion.identity, borderSubmesh);
            }
            MeshShapes.AddBox(d, new Vector3(cx, soilTop * 0.5f, cz), new Vector3(lenX - 2f * t + 0.02f, soilTop, lenZ - 2f * t + 0.02f), Quaternion.identity, 1);

            zones.Add(new ZoneInfo
            {
                Name = zoneName,
                Center = new Vector3(cx, soilTop, cz),
                Size = new Vector2(lenX - 2f * t, lenZ - 2f * t)
            });
        }

        private static void BuildZoneObjects(Transform interior, List<ZoneInfo> zones)
        {
            Transform parent = Child(interior, "PlantingZones");
            for (int i = 0; i < zones.Count; i++)
            {
                var go = new GameObject($"PlantingZone_{zones[i].Name}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = zones[i].Center;
                go.AddComponent<PlantingZone>().Configure(zones[i].Name, zones[i].Size);
                GameObjectUtility.SetStaticEditorFlags(go, 0);
            }
        }

        // --- Irrigation -----------------------------------------------------------

        private static MeshData BuildIrrigation(Layout l)
        {
            var d = new MeshData();
            if (!l.HasBeds) return d;

            const float groundY = 0.08f;
            float barY = l.BedH + 0.6f;
            var rng = new System.Random(1);

            for (int s = -1; s <= 1; s += 2)
            {
                // Main supply line beside the path.
                MeshShapes.AddTube(d, new Vector3(l.XStart - 0.22f, groundY, s * l.ZMain), new Vector3(l.XEnd, groundY, s * l.ZMain), 0.05f, 10);

                float zFar = s * l.RowCenterZ(l.Rows - 1);
                for (int c = 0; c < l.BedsPerRow; c++)
                {
                    float xf = l.BedX0(c) - 0.22f;
                    float xEnd = l.BedX0(c) + l.BedLen - 0.15f;

                    // Feed pipe along Z to the farthest row, junction ball at the main.
                    MeshShapes.AddTube(d, new Vector3(xf, groundY, s * l.ZMain), new Vector3(xf, groundY, zFar), 0.035f, 8);
                    MeshShapes.AddSphere(d, new Vector3(xf, groundY, s * l.ZMain), 0.065f, Vector3.one, 5, 8, rng);

                    for (int r = 0; r < l.Rows; r++)
                    {
                        float zc = s * l.RowCenterZ(r);

                        // Riser and spray bar above the bed centre line.
                        MeshShapes.AddTube(d, new Vector3(xf, groundY, zc), new Vector3(xf, barY, zc), 0.03f, 8);
                        MeshShapes.AddSphere(d, new Vector3(xf, barY, zc), 0.05f, Vector3.one, 5, 8, rng);
                        MeshShapes.AddTube(d, new Vector3(xf, barY, zc), new Vector3(xEnd, barY, zc), 0.03f, 8);

                        // Nozzles hanging from the bar about once a metre.
                        for (float x = xf + 0.6f; x < xEnd - 0.2f; x += 1f)
                        {
                            MeshShapes.AddTube(d, new Vector3(x, barY, zc), new Vector3(x, barY - 0.22f, zc), 0.014f, 6);
                        }
                    }
                }
            }
            return d;
        }

        // --- Water feature --------------------------------------------------------

        private const float RimOuter = 2.1f;
        private const float RimInner = 1.7f;

        private static MeshData BuildWaterFeature(Layout l, out MeshData water)
        {
            var d = new MeshData();
            water = new MeshData();
            float cx = l.PlazaX;

            MeshShapes.AddRing(d, new Vector3(cx, 0f, 0f), RimInner, RimOuter, 0.6f, 56);
            MeshShapes.AddDisc(d, new Vector3(cx, 0.04f, 0f), RimInner + 0.02f, 48);

            MeshPrimitives.AddCylinder(d, new Vector3(cx, 0.04f, 0f), 0.42f, 0.28f, 0.9f, 20, 1, false, true);
            MeshPrimitives.AddCylinder(d, new Vector3(cx, 0.94f, 0f), 0.25f, 0.75f, 0.3f, 28, 1, true, true);
            MeshPrimitives.AddCylinder(d, new Vector3(cx, 1.24f, 0f), 0.08f, 0.04f, 0.45f, 12, 1, false, true);

            MeshShapes.AddDisc(water, new Vector3(cx, 0.42f, 0f), RimInner + 0.01f, 48);
            MeshShapes.AddDisc(water, new Vector3(cx, 1.245f, 0f), 0.68f, 32);
            return d;
        }

        private static void BuildFountainSpray(Transform parent, Layout l)
        {
            var go = new GameObject("FountainSpray");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(l.PlazaX, 1.7f, 0f);
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along local +Z; point it up

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 1.3f;
            main.startSpeed = 3.4f;
            main.startSize = 0.05f;
            main.gravityModifier = 1f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.8f);

            var emission = ps.emission;
            emission.rateOverTime = 140f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.03f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Material droplet = GreenhouseAssetFactory.WaterDroplet(false);
            if (droplet != null) renderer.sharedMaterial = droplet;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // --- Benches --------------------------------------------------------------

        /// <summary>A bench facing local +Z, centred on the origin, standing on y = 0. Submesh 0 timber, 1 steel.</summary>
        private static MeshData BenchTemplate()
        {
            var b = new MeshData();
            const float length = 1.7f;
            for (int i = -1; i <= 1; i++)
                MeshShapes.AddBox(b, new Vector3(0f, 0.45f, i * 0.15f), new Vector3(length, 0.05f, 0.13f), Quaternion.identity, 0);

            Quaternion lean = Quaternion.Euler(-12f, 0f, 0f);
            MeshShapes.AddBox(b, new Vector3(0f, 0.72f, -0.23f), new Vector3(length, 0.1f, 0.04f), lean, 0);
            MeshShapes.AddBox(b, new Vector3(0f, 0.88f, -0.26f), new Vector3(length, 0.1f, 0.04f), lean, 0);

            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (length * 0.5f - 0.1f);
                MeshShapes.AddBox(b, new Vector3(x, 0.225f, 0.18f), new Vector3(0.05f, 0.45f, 0.05f), Quaternion.identity, 1);
                MeshShapes.AddBox(b, new Vector3(x, 0.45f, -0.2f), new Vector3(0.05f, 0.9f, 0.05f), Quaternion.identity, 1);
                MeshShapes.AddBox(b, new Vector3(x, 0.43f, -0.01f), new Vector3(0.05f, 0.04f, 0.42f), Quaternion.identity, 1);
            }
            return b;
        }

        private static MeshData BuildBenches(Layout l, GreenhouseSettings settings)
        {
            var d = new MeshData();
            MeshData template = BenchTemplate();

            // Around the plaza, on the side away from the entrance.
            int n = Mathf.Max(0, settings.plazaBenches);
            float radius = l.PlazaR - 0.9f;
            for (int i = 0; i < n; i++)
            {
                float angle = n == 1 ? 0f : Mathf.Lerp(-125f, 125f, i / (float)(n - 1));
                Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector3 pos = new Vector3(l.PlazaX, 0f, 0f) + dir * radius;
                float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg; // face the fountain
                d.Append(template, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one));
            }

            // Beside the main path, facing it.
            if (l.HasBeds)
            {
                int pairs = settings.pathBenchPairs;
                for (int p = 0; p < pairs; p++)
                {
                    int bed = pairs == 1 ? l.BedsPerRow / 2 : Mathf.RoundToInt(p * (l.BedsPerRow - 1f) / (pairs - 1f));
                    float xc = l.BedX0(bed) + l.BedLen * 0.5f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 pos = new Vector3(xc, 0f, s * (l.PathW * 0.5f + 0.5f));
                        float yaw = s > 0 ? 180f : 0f; // face the path (towards z = 0)
                        d.Append(template, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one));
                    }
                }
            }
            return d;
        }

        // --- Decorative stones ----------------------------------------------------

        private static MeshData BuildStones(Layout l, GreenhouseSettings settings)
        {
            var d = new MeshData();
            var rng = new System.Random(settings.seed);
            int total = Mathf.Max(0, settings.decorativeStones);
            int pond = total / 4;
            int entrance = total / 4;
            float half = l.PathW * 0.5f;

            for (int i = 0; i < total; i++)
            {
                Vector3 pos;
                if (i < pond)
                {
                    float angle = Mathf.Lerp(-150f, 150f, (float)rng.NextDouble()) * Mathf.Deg2Rad;
                    float r = RimOuter + 0.25f + (float)rng.NextDouble() * 0.6f;
                    pos = new Vector3(l.PlazaX + Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                }
                else if (i < pond + entrance)
                {
                    float x = Mathf.Lerp(-l.HalfL - 8f, -l.HalfL - 1.8f, (float)rng.NextDouble());
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    pos = new Vector3(x, 0f, side * (half + 0.5f + (float)rng.NextDouble() * 1.4f));
                }
                else
                {
                    if (!TryAislePosition(l, rng, out pos)) continue;
                }

                float radius = 0.12f + (float)rng.NextDouble() * 0.28f;
                Vector3 scale = new Vector3(1f + (float)rng.NextDouble() * 0.4f, 0.55f + (float)rng.NextDouble() * 0.25f, 1f + (float)rng.NextDouble() * 0.3f);
                pos.y = radius * scale.y * 0.45f;

                var rock = new MeshData();
                MeshShapes.AddSphere(rock, Vector3.zero, radius, scale, 7, 10, rng, 0.18f);
                float yaw = (float)rng.NextDouble() * 360f;
                d.Append(rock, Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one));
            }
            return d;
        }

        /// <summary>Picks a spot in an aisle between bed rows (or the border aisle), clear of cross paths.</summary>
        private static bool TryAislePosition(Layout l, System.Random rng, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (!l.HasBeds) return false;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float x = Mathf.Lerp(l.XStart, l.XEnd, (float)rng.NextDouble());

                bool onCrossPath = false;
                for (int c = 0; c < l.BedsPerRow - 1; c++)
                {
                    float gx = l.BedX0(c) + l.BedLen + l.BedGap * 0.5f;
                    if (Mathf.Abs(x - gx) < l.BedGap * 0.5f + 0.3f) { onCrossPath = true; break; }
                }
                if (onCrossPath) continue;

                int row = rng.Next(0, l.Rows); // aisle after this row (the last one is the border aisle)
                float z = l.RowCenterZ(row) + l.RowW * 0.5f + (row == l.Rows - 1 ? 0.5f : l.Aisle * 0.5f);
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                pos = new Vector3(x, 0f, side * z);
                return true;
            }
            return false;
        }

        // --- Lighting fixtures ----------------------------------------------------

        private static void BuildLighting(Transform props, Layout l, GreenhouseSettings settings)
        {
            Transform parent = Child(props, "Lighting");
            if (settings.lampEveryNFrames <= 0) return;

            var d = new MeshData();
            var lights = new List<Vector3>();
            float mountY = l.WallH - 0.3f;
            const float drop = 1.6f;

            for (int i = settings.lampEveryNFrames; i < l.Bays; i += settings.lampEveryNFrames)
            {
                float x = l.FrameX[i];
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * l.HalfW * 0.5f;
                    float lampY = mountY - drop;

                    MeshShapes.AddTube(d, new Vector3(x, mountY, z), new Vector3(x, lampY, z), 0.015f, 6, 0, false);
                    MeshPrimitives.AddCylinder(d, new Vector3(x, lampY - 0.35f, z), 0.4f, 0.1f, 0.35f, 16, 1, true, true, 0);
                    MeshShapes.AddSphere(d, new Vector3(x, lampY - 0.4f, z), 0.1f, Vector3.one, 6, 10, new System.Random(7), 0f, 1);
                    lights.Add(new Vector3(x, lampY - 0.5f, z));
                }
            }

            GameObject fixtures = Make("Fixtures", parent, "Greenhouse_Lamps", d, new[] { settings.steel, settings.lampBulb }, collider: false);
            DisableShadows(fixtures);

            if (!settings.addPointLights) return;
            for (int i = 0; i < lights.Count; i++)
            {
                var go = new GameObject($"LampLight_{i + 1:00}");
                go.transform.SetParent(parent, false);
                go.transform.position = lights[i];
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.86f, 0.65f);
                light.range = 10f;
                light.intensity = 2.5f;
                light.shadows = LightShadows.None;
            }
        }

        // --- Scene environment ----------------------------------------------------

        private static void SetupSceneEnvironment(Layout l)
        {
            // Sun
            GameObject sunObject = GameObject.Find("Sun");
            Light sun = sunObject != null ? sunObject.GetComponent<Light>() : null;
            if (sun == null)
            {
                sunObject = new GameObject("Sun");
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.96f, 0.88f);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
                sunObject.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            }
            RenderSettings.sun = sun;

            // Skybox, ambient light, fog
            Material sky = GreenhouseAssetFactory.Sky(false);
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = false; // no global fog
            DynamicGI.UpdateEnvironment();

            // Post-processing volume, borrowing the template's profile (read only).
            if (Object.FindAnyObjectByType<Volume>() == null)
            {
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
                if (profile != null)
                {
                    var volumeObject = new GameObject("Global Volume");
                    var volume = volumeObject.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.sharedProfile = profile;
                }
            }

            // Camera with the orbit controller, framed on the entrance and plaza.
            Camera cam = Object.FindAnyObjectByType<Camera>();
            if (cam == null)
            {
                var camObject = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camObject.AddComponent<Camera>();
                camObject.AddComponent<AudioListener>();
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.fieldOfView = 55f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 500f;
            }

            Vector3 pivot = new Vector3(0f, l.WallH * 0.4f, 0f);
            float distance = Mathf.Max(l.L, l.W) * 1.1f;
            const float yaw = 62f;
            const float pitch = 22f;

            if (cam.GetComponent<CameraController>() == null)
            {
                var controller = cam.gameObject.AddComponent<CameraController>();
                var so = new SerializedObject(controller);
                so.FindProperty("defaultPivot").vector3Value = pivot;
                so.FindProperty("defaultDistance").floatValue = distance;
                so.FindProperty("defaultYaw").floatValue = yaw;
                so.FindProperty("defaultPitch").floatValue = pitch;
                so.FindProperty("maxDistance").floatValue = Mathf.Max(80f, distance * 2f);
                so.ApplyModifiedPropertiesWithoutUndo();

                Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
                cam.transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
            }
        }

        // --- Scene / asset helpers ------------------------------------------------

        private static Scene OpenOrCreateScene()
        {
            if (File.Exists(Path.GetFullPath(ScenePath)))
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static GreenhouseSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GreenhouseSettings>(SettingsPath);
            if (settings == null)
            {
                GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.SettingsFolder);
                settings = ScriptableObject.CreateInstance<GreenhouseSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Beam(MeshData d, Vector3 a, Vector3 b, float width, float height)
        {
            MeshShapes.AddBeam(d, a, b, width, height, 0);
        }

        private static void DisableShadows(GameObject go)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Saves the mesh as an asset (updating it in place on rebuild) and creates a renderer object for it.</summary>
        private static GameObject Make(string name, Transform parent, string meshName, MeshData data, Material[] materials, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Mesh mesh = SaveMesh(meshName, data);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            int count = Mathf.Max(1, mesh.subMeshCount);
            var used = new Material[count];
            for (int i = 0; i < count; i++) used[i] = materials[Mathf.Min(i, materials.Length - 1)];
            go.AddComponent<MeshRenderer>().sharedMaterials = used;

            if (collider && mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;

            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        private static Mesh SaveMesh(string meshName, MeshData data)
        {
            string path = $"{GreenhouseAssetFactory.MeshFolder}/{meshName}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();

            mesh.name = meshName;
            data.ApplyTo(mesh);
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // --- Expansion support (used by ConservatoryExpansion; the greenhouse build above is unchanged) -----------

        /// <summary>Geometry for a greenhouse lengthened at its east (rear) end, generated by the same code and modules as the original.</summary>
        public sealed class ExpansionData
        {
            public MeshData Frame, Glass, Plinth, Lamps;
            public readonly List<Vector3> LampLights = new List<Vector3>();
            /// <summary>Shift to apply to the generated meshes so the entrance stays where it was.</summary>
            public float OffsetX;
            public float OldEastX, NewEastX, HalfWidth, WallHeight, RidgeHeight, BayLength;
            public int OriginalBays, TotalBays;
            public GreenhouseSettings Settings;
        }

        /// <summary>
        /// Generates frame, glass, plinth and lamps for the greenhouse made <paramref name="extraLength"/> metres longer. The layout is centred on
        /// the new length, so the caller offsets it by <see cref="ExpansionData.OffsetX"/> = extraLength / 2; then every bay of the original
        /// building lands exactly where it was, and the new bays repeat the same modules. The settings asset itself is not modified.
        /// </summary>
        public static ExpansionData GenerateExpansion(float extraLength)
        {
            GreenhouseSettings baseSettings = LoadOrCreateSettings();
            GreenhouseAssetFactory.ResolveMaterials(baseSettings, false);
            var original = new Layout(baseSettings);

            GreenhouseSettings longer = Object.Instantiate(baseSettings);
            longer.length = baseSettings.length + extraLength;
            var l = new Layout(longer);

            var data = new ExpansionData
            {
                Frame = BuildFrame(l), Glass = BuildGlass(l), Plinth = BuildPlinth(l),
                OffsetX = extraLength * 0.5f,
                OldEastX = original.HalfL, NewEastX = original.HalfL + extraLength,
                HalfWidth = l.HalfW, WallHeight = l.WallH, RidgeHeight = l.H, BayLength = l.BayLen,
                OriginalBays = original.Bays, TotalBays = l.Bays, Settings = baseSettings
            };

            // Lamps for the new frames only (same pattern as BuildLighting: every Nth frame, two per frame).
            var d = new MeshData();
            float mountY = l.WallH - 0.3f;
            const float drop = 1.6f;
            int every = baseSettings.lampEveryNFrames;
            if (every > 0)
            {
                for (int i = every; i < l.Bays; i += every)
                {
                    if (i < original.Bays) continue; // the original lamps already hang there
                    float x = l.FrameX[i];
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float z = side * l.HalfW * 0.5f;
                        float lampY = mountY - drop;
                        MeshShapes.AddTube(d, new Vector3(x, mountY, z), new Vector3(x, lampY, z), 0.015f, 6, 0, false);
                        MeshPrimitives.AddCylinder(d, new Vector3(x, lampY - 0.35f, z), 0.4f, 0.1f, 0.35f, 16, 1, true, true, 0);
                        MeshShapes.AddSphere(d, new Vector3(x, lampY - 0.4f, z), 0.1f, Vector3.one, 6, 10, new System.Random(7), 0f, 1);
                        data.LampLights.Add(new Vector3(x, lampY - 0.5f, z));
                    }
                }
            }
            data.Lamps = d;
            Object.DestroyImmediate(longer);
            return data;
        }
    }
}
