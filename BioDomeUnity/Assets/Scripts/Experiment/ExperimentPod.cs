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

        private ZoneProfile sharedProfile;

        /// <summary>The profile asset this pod was built with (never modified at run time).</summary>
        public ZoneProfile OriginalProfile => sharedProfile != null ? sharedProfile : profile;

        private void Awake()
        {
            // Give this pod its OWN runtime copy of its profile so live changes (test panel) affect only this pod and never write to the
            // shared asset on disk. The copy starts with identical values, so nothing changes until someone edits it.
            if (profile == null) return;
            sharedProfile = profile;
            profile = Instantiate(sharedProfile);
            profile.name = sharedProfile.name + " (live)";
            profile.hideFlags = HideFlags.DontSave;
            for (int i = 0; i < plants.Count; i++)
                if (plants[i] != null && plants[i].ZoneProfile == sharedProfile) plants[i].SetZoneProfileLive(profile);
        }

        /// <summary>Changes this pod's live conditions (offsets on top of the greenhouse climate, light multiplier, irrigation share). Only this pod's plants are affected.</summary>
        public void SetLiveConditions(float temperatureOffset, float humidityOffset, float lightMultiplier, float waterShare)
        {
            if (profile == null) return;
            profile.Configure(profile.DisplayName, profile.Condition, temperatureOffset, humidityOffset, lightMultiplier, profile.WaterIntakeFactor, waterShare);
            for (int i = 0; i < plants.Count; i++) if (plants[i] != null) plants[i].ApplyProfileWaterSettings();
        }

        /// <summary>Restores the pod's original experimental conditions.</summary>
        public void ResetLiveConditions()
        {
            ZoneProfile o = OriginalProfile;
            if (o != null) SetLiveConditions(o.TemperatureOffsetCelsius, o.HumidityOffset, o.LightMultiplier, o.WaterShareWeight);
        }

        private void OnDestroy()
        {
            if (sharedProfile != null && profile != null && profile != sharedProfile) Destroy(profile);
        }

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
            public float TemperatureStress, HumidityStress, LightStress, WaterStress;
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
                m.TemperatureStress += s.TemperatureStress01; m.HumidityStress += s.HumidityStress01;
                m.LightStress += s.LightStress01; m.WaterStress += s.WaterStress01;
            }
            if (m.Total > 0)
            {
                m.Growth /= m.Total; m.Health /= m.Total; m.Moisture /= m.Total; m.Stress /= m.Total;
                m.TemperatureStress /= m.Total; m.HumidityStress /= m.Total; m.LightStress /= m.Total; m.WaterStress /= m.Total;
            }
            return m;
        }

        /// <summary>True when every plant in this pod carries exactly this pod's profile (no condition leaks between pods or to display plants).</summary>
        public bool PlantsUseOwnProfile()
        {
            for (int i = 0; i < plants.Count; i++) if (plants[i] != null && plants[i].ZoneProfile != profile) return false;
            return true;
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
