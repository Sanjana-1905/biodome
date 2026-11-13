using System;
using System.Collections.Generic;
using BioDome.Plants;
using UnityEngine;

namespace BioDome.Irrigation
{
    /// <summary>
    /// Water reservoir and sprinkler logic. Delivers water to registered plants and drives the
    /// spray particle effects. In Auto mode it waters with hysteresis (start below one moisture
    /// level, stop above another). It advances only when the GreenhouseManager calls <see cref="Tick"/>.
    /// </summary>
    public class IrrigationController : MonoBehaviour
    {
        public enum Mode
        {
            Off = 0,
            Manual = 1,
            Auto = 2
        }

        [Header("Reservoir")]
        [SerializeField, Min(1f)] private float reservoirCapacityLitres = 500f;
        [SerializeField, Min(0f)] private float startingWaterLitres = 500f;

        [Header("Delivery")]
        [SerializeField, Min(0f), Tooltip("Total litres the sprinklers deliver per simulated hour.")]
        private float flowRateLitresPerHour = 60f;
        [SerializeField] private Mode mode = Mode.Auto;

        [Header("Auto Mode (soil moisture 0-1)")]
        [SerializeField, Range(0f, 1f), Tooltip("Start watering when any living plant falls below this.")]
        private float startBelowMoisture = 0.35f;
        [SerializeField, Range(0f, 1f), Tooltip("Stop watering once every living plant is at or above this.")]
        private float stopAboveMoisture = 0.75f;

        [Header("Effects (optional)")]
        [SerializeField, Tooltip("Particle systems played while water is flowing.")]
        private ParticleSystem[] sprayEffects = Array.Empty<ParticleSystem>();

        private readonly List<PlantController> plants = new List<PlantController>();
        private float waterLitres;
        private bool isRunning;
        private bool autoWateringActive;

        /// <summary>Raised when sprinklers start or stop.</summary>
        public event Action<bool> RunningChanged;

        public Mode CurrentMode => mode;
        public bool IsRunning => isRunning;
        public float WaterLitres => waterLitres;
        public float ReservoirCapacityLitres => reservoirCapacityLitres;
        public float Reservoir01 => reservoirCapacityLitres > 0f ? waterLitres / reservoirCapacityLitres : 0f;

        private void Awake()
        {
            waterLitres = Mathf.Clamp(startingWaterLitres, 0f, reservoirCapacityLitres);
            SetRunning(false);
        }

        public void RegisterPlant(PlantController plant)
        {
            if (plant == null || plants.Contains(plant)) return;
            plants.Add(plant);
        }

        public void UnregisterPlant(PlantController plant)
        {
            if (plant == null) return;
            plants.Remove(plant);
        }

        public void SetMode(Mode newMode)
        {
            mode = newMode;
            autoWateringActive = false;
            if (mode == Mode.Off) SetRunning(false);
        }

        /// <summary>Convenience for a UI toggle: true = Auto, false = Off.</summary>
        public void SetAutoEnabled(bool enabled)
        {
            SetMode(enabled ? Mode.Auto : Mode.Off);
        }

        public void Refill(float litres)
        {
            if (litres <= 0f) return;
            waterLitres = Mathf.Min(reservoirCapacityLitres, waterLitres + litres);
        }

        public void RefillFull()
        {
            waterLitres = reservoirCapacityLitres;
        }

        /// <summary>Advance irrigation by <paramref name="deltaHours"/> simulated hours.</summary>
        public void Tick(float deltaHours)
        {
            if (deltaHours <= 0f) return;

            bool shouldRun = ShouldRun();
            if (shouldRun != isRunning) SetRunning(shouldRun);
            if (!isRunning) return;

            DeliverWater(deltaHours);
        }

        // --- Internals -----------------------------------------------------------

        private bool ShouldRun()
        {
            if (mode == Mode.Off || waterLitres <= 0f) return false;

            if (mode == Mode.Manual) return true;

            // Auto: hysteresis so sprinklers do not flicker around a single threshold.
            float lowest = LowestLivingMoisture();
            if (float.IsNaN(lowest)) return false; // no living plants

            if (autoWateringActive)
            {
                if (lowest >= stopAboveMoisture) autoWateringActive = false;
            }
            else if (lowest < startBelowMoisture)
            {
                autoWateringActive = true;
            }

            return autoWateringActive;
        }

        private float LowestLivingMoisture()
        {
            float lowest = float.NaN;
            for (int i = 0; i < plants.Count; i++)
            {
                PlantController plant = plants[i];
                if (plant == null || !plant.IsInitialized || !plant.Simulation.IsAlive) continue;

                float water = plant.Simulation.Water01;
                if (float.IsNaN(lowest) || water < lowest) lowest = water;
            }
            return lowest;
        }

        private void DeliverWater(float deltaHours)
        {
            float budget = Mathf.Min(flowRateLitresPerHour * deltaHours, waterLitres);
            if (budget <= 0f)
            {
                SetRunning(false);
                return;
            }

            // Share the budget among living plants that are not already full.
            int thirsty = 0;
            for (int i = 0; i < plants.Count; i++)
            {
                if (IsThirsty(plants[i])) thirsty++;
            }
            if (thirsty == 0)
            {
                if (mode == Mode.Auto) autoWateringActive = false;
                return;
            }

            float share = budget / thirsty;
            float used = 0f;
            for (int i = 0; i < plants.Count; i++)
            {
                if (IsThirsty(plants[i])) used += plants[i].ReceiveWater(share);
            }

            waterLitres = Mathf.Max(0f, waterLitres - used);
            if (waterLitres <= 0f) SetRunning(false);
        }

        private static bool IsThirsty(PlantController plant)
        {
            return plant != null && plant.IsInitialized && plant.Simulation.IsAlive && plant.Simulation.Water01 < 1f;
        }

        private void SetRunning(bool running)
        {
            bool changed = running != isRunning;
            isRunning = running;

            if (sprayEffects != null)
            {
                for (int i = 0; i < sprayEffects.Length; i++)
                {
                    ParticleSystem effect = sprayEffects[i];
                    if (effect == null) continue;

                    if (running && !effect.isPlaying) effect.Play();
                    else if (!running && effect.isPlaying) effect.Stop();
                }
            }

            if (changed) RunningChanged?.Invoke(isRunning);
        }

        private void OnValidate()
        {
            reservoirCapacityLitres = Mathf.Max(1f, reservoirCapacityLitres);
            startingWaterLitres = Mathf.Clamp(startingWaterLitres, 0f, reservoirCapacityLitres);
            stopAboveMoisture = Mathf.Max(stopAboveMoisture, startBelowMoisture);
        }
    }
}
