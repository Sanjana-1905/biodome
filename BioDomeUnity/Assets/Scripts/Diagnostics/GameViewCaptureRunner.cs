using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BioDome.CameraSystem;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using BioDome.UI;
using TMPro;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Play Mode helper (added at run time by PlayModeGameViewCapture). Walks the REAL scene through the states that need visual evidence and takes true Game-view
    /// screenshots with ScreenCapture (so the on-screen UI, including the plant inspection panel, is in the picture). It moves no plant and changes no asset or scene:
    /// plants are only dried or watered through the simulation, and temporary spray particle effects are created at run time (the scene has none).
    /// </summary>
    public sealed class GameViewCaptureRunner : MonoBehaviour
    {
        public string OutDir;
        private readonly StringBuilder log = new StringBuilder("shot,real_s,species,growth,health,wilt,bloom,water,irrigation_state,inspection_panel\n");
        private GreenhouseManager manager;
        private CameraController camCtl;
        private float t0;

        private IEnumerator Start()
        {
            Directory.CreateDirectory(OutDir);
            Application.runInBackground = true;
            manager = FindFirstObjectByType<GreenhouseManager>();
            camCtl = FindFirstObjectByType<CameraController>();
            t0 = Time.realtimeSinceStartup;
            for (int i = 0; i < 20; i++) yield return null;
            // 0. Scene overview shots (true Game view, scene's own camera and UI), taken before any speed change.
            Vector3 Where(string n, Vector3 fallback) { var g = GameObject.Find(n); return g != null ? g.transform.position : fallback; }
            var glassN = GameObject.Find("Glass_N"); var glassS = GameObject.Find("Glass_S");
            Vector3 centre = (glassN != null && glassS != null) ? (glassN.transform.position + glassS.transform.position) * 0.5f : Vector3.zero;
            camCtl.SnapTo(centre, 60f, 30f, 35f); manager.ClearSelection();
            yield return CaptureFrame("00_overview_greenhouse", null, null);
            camCtl.SnapTo(Where("FountainSpray", centre), 9f, 28f, 25f);
            yield return CaptureFrame("00_fountain", null, null);
            camCtl.SnapTo(Where("Group_Research", centre), 22f, 30f, 34f);
            yield return CaptureFrame("00_research_garden", null, null);
            foreach (string podName in new[] { "Pod_Control", "Pod_HeatStress", "Pod_LowHumidity", "Pod_LowLight", "Pod_WaterStress" })
            {
                camCtl.SnapTo(Where(podName, centre) + Vector3.up * 0.4f, 7f, 30f, 30f);
                yield return CaptureFrame("00_" + podName.ToLowerInvariant(), null, null);
            }
            camCtl.SnapTo(Where("PlantingZone_Bed_S1_1", centre), 14f, 30f, 32f);
            yield return CaptureFrame("00_collection_zone", null, null);
            manager.SetSpeedMultiplier(10f); // 4 simulated hours per real second

            var all = FindObjectsByType<PlantController>(FindObjectsSortMode.None);
            PlantController Pick(string id, int skip) => all.Where(p => p.ZoneProfile == null && p.Data.SpeciesId == id && p.Simulation.IsAlive).OrderByDescending(p => p.Simulation.Growth01).ThenBy(p => p.name).Skip(skip).First();
            PlantController monstera = Pick("monstera", 0), anthurium = Pick("anthurium", 0), orchid = Pick("orchid", 0), bird = Pick("bird_of_paradise", 0);

            Debug.Log("[Capture] subjects growth: monstera " + monstera.Simulation.Growth01.ToString("0.00") + " anthurium " + anthurium.Simulation.Growth01.ToString("0.00") + " orchid " + orchid.Simulation.Growth01.ToString("0.00") + " bird " + bird.Simulation.Growth01.ToString("0.00"));
            // 1. Healthy
            yield return Shot("01_healthy_monstera", monstera, 2.6f);

            // 2. Wilted (dried through the simulation, then captured)
            float limit = Time.realtimeSinceStartup + 25f;
            while (monstera.Simulation.Wilt01 < 0.8f && Time.realtimeSinceStartup < limit) { SetWater(monstera, 0.02f); yield return null; }
            yield return Shot("02_wilted_monstera", monstera, 2.6f);

            // 3. Recovering after watering
            monstera.ReceiveWater(0.7f * monstera.Data.WaterCapacityLitres);
            limit = Time.realtimeSinceStartup + 25f;
            while (monstera.Simulation.Wilt01 > 0.5f && Time.realtimeSinceStartup < limit) yield return null;
            yield return Shot("03_recovering_monstera", monstera, 2.6f);

            // 4. Flowering: let the greenhouse run (untouched) until each subject is mature and in bloom
            limit = Time.realtimeSinceStartup + 240f;
            while ((anthurium.Simulation.Bloom01 < 0.5f || orchid.Simulation.Bloom01 < 0.5f || bird.Simulation.Bloom01 < 0.5f) && Time.realtimeSinceStartup < limit) yield return null;
            Debug.Log("[Capture] bloom: anthurium " + anthurium.Simulation.Bloom01.ToString("0.00") + " orchid " + orchid.Simulation.Bloom01.ToString("0.00") + " bird " + bird.Simulation.Bloom01.ToString("0.00"));
            yield return Shot("04_flowering_anthurium", anthurium, 1.8f);
            yield return Shot("05_flowering_orchid", orchid, 1.4f);
            yield return Shot("06_flowering_bird_of_paradise", bird, 3.4f);

            // 5. Flowers fading (orchid dried until its bloom has dropped)
            limit = Time.realtimeSinceStartup + 40f;
            while (orchid.Simulation.Bloom01 > 0.45f && Time.realtimeSinceStartup < limit) { SetWater(orchid, 0.02f); yield return null; }
            yield return Shot("07_orchid_flowers_fading", orchid, 1.4f);

            // 6/7. Sprinklers. The scene has no sprinkler effects, so a temporary spray is attached at run time to two controllers.
            var groups = FindObjectsByType<BedIrrigationGroup>(FindObjectsSortMode.None).Where(g => g.Irrigation != null).OrderBy(g => g.name).ToList();
            BedIrrigationGroup active = groups.First(g => g.name == "Bed_Monstera");
            foreach (var g in groups) AttachTemporarySpray(g);

            // 6. Actively delivering: the bed's plants are dried through the simulation so the controller delivers at its full flow.
            foreach (var p in PlantsOf(active)) SetWater(p, 0.05f);
            limit = Time.realtimeSinceStartup + 20f;
            while (!active.Irrigation.IsSpraying && Time.realtimeSinceStartup < limit) yield return null;
            for (int i = 0; i < 20; i++) yield return null;
            yield return ShotAt("08_sprinkler_actively_delivering", active, 6.5f, PlantsOf(active).First(), controller: active.Irrigation);

            // 7. On but delivering a trickle: a controller that is running yet not spraying.
            float wait = Time.realtimeSinceStartup + 40f;
            BedIrrigationGroup quiet = null;
            while (quiet == null && Time.realtimeSinceStartup < wait)
            {
                quiet = groups.FirstOrDefault(g => g != active && g.Irrigation.IsRunning && !g.Irrigation.IsSpraying && g.Irrigation.DeliveryFraction01 < 0.2f);
                yield return null;
            }
            if (quiet != null) yield return ShotAt("09_controller_on_but_negligible_delivery", quiet, 6.5f, PlantsOf(quiet).First(), controller: quiet.Irrigation);
            else log.AppendLine("09_controller_on_but_negligible_delivery,,no such controller found");

            File.WriteAllText(Path.Combine(OutDir, "shots.csv"), log.ToString());
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }

        private IEnumerator Shot(string name, PlantController plant, float distance)
        {
            camCtl.SnapTo(plant.transform.position + Vector3.up * 0.35f, distance, 30f, 16f);
            manager.SelectPlant(plant); // opens the inspection panel
            yield return CaptureFrame(name, plant, null);
        }

        private IEnumerator ShotAt(string name, BedIrrigationGroup bed, float distance, PlantController focus, IrrigationController controller)
        {
            camCtl.SnapTo(focus.transform.position + Vector3.up * 0.6f, distance, 30f, 24f);
            manager.ClearSelection();
            yield return CaptureFrame(name, focus, controller);
        }

        private IEnumerator CaptureFrame(string name, PlantController plant, IrrigationController controller)
        {
            for (int i = 0; i < 8; i++) { Repaint(); yield return null; }
            Repaint();
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            int w = tex.width, h = tex.height; DestroyImmediate(tex);

            if (plant == null) { log.AppendLine(name + $" ({w}x{h}),{(Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture)},overview"); yield break; }
            var s = plant.Simulation;
            string irr = controller == null ? "" : $"running={controller.IsRunning} spraying={controller.IsSpraying} deliveredShare={controller.DeliveryFraction01:0.00} mode={controller.CurrentMode}";
            string panel = ReadInspectionPanel().Replace("\n", " / ").Replace(",", ";");
            log.AppendLine(string.Join(",", new[] { name + $" ({w}x{h})", (Time.realtimeSinceStartup - t0).ToString("0.0", CultureInfo.InvariantCulture), plant.Data.SpeciesId,
                F(s.Growth01), F(s.Health01), F(s.Wilt01), F(s.Bloom01), F(s.Water01), irr, panel }));
        }

        private static void Repaint()
        {
#if UNITY_EDITOR
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        private string ReadInspectionPanel()
        {
            var ui = FindFirstObjectByType<UIManager>();
            if (ui == null) return "";
            var field = typeof(UIManager).GetField("plantDetailsText", BindingFlags.NonPublic | BindingFlags.Instance);
            var panelField = typeof(UIManager).GetField("plantPanel", BindingFlags.NonPublic | BindingFlags.Instance);
            var panel = panelField?.GetValue(ui) as GameObject;
            var text = field?.GetValue(ui) as TMP_Text;
            if (text == null || panel == null || !panel.activeInHierarchy) return "(panel hidden)";
            return System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "");
        }

        private static List<PlantController> PlantsOf(BedIrrigationGroup g) =>
            ((List<PlantController>)typeof(BedIrrigationGroup).GetField("plants", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(g)).Where(p => p != null).ToList();

        private static void SetWater(PlantController p, float value) =>
            typeof(BioDome.Simulation.PlantSimulation).GetProperty("Water01").GetSetMethod(true).Invoke(p.Simulation, new object[] { value });

        // A simple droplet fountain above the bed, attached to the controller's spray hook (not saved anywhere).
        private static void AttachTemporarySpray(BedIrrigationGroup g)
        {
            var plants = PlantsOf(g); if (plants.Count == 0) return;
            Vector3 centre = plants.Aggregate(Vector3.zero, (a, p) => a + p.transform.position) / plants.Count;
            var go = new GameObject("TemporarySpray_" + g.name);
            go.transform.position = centre + Vector3.up * 1.6f; go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // emit downward
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = true; main.startLifetime = 1.0f; main.startSpeed = 1.8f; main.startSize = 0.06f; main.gravityModifier = 0.6f;
            main.startColor = new Color(0.8f, 0.92f, 1f, 0.85f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 600;
            var emission = ps.emission; emission.rateOverTime = 160f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55f; shape.radius = 0.9f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { color = new Color(0.8f, 0.92f, 1f, 0.85f) };
            typeof(IrrigationController).GetField("sprayEffects", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(g.Irrigation, new[] { ps });
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
