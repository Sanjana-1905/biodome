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
        }

        private void Update()
        {
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
