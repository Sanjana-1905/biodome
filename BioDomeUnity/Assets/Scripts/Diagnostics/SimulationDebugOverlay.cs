#if UNITY_EDITOR || DEBUG
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BioDome.Diagnostics
{
    /// <summary>
    /// Read-only on-screen diagnostics for the BioDome simulation (Game view).
    /// It only READS from the existing systems (EnvironmentController, IrrigationController,
    /// GreenhouseManager and each PlantController's PlantSimulation). It owns no simulation state,
    /// changes nothing, and has no controls that modify the simulation. Press F1 to hide or show it.
    ///
    /// It creates itself automatically when Play Mode starts (a temporary, never-saved GameObject),
    /// so the scene does not need to be edited. It is compiled only in the Editor and development builds.
    /// </summary>
    public sealed class SimulationDebugOverlay : MonoBehaviour
    {
        private const float TextRefreshSeconds = 0.2f;
        private const float PlantRescanSeconds = 1f;
        private const float PanelWidth = 440f;

        private GreenhouseManager manager;
        private EnvironmentController environment;
        private IrrigationController irrigation;
        private PlantController[] plants = new PlantController[0];

        private readonly StringBuilder builder = new StringBuilder(1024);
        private string text = "Starting...";
        private float textTimer;
        private float rescanTimer = PlantRescanSeconds; // scan on first frame
        private bool visible = true;

        // Measured simulation speed (read from the clock itself; the manager's field is private).
        private double lastSimHours = double.NaN;
        private float lastRealTime;
        private float measuredHoursPerSecond;

        private GUIStyle labelStyle;
        private Texture2D backgroundTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<SimulationDebugOverlay>() != null) return;

            var go = new GameObject("~SimulationDebugOverlay") { hideFlags = HideFlags.DontSave };
            go.AddComponent<SimulationDebugOverlay>();
        }

        private void OnDestroy()
        {
            if (backgroundTexture != null) Destroy(backgroundTexture);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) visible = !visible;

            rescanTimer += Time.unscaledDeltaTime;
            if (rescanTimer >= PlantRescanSeconds)
            {
                rescanTimer = 0f;
                FindSystems();
            }

            textTimer += Time.unscaledDeltaTime;
            if (textTimer >= TextRefreshSeconds)
            {
                textTimer = 0f;
                BuildText();
            }
        }

        private void FindSystems()
        {
            if (manager == null) manager = FindAnyObjectByType<GreenhouseManager>();
            if (environment == null) environment = FindAnyObjectByType<EnvironmentController>();
            if (irrigation == null) irrigation = FindAnyObjectByType<IrrigationController>();
            plants = FindObjectsByType<PlantController>();
        }

        // --- Text ----------------------------------------------------------------

        private void BuildText()
        {
            StringBuilder b = builder;
            b.Clear();
            b.Append("<b>BIODOME DEBUG (read-only)</b>   <color=#9aa>[F1 hide]</color>\n");

            if (environment == null)
            {
                b.Append("<color=#f66>No EnvironmentController found in the scene.</color>");
                text = b.ToString();
                return;
            }

            AppendEnvironment(b);
            AppendIrrigation(b);
            AppendPlants(b);
            text = b.ToString();
        }

        private void AppendEnvironment(StringBuilder b)
        {
            EnvironmentConditions c = environment.Current;
            int hours = Mathf.FloorToInt(c.TimeOfDayHours) % 24;
            int minutes = Mathf.FloorToInt((c.TimeOfDayHours - Mathf.Floor(c.TimeOfDayHours)) * 60f) % 60;

            // Measured speed: simulated hours advanced per real second since the last refresh.
            double simHours = c.DayCount * 24.0 + c.TimeOfDayHours;
            float now = Time.unscaledTime;
            if (!double.IsNaN(lastSimHours) && now > lastRealTime)
                measuredHoursPerSecond = Mathf.Max(0f, (float)((simHours - lastSimHours) / (now - lastRealTime)));
            lastSimHours = simHours;
            lastRealTime = now;

            bool paused = manager != null && manager.Paused;

            b.Append("\n<b>TIME</b>\n");
            b.Append(c.Season).Append("  Day ").Append(c.DayCount + 1).Append("  ")
                .Append(hours.ToString("00")).Append(':').Append(minutes.ToString("00"));
            b.Append(paused ? "   <color=#fc4>PAUSED</color>" : "").Append('\n');
            b.Append("Speed: ").Append(measuredHoursPerSecond.ToString("0.00")).Append(" sim-h per real second\n");

            b.Append("\n<b>GREENHOUSE</b>\n");
            b.Append("Temperature: <b>").Append(c.TemperatureCelsius.ToString("0.0")).Append(" °C</b>")
                .Append("   (offset ").Append(environment.TemperatureOffsetCelsius.ToString("+0.0;-0.0;0.0")).Append(")\n");
            b.Append("Humidity: <b>").Append((c.Humidity01 * 100f).ToString("0.0")).Append(" %</b>")
                .Append("   (offset ").Append((environment.HumidityOffset * 100f).ToString("+0;-0;0")).Append(" %)\n");
            b.Append("Light: ").Append((c.Light01 * 100f).ToString("0")).Append(" %\n");
        }

        private void AppendIrrigation(StringBuilder b)
        {
            b.Append("\n<b>IRRIGATION</b>\n");
            if (irrigation == null)
            {
                b.Append("<color=#f66>No IrrigationController found.</color>\n");
                return;
            }

            b.Append("Mode: <b>").Append(irrigation.CurrentMode).Append("</b>   Watering: ")
                .Append(irrigation.IsRunning ? "<color=#5cf>ACTIVE</color>" : "idle").Append('\n');
            b.Append("Reservoir: <b>").Append(irrigation.WaterLitres.ToString("0")).Append(" L</b> / ")
                .Append(irrigation.ReservoirCapacityLitres.ToString("0")).Append(" L (")
                .Append((irrigation.Reservoir01 * 100f).ToString("0")).Append(" %)\n");
        }

        private void AppendPlants(StringBuilder b)
        {
            int total = 0, alive = 0;
            int[] states = new int[4];
            int[] stages = new int[4];
            float moistMin = 1f, moistMax = 0f, moistSum = 0f;
            float healthMin = 1f, healthMax = 0f, healthSum = 0f;
            float growthMin = 1f, growthMax = 0f, growthSum = 0f;
            float stressSum = 0f;

            for (int i = 0; i < plants.Length; i++)
            {
                PlantController p = plants[i];
                if (p == null || !p.IsInitialized) continue;
                var s = p.Simulation;

                total++;
                if (s.IsAlive) alive++;
                states[(int)s.State]++;
                stages[(int)s.Stage]++;

                moistSum += s.Water01; moistMin = Mathf.Min(moistMin, s.Water01); moistMax = Mathf.Max(moistMax, s.Water01);
                healthSum += s.Health01; healthMin = Mathf.Min(healthMin, s.Health01); healthMax = Mathf.Max(healthMax, s.Health01);
                growthSum += s.Growth01; growthMin = Mathf.Min(growthMin, s.Growth01); growthMax = Mathf.Max(growthMax, s.Growth01);
                stressSum += s.Stress01;
            }

            b.Append("\n<b>PLANTS</b> (").Append(total).Append(")\n");
            if (total == 0)
            {
                b.Append("<color=#fc4>No initialised plants found. Is the BioDome_Plants group in the scene?</color>\n");
                return;
            }

            b.Append("Soil moisture is stored PER PLANT (not shared).\n");
            b.Append("Health states: ")
                .Append("<color=#5e5>Healthy ").Append(states[(int)PlantHealthState.Healthy]).Append("</color>  ")
                .Append("<color=#fd4>Stressed ").Append(states[(int)PlantHealthState.Stressed]).Append("</color>  ")
                .Append("<color=#f93>Critical ").Append(states[(int)PlantHealthState.Critical]).Append("</color>  ")
                .Append("<color=#f55>Dead ").Append(states[(int)PlantHealthState.Dead]).Append("</color>\n");
            b.Append("Stages: Seedling ").Append(stages[0]).Append("  Juvenile ").Append(stages[1])
                .Append("  Established ").Append(stages[2]).Append("  Mature ").Append(stages[3]).Append('\n');

            AppendRange(b, "Soil moisture", moistMin, moistSum / total, moistMax);
            AppendRange(b, "Health", healthMin, healthSum / total, healthMax);
            AppendRange(b, "Growth", growthMin, growthSum / total, growthMax);
            b.Append("Avg stress: ").Append((stressSum / total * 100f).ToString("0")).Append(" %\n");

            float spread = (growthMax - growthMin) * 100f;
            bool oneStage = CountNonZero(stages) == 1;
            b.Append("Growth spread: ").Append(spread.ToString("0.00")).Append(" %   Same stage: ")
                .Append(oneStage ? "<color=#5e5>yes</color>" : "<color=#fc4>no</color>").Append('\n');

            AppendFocusPlant(b);
        }

        private void AppendFocusPlant(StringBuilder b)
        {
            PlantController focus = manager != null ? manager.SelectedPlant : null;
            bool selected = focus != null && focus.IsInitialized;
            if (!selected)
            {
                for (int i = 0; i < plants.Length; i++)
                {
                    if (plants[i] != null && plants[i].IsInitialized) { focus = plants[i]; break; }
                }
            }
            if (focus == null) return;

            var s = focus.Simulation;
            b.Append("\n<b>").Append(selected ? "SELECTED PLANT: " : "FIRST PLANT: ").Append(focus.name).Append("</b>");
            if (!selected) b.Append("\n<color=#9aa>(click a plant in the Game view to select one)</color>");
            b.Append('\n');

            b.Append("Health: <b>").Append((s.Health01 * 100f).ToString("0.0")).Append(" %</b>  ")
                .Append(StateColor(s.State)).Append(s.State).Append("</color>\n");
            b.Append("Growth: <b>").Append((s.Growth01 * 100f).ToString("0.0")).Append(" %</b>  ").Append(s.Stage).Append('\n');
            b.Append("Soil moisture: <b>").Append((s.Water01 * 100f).ToString("0.0")).Append(" %</b>\n");
            b.Append("Stress: <b>").Append((s.Stress01 * 100f).ToString("0")).Append(" %</b>  (temp ")
                .Append((s.TemperatureStress01 * 100f).ToString("0")).Append(", humidity ")
                .Append((s.HumidityStress01 * 100f).ToString("0")).Append(", light ")
                .Append((s.LightStress01 * 100f).ToString("0")).Append(", water ")
                .Append((s.WaterStress01 * 100f).ToString("0")).Append(")\n");
            b.Append("Age: ").Append((s.AgeHours / 24f).ToString("0.0")).Append(" days");
        }

        private static void AppendRange(StringBuilder b, string label, float min, float avg, float max)
        {
            b.Append(label).Append(": min ").Append((min * 100f).ToString("0.0"))
                .Append("  avg <b>").Append((avg * 100f).ToString("0.0")).Append("</b>")
                .Append("  max ").Append((max * 100f).ToString("0.0")).Append(" %\n");
        }

        private static int CountNonZero(int[] values)
        {
            int n = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] > 0) n++;
            return n;
        }

        private static string StateColor(PlantHealthState state)
        {
            switch (state)
            {
                case PlantHealthState.Healthy: return "<color=#5e5>";
                case PlantHealthState.Stressed: return "<color=#fd4>";
                case PlantHealthState.Critical: return "<color=#f93>";
                default: return "<color=#f55>";
            }
        }

        // --- Drawing -------------------------------------------------------------

        private void OnGUI()
        {
            if (!visible) return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 15,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = Color.white }
                };
                backgroundTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                backgroundTexture.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.08f, 0.82f));
                backgroundTexture.Apply();
            }

            // Scale with the Game view height so it stays readable at any resolution.
            float scale = Mathf.Max(1f, Screen.height / 1000f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var content = new GUIContent(text);
            float width = PanelWidth;
            float height = labelStyle.CalcHeight(content, width - 16f) + 12f;
            var panel = new Rect(10f, 10f, width, height);

            GUI.DrawTexture(panel, backgroundTexture);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 6f, width - 16f, height - 12f), content, labelStyle);

            GUI.matrix = previous;
        }
    }
}
#endif
