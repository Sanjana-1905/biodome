using System.Collections.Generic;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Plants;
using BioDome.Simulation;
using UnityEngine;

namespace BioDome.Experiment
{
    /// <summary>
    /// Groups the five climate pods. Refreshes their indicators and, once at start, checks that every experimental plant began
    /// from an equivalent simulation state (the check result is exposed to the UI and logged).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class ExperimentGarden : MonoBehaviour
    {
        [SerializeField] private EnvironmentController environment;
        [SerializeField] private List<ExperimentPod> pods = new List<ExperimentPod>();
        [SerializeField, Min(0.05f)] private float indicatorInterval = 0.25f;

        private float timer;
        private int lastRecordedDay = -1;
        private float lastHours = -1f, accumulatedHours;
        private ExperimentPod.Metrics[] stressSum;
        private readonly List<Snapshot> history = new List<Snapshot>();

        /// <summary>One row of the experiment log: every pod's average state at the start of a simulated day.</summary>
        public sealed class Snapshot
        {
            public int Day;          // 1-based simulation day
            public float Hours;      // total simulated hours since the greenhouse clock started
            public ExperimentPod.Metrics[] Pods;
        }

        public IReadOnlyList<Snapshot> History => history;
        public int CurrentDay => environment != null ? environment.Current.DayCount + 1 : 1;
        public float CurrentTimeOfDay => environment != null ? environment.Current.TimeOfDayHours : 0f;
        public EnvironmentConditions GreenhouseNow => environment != null ? environment.Current : default;

        /// <summary>Index of the control pod (the pod with an identity profile), or 0.</summary>
        public int ControlIndex
        {
            get
            {
                for (int i = 0; i < pods.Count; i++) if (pods[i] != null && pods[i].Profile != null && pods[i].Profile.Condition == ExperimentalCondition.Control) return i;
                return 0;
            }
        }

        /// <summary>The snapshot recorded at the start of <paramref name="day"/> (1-based), or null if that day has not been reached.</summary>
        public Snapshot SnapshotOfDay(int day)
        {
            for (int i = 0; i < history.Count; i++) if (history[i].Day == day) return history[i];
            return null;
        }

        /// <summary>True when every pod's plants carry only their own pod's profile.</summary>
        public bool ConditionsIsolated()
        {
            foreach (ExperimentPod pod in pods) if (pod != null && !pod.PlantsUseOwnProfile()) return false;
            return true;
        }

        private void Record()
        {
            if (environment == null) return;
            EnvironmentConditions c = environment.Current;
            if (c.DayCount == lastRecordedDay) return;
            lastRecordedDay = c.DayCount;
            var snap = new Snapshot { Day = c.DayCount + 1, Hours = c.DayCount * 24f + c.TimeOfDayHours, Pods = new ExperimentPod.Metrics[pods.Count] };
            for (int i = 0; i < pods.Count; i++)
            {
                if (pods[i] == null) continue;
                ExperimentPod.Metrics m = pods[i].Measure(); // growth, health, moisture: the state now
                if (stressSum != null && accumulatedHours > 0.01f)
                {
                    // Stress swings with the time of day (a midnight reading has no light), so stress is reported as the mean over the day just ended.
                    ExperimentPod.Metrics s = stressSum[i];
                    m.Stress = s.Stress / accumulatedHours; m.TemperatureStress = s.TemperatureStress / accumulatedHours;
                    m.HumidityStress = s.HumidityStress / accumulatedHours; m.LightStress = s.LightStress / accumulatedHours; m.WaterStress = s.WaterStress / accumulatedHours;
                }
                snap.Pods[i] = m;
            }
            history.Add(snap);
            stressSum = new ExperimentPod.Metrics[pods.Count];
            accumulatedHours = 0f;
        }

        // Time-weighted accumulation of stress (weights are simulated hours, so the means do not depend on frame rate).
        private void Accumulate()
        {
            if (environment == null) return;
            EnvironmentConditions c = environment.Current;
            float now = c.DayCount * 24f + c.TimeOfDayHours;
            if (lastHours >= 0f && now > lastHours && stressSum != null)
            {
                float dt = now - lastHours;
                for (int i = 0; i < pods.Count; i++)
                {
                    if (pods[i] == null) continue;
                    ExperimentPod.Metrics m = pods[i].Measure();
                    stressSum[i].Stress += m.Stress * dt; stressSum[i].TemperatureStress += m.TemperatureStress * dt; stressSum[i].HumidityStress += m.HumidityStress * dt;
                    stressSum[i].LightStress += m.LightStress * dt; stressSum[i].WaterStress += m.WaterStress * dt;
                }
                accumulatedHours += dt;
            }
            lastHours = now;
        }
        private bool checkedStart;

        public IReadOnlyList<ExperimentPod> Pods => pods;
        public bool StartChecked => checkedStart;
        public bool StartsEquivalent { get; private set; }
        public string EquivalenceReport { get; private set; } = "";

        private void Start()
        {
            // Runs after GreenhouseManager.Start (which plants every plant from the shared cohort; this component has a later execution
            // order) and before the first Update, so no simulation tick has happened yet. Checking later compares plants that have
            // already lived one tick under different pod climates, which is not an initial-state comparison.
            CheckEquivalentStart();
            stressSum = new ExperimentPod.Metrics[pods.Count];
            Record(); // day 1, before any tick
            // Live pod test panel ("Pod Lab"), built at run time so the scene file is untouched.
            if (Application.isPlaying && GetComponent<PodTestPanel>() == null) gameObject.AddComponent<PodTestPanel>().Init(this);
        }

        private void Update()
        {
            Accumulate();
            Record();
            timer += Time.unscaledDeltaTime;
            if (timer < indicatorInterval || environment == null) return;
            timer = 0f;
            EnvironmentConditions now = environment.Current;
            for (int i = 0; i < pods.Count; i++) if (pods[i] != null) pods[i].RefreshIndicator(now);
        }

        private void CheckEquivalentStart()
        {
            checkedStart = true;
            var sims = new List<PlantSimulation>();
            foreach (ExperimentPod pod in pods)
            {
                if (pod == null) continue;
                foreach (PlantController p in pod.Plants) if (p != null && p.IsInitialized) sims.Add(p.Simulation);
            }
            List<string> problems = ExperimentEquivalence.Differences(sims);
            StartsEquivalent = sims.Count > 0 && problems.Count == 0;
            EquivalenceReport = StartsEquivalent
                ? $"All {sims.Count} experimental plants started from an equivalent state."
                : $"Experimental plants did NOT start equivalent: {string.Join("; ", problems)}";
            if (StartsEquivalent) Debug.Log("ExperimentGarden: " + EquivalenceReport, this);
            else Debug.LogWarning("ExperimentGarden: " + EquivalenceReport, this);
        }
    }
}
