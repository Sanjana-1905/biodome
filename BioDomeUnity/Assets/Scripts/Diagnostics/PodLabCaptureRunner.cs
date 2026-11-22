using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BioDome.CameraSystem;
using BioDome.Core;
using BioDome.Experiment;
using BioDome.Plants;
using UnityEngine;

namespace BioDome.Diagnostics
{
    /// <summary>
    /// Play Mode screenshot run (true Game view with all UI, ScreenCapture) for the Pod Lab and the plant condition colours.
    /// It drives one Control-pod plant through healthy, stressed, wilted, damaged and dead ONLY through the Pod Lab controls
    /// (irrigation 0, +20 C) and the normal simulation. Added at run time by the editor menu "BioDome/Tests/Capture Pod Lab Screenshots".
    /// </summary>
    public sealed class PodLabCaptureRunner : MonoBehaviour
    {
        public string OutDir;
        private readonly StringBuilder log = new StringBuilder("shot,width,height,day,hour,health,wilt,state,stage,soil,pod_lab_open,speed_x,paused\n");
        private GreenhouseManager manager;
        private CameraController cam;
        private PodTestPanel lab;
        private ExperimentGarden garden;

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(OutDir);
            manager = FindAnyObjectByType<GreenhouseManager>();
            cam = FindAnyObjectByType<CameraController>();
            garden = FindAnyObjectByType<ExperimentGarden>();
            while ((lab = FindAnyObjectByType<PodTestPanel>()) == null || !lab.isActiveAndEnabled) yield return null;
            for (int i = 0; i < 30; i++) { Repaint(); yield return null; }

            // 0. Whole interface with Pod Lab closed (environment panel, comparison panel, speed bar, view bar).
            yield return Shot("00_interface_podlab_closed", null);

            // 1. Pod Lab open on the Heat pod with one changed value (yellow indicator), comparison panel hidden.
            lab.SetOpen(true);
            lab.SelectPod(1);
            ExperimentPod heat = garden.Pods[1];
            heat.SetLiveConditions(heat.Profile.TemperatureOffsetCelsius + 4f, heat.Profile.HumidityOffset, heat.Profile.LightMultiplier, heat.Profile.WaterShareWeight);
            lab.SetSpeed(50f);
            yield return Wait(1.5f);
            yield return Shot("01_podlab_heat_pod_changed_50x", null);
            heat.ResetLiveConditions();

            // 2. Control pod, plant 1, inspected and framed close.
            ExperimentPod control = garden.Pods[0];
            PlantController plant = control.Plants[0];
            lab.SelectPod(0);
            lab.FocusPod();
            lab.SetSpeed(10f);
            yield return Wait(1f);
            yield return Shot("02_healthy_control_plant", plant);

            // 3. Drought and heat through the Pod Lab: stressed -> wilted -> damaged (alive) -> dead.
            control.SetLiveConditions(20f, 0f, 1f, 0f);
            lab.SetSpeed(100f);
            yield return Until(() => plant.Simulation.Health01 <= 0.62f, 90f);
            yield return PausedShot("03_stressed_plant", plant);
            yield return Until(() => plant.Simulation.Wilt01 >= 0.7f, 60f);
            yield return PausedShot("04_wilted_plant", plant);
            lab.SetSpeed(10f); // health falls fast at 100x; slow down so the damaged-but-alive state is caught
            yield return Until(() => (plant.Simulation.Health01 <= 0.3f && garden.GreenhouseNow.Light01 > 0.5f) || plant.Simulation.Health01 <= 0.08f || !plant.Simulation.IsAlive, 120f);
            yield return PausedShot("05_damaged_leaves_living_stems", plant);
            lab.SetSpeed(100f);
            yield return Until(() => !plant.Simulation.IsAlive, 90f);
            float deathHour = Hours();
            yield return Until(() => Hours() - deathHour >= 36f && garden.GreenhouseNow.Light01 > 0.5f, 60f); // stems fade over ~12 simulated hours; wait for daylight
            yield return PausedShot("06_dead_plant", plant);

            // 4. Close Pod Lab: comparison panel must come back.
            control.ResetLiveConditions();
            lab.SetOpen(false);
            yield return Wait(0.5f);
            yield return Shot("07_interface_after_closing_podlab", plant);

            File.WriteAllText(Path.Combine(OutDir, "shots.csv"), log.ToString());
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }

        private float Hours() { var c = garden.GreenhouseNow; return c.DayCount * 24f + c.TimeOfDayHours; }

        private IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { Repaint(); yield return null; }
        }

        private IEnumerator Until(System.Func<bool> condition, float timeoutSeconds)
        {
            float end = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < end) { Repaint(); yield return null; }
        }

        private IEnumerator PausedShot(string name, PlantController plant)
        {
            manager.SetPaused(true);
            cam.SnapTo(plant.transform.position + Vector3.up * 0.45f, 2.4f, 30f, 22f);
            manager.SelectPlant(plant);
            yield return Wait(0.6f);
            yield return Shot(name, plant);
            manager.SetPaused(false);
        }

        private IEnumerator Shot(string name, PlantController plant)
        {
            for (int i = 0; i < 6; i++) { Repaint(); yield return null; }
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            int w = tex.width, h = tex.height;
            Destroy(tex);
            var g = garden.GreenhouseNow;
            var s = plant != null ? plant.Simulation : null;
            log.AppendLine(string.Join(",", name, w, h, g.DayCount + 1, g.TimeOfDayHours.ToString("0.0", CultureInfo.InvariantCulture),
                s != null ? s.Health01.ToString("0.000", CultureInfo.InvariantCulture) : "", s != null ? s.Wilt01.ToString("0.00", CultureInfo.InvariantCulture) : "",
                s != null ? s.State.ToString() : "", s != null ? s.Stage.ToString() : "", s != null ? s.Water01.ToString("0.00", CultureInfo.InvariantCulture) : "",
                lab.IsOpen, (manager.SimulatedHoursPerSecond / manager.NormalHoursPerSecond).ToString("0.#", CultureInfo.InvariantCulture), manager.Paused));
            Debug.Log("[PodLabCapture] " + name);
        }

        private static void Repaint()
        {
#if UNITY_EDITOR
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }
    }
}
