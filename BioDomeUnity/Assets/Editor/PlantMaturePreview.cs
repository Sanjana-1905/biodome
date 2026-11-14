using System;
using System.Collections.Generic;
using System.IO;
using BioDome.Environment;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Editor-only preview of every plant in the open scene at a chosen growth (default 100%), with nothing changed.
    ///
    /// How it stays safe: a plant shows its growth by swapping the child MeshFilter's mesh, and that mesh reference (plus the
    /// generator's growth) is saved in the scene. This preview therefore never touches a plant. It builds the mature mesh of each plant
    /// IN MEMORY from that plant's own settings and draws it as an overlay with Graphics.DrawMesh in the Scene view and Game view.
    /// Turning the preview off simply stops drawing and frees the temporary meshes.
    ///
    /// Not touched: the scene (it is never marked dirty or saved), PlantController, PlantSimulation (health, moisture, age, growth),
    /// the simulation clock and every asset. The live plants keep showing their normal growth underneath the overlay.
    /// The preview switches itself off before Play Mode, before a script reload and when the scene is opened or closed.
    /// </summary>
    public static class PlantMaturePreview
    {
        private sealed class Entry
        {
            public Transform Transform;
            public int Layer;
            public Mesh Mesh;
            public Material[] Materials;
            public MaterialPropertyBlock[] Blocks;
        }

        public struct ViewPreset
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Target;
            public float Fov;
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly Dictionary<long, Mesh> meshCache = new Dictionary<long, Mesh>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static bool active;
        private static bool hooked;
        private static bool listStale;
        private static double lastChangeTime;
        private static float growth = 1f;
        private static bool sceneWasCleanAtStart;
        private static Scene previewedScene;

        public static event Action StateChanged;

        public static bool Active => active;
        public static int PlantCount { get; private set; }
        public static int MeshCount => meshCache.Count;
        public static long TriangleCount { get; private set; }

        /// <summary>True unless enabling the preview somehow left a previously clean scene marked as modified.</summary>
        public static bool SceneUntouched => !active || !sceneWasCleanAtStart || !previewedScene.isDirty;

        public static float Growth
        {
            get => growth;
            set
            {
                float clamped = Mathf.Clamp(value, 0.1f, 1f);
                if (Mathf.Approximately(clamped, growth)) return;
                growth = clamped;
                if (active) Rebuild();
            }
        }

        // --- Switching on and off -------------------------------------------------------

        public static bool SetActive(bool enable)
        {
            if (enable == active) return true;

            if (!enable)
            {
                active = false;
                Unhook();
                Clear();
                Repaint();
                StateChanged?.Invoke();
                return true;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

            previewedScene = SceneManager.GetActiveScene();
            sceneWasCleanAtStart = !previewedScene.isDirty;
            active = true;
            Hook();
            Rebuild();
            Repaint();
            StateChanged?.Invoke();
            return true;
        }

        private static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            EditorApplication.update += OnUpdate;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorSceneManager.sceneOpening += OnSceneChange;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorApplication.quitting += OnQuit;
        }

        private static void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            EditorApplication.update -= OnUpdate;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            EditorSceneManager.sceneOpening -= OnSceneChange;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            EditorApplication.quitting -= OnQuit;
        }

        // Anything that could invalidate the temporary meshes or leave the overlay on by accident switches it off.
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) SetActive(false);
        }
        private static void OnBeforeReload() => SetActive(false);
        private static void OnSceneChange(string path, OpenSceneMode mode) => SetActive(false);
        private static void OnSceneClosing(Scene scene, bool removing) => SetActive(false);
        private static void OnQuit() => SetActive(false);

        private static void OnHierarchyChanged()
        {
            listStale = true;
            lastChangeTime = EditorApplication.timeSinceStartup;
        }

        private static void OnUpdate()
        {
            // Rebuild the list shortly after the scene changed (plants added or removed), not on every edit event.
            if (active && listStale && EditorApplication.timeSinceStartup - lastChangeTime > 0.6)
            {
                Rebuild();
                Repaint();
                StateChanged?.Invoke();
            }
        }

        // --- Building the overlay ------------------------------------------------------------

        private static void Rebuild()
        {
            listStale = false;
            entries.Clear();
            ClearMeshes();
            TriangleCount = 0;

            int step = PlantGenerator.StepOf(growth);
            var counted = new Dictionary<Mesh, long>();

            foreach (PlantGenerator generator in UnityEngine.Object.FindObjectsByType<PlantGenerator>())
            {
                if (generator == null || EditorUtility.IsPersistent(generator)) continue; // scene objects only, never prefab assets
                if (!generator.gameObject.scene.IsValid() || generator.Renderer == null) continue;

                // Plants of the same shape share one temporary mesh.
                long key = ((long)generator.GetType().GetHashCode() * 397L) ^ ((long)generator.VariantKey << 8) ^ (uint)step;
                if (!meshCache.TryGetValue(key, out Mesh mesh))
                {
                    mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "MaturePreview" };
                    generator.BuildMeshInto(mesh, growth); // reads the plant's settings; changes nothing on the plant
                    meshCache.Add(key, mesh);
                }

                var entry = new Entry
                {
                    Transform = generator.Renderer.transform,
                    Layer = generator.Renderer.gameObject.layer,
                    Mesh = mesh,
                    Materials = new Material[mesh.subMeshCount],
                    Blocks = new MaterialPropertyBlock[mesh.subMeshCount]
                };
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    entry.Materials[i] = generator.MaterialForSubmesh(i);
                    if (generator.TryGetLeafTint(i, out Color tint))
                    {
                        var block = new MaterialPropertyBlock();
                        block.SetColor(BaseColorId, tint);
                        block.SetColor(ColorId, tint);
                        entry.Blocks[i] = block;
                    }
                }
                entries.Add(entry);

                if (!counted.TryGetValue(mesh, out long triangles))
                {
                    triangles = 0;
                    for (int i = 0; i < mesh.subMeshCount; i++) triangles += mesh.GetIndexCount(i) / 3;
                    counted.Add(mesh, triangles);
                }
                TriangleCount += triangles;
            }

            PlantCount = entries.Count;
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!active) return;
            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game) return;

            for (int e = 0; e < entries.Count; e++)
            {
                Entry entry = entries[e];
                if (entry.Transform == null) { listStale = true; continue; }

                Matrix4x4 matrix = entry.Transform.localToWorldMatrix;
                for (int s = 0; s < entry.Mesh.subMeshCount; s++)
                {
                    if (entry.Materials[s] == null) continue;
                    Graphics.DrawMesh(entry.Mesh, matrix, entry.Materials[s], entry.Layer, camera, s, entry.Blocks[s],
                        ShadowCastingMode.On, true);
                }
            }
        }

        private static void Clear()
        {
            entries.Clear();
            ClearMeshes();
            PlantCount = 0;
            TriangleCount = 0;
        }

        private static void ClearMeshes()
        {
            foreach (Mesh mesh in meshCache.Values)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            meshCache.Clear();
        }

        private static void Repaint()
        {
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // --- Camera angles ------------------------------------------------------------------

        /// <summary>Overview angles plus one per planting zone, worked out from where the planting zones are.</summary>
        public static List<ViewPreset> BuildViews()
        {
            var views = new List<ViewPreset>();
            PlantingZone[] zones = UnityEngine.Object.FindObjectsByType<PlantingZone>();
            if (zones.Length == 0) return views;

            Bounds bounds = new Bounds(zones[0].transform.position, Vector3.zero);
            foreach (PlantingZone z in zones)
            {
                bounds.Encapsulate(z.transform.TransformPoint(new Vector3(z.Size.x * 0.5f, 0f, z.Size.y * 0.5f)));
                bounds.Encapsulate(z.transform.TransformPoint(new Vector3(-z.Size.x * 0.5f, 0f, -z.Size.y * 0.5f)));
            }
            Vector3 c = bounds.center;
            Vector3 s = bounds.size;

            views.Add(new ViewPreset
            {
                Name = "Overview from the west end",
                Position = new Vector3(c.x - s.x * 0.5f - 3f, 8.5f, c.z),
                Target = new Vector3(c.x + 2f, 0.8f, c.z),
                Fov = 62f
            });
            views.Add(new ViewPreset
            {
                Name = "Overview from high up",
                Position = new Vector3(c.x - 4f, 17f, c.z - s.z * 0.5f - 3f),
                Target = new Vector3(c.x - 4f, 0.5f, c.z + 3f),
                Fov = 60f
            });

            var sorted = new List<PlantingZone>(zones);
            sorted.Sort((a, b) => string.CompareOrdinal(a.ZoneName, b.ZoneName));
            foreach (PlantingZone z in sorted)
            {
                Vector3 center = z.transform.position;
                float aisleSign = center.z >= 0f ? 1f : -1f; // the central aisle runs along world X; stand on its side of the bed
                bool border = z.Size.x > 15f;
                views.Add(new ViewPreset
                {
                    Name = "Bed " + z.ZoneName,
                    Position = new Vector3(border ? center.x - 6f : center.x, border ? 2.0f : 2.6f, center.z - aisleSign * (border ? 4.6f : 4.8f)),
                    Target = new Vector3(border ? center.x - 6f : center.x, 0.9f, center.z),
                    Fov = border ? 70f : 62f
                });
            }
            return views;
        }

        /// <summary>Moves the Scene view camera (an editor-only setting, not scene data) to a preset angle.</summary>
        public static bool GoTo(ViewPreset view)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return false;

            Vector3 offset = view.Target - view.Position;
            sceneView.LookAt(view.Target, Quaternion.LookRotation(offset, Vector3.up), offset.magnitude, false, false);
            sceneView.Repaint();
            return true;
        }

        /// <summary>Renders the given angles to PNG files with a temporary camera (nothing in the scene changes). Returns how many were written.</summary>
        public static int SaveScreenshots(string folder, IList<ViewPreset> views, int width = 1600, int height = 900)
        {
            Directory.CreateDirectory(folder);
            int written = 0;
            foreach (ViewPreset view in views)
            {
                var go = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = go.AddComponent<Camera>();
                camera.fieldOfView = view.Fov;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 250f;
                camera.transform.position = view.Position;
                camera.transform.rotation = Quaternion.LookRotation(view.Target - view.Position, Vector3.up);

                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();
                camera.Render(); // the first frame after creating a camera can come out unlit

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;

                string safe = view.Name.Replace(' ', '_');
                File.WriteAllBytes(Path.Combine(folder, safe + ".png"), image.EncodeToPNG());
                written++;

                UnityEngine.Object.DestroyImmediate(image);
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(go);
            }
            return written;
        }
    }
}
