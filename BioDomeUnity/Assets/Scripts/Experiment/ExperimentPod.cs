using System.Collections.Generic;
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using TMPro;
using UnityEngine;

namespace BioDome.Experiment
{
    /// <summary>
    /// One climate pod: a group of experimental plants under one <see cref="ZoneProfile"/>, with its own irrigation.
    /// Reads plant state for the comparison UI and drives the pod's live indicator sign. It never changes the simulation.
    /// </summary>
    public class ExperimentPod : MonoBehaviour
    {
        [SerializeField] private string displayName = "Pod";
        [SerializeField] private string description = "";
        [SerializeField] private Color accent = Color.white;
        [SerializeField] private ZoneProfile profile;
        [SerializeField] private List<PlantController> plants = new List<PlantController>();
        [SerializeField] private IrrigationController irrigation;
        [SerializeField] private TMP_Text indicatorText;

        public string DisplayName => displayName;
        public string Description => description;
        public Color Accent => accent;
        public ZoneProfile Profile => profile;
        public IReadOnlyList<PlantController> Plants => plants;
        public IrrigationController Irrigation => irrigation;

        private void Start()
        {
            // The irrigation controller's plant list is runtime-only, so each pod registers its own plants with its own controller.
            if (irrigation == null) return;
            for (int i = 0; i < plants.Count; i++) irrigation.RegisterPlant(plants[i]);
        }

        /// <summary>Average state of the pod's plants (dead plants count with their final values).</summary>
        public struct Metrics
        {
            public int Total, Alive;
            public float Growth, Health, Moisture, Stress;
        }

        public Metrics Measure()
        {
            var m = new Metrics();
            for (int i = 0; i < plants.Count; i++)
            {
                PlantController p = plants[i];
                if (p == null || !p.IsInitialized) continue;
                var s = p.Simulation;
                m.Total++;
                if (s.IsAlive) m.Alive++;
                m.Growth += s.Growth01;
                m.Health += s.Health01;
                m.Moisture += s.Water01;
                m.Stress += s.Stress01;
            }
            if (m.Total > 0)
            {
                m.Growth /= m.Total; m.Health /= m.Total; m.Moisture /= m.Total; m.Stress /= m.Total;
            }
            return m;
        }

        /// <summary>The conditions this pod's plants experience, given the greenhouse conditions.</summary>
        public EnvironmentConditions ConditionsFor(in EnvironmentConditions greenhouse)
        {
            return profile != null ? profile.Apply(greenhouse) : greenhouse;
        }

        private readonly StringBuilder text = new StringBuilder(96);

        public void RefreshIndicator(in EnvironmentConditions greenhouse)
        {
            if (indicatorText == null) return;
            EnvironmentConditions c = ConditionsFor(greenhouse);
            text.Clear();
            text.Append(c.TemperatureCelsius.ToString("0.0")).Append("°C  ·  ");
            text.Append(Mathf.RoundToInt(c.Humidity01 * 100f)).Append("% RH  ·  ");
            text.Append("light ").Append(Mathf.RoundToInt(c.Light01 * 100f)).Append("%");
            float share = profile != null ? profile.WaterShareWeight : 1f;
            if (share < 1f) text.Append("  ·  water ").Append(Mathf.RoundToInt(share * 100f)).Append("%");
            indicatorText.text = text.ToString();
        }
    }
}
