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
        [SerializeField, Min(0f), Tooltip("Litres per simulated hour added from a mains supply. 0 = reservoir only (refill by hand).")]
        private float mainsSupplyLitresPerHour = 0f;

        [Header("Delivery")]
        [SerializeField, Min(0f), Tooltip("Total litres the sprinklers deliver per simulated hour.")]
        private float flowRateLitresPerHour = 60f;
        [SerializeField] private Mode mode = Mode.Auto;

        [Header("Auto Mode (soil moisture 0-1)")]
        [SerializeField, Range(0f, 1f), Tooltip("Start watering when any living plant falls below this.")]
        private float startBelowMoisture = 0.35f;
        [SerializeField, Range(0f, 1f), Tooltip("Stop watering once every living plant is at or above this.")]
        private float stopAboveMoisture = 0.75f;

        [Header("Species-aware watering (Auto mode)")]
        [SerializeField, Tooltip("When on, Auto mode waters each plant by its OWN species' ideal soil-moisture range instead of the two shared thresholds above, and never waters a plant past its own stop level. " +
            "Off keeps the original behaviour (used by the experiment pods and the regression baselines).")]
        private bool speciesAware;
        [SerializeField, Range(0f, 1f), Tooltip("A plant is watered once its soil falls below ideal minimum + this fraction of its ideal moisture range.")]
        private float startFractionOfIdealRange = 0.25f;
        [SerializeField, Range(0f, 1f), Tooltip("A plant stops being watered once it reaches ideal minimum + this fraction of its ideal moisture range.")]
        private float stopFractionOfIdealRange = 0.8f;

        [Header("Effects (optional)")]
        [SerializeField, Tooltip("Particle systems played while water is actually being delivered (not merely while the controller is on).")]
        private ParticleSystem[] sprayEffects = Array.Empty<ParticleSystem>();
        [SerializeField, Range(0f, 1f), Tooltip("The spray starts when the smoothed delivered flow reaches this fraction of the controller's maximum flow.")]
        private float sprayStartFraction = 0.25f;
        [SerializeField, Range(0f, 1f), Tooltip("The spray stops when the smoothed delivered flow falls below this fraction of the maximum flow (lower than the start fraction, so it does not flicker).")]
        private float sprayStopFraction = 0.10f;
        [SerializeField, Min(0.01f), Tooltip("Simulated hours over which the delivered flow is averaged for the spray, so single steps do not make it flicker.")]
        private float sprayBlendHours = 0.25f;

        private readonly List<PlantController> plants = new List<PlantController>();
        private float waterLitres;
        private bool isRunning;
        private bool autoWateringActive;
        private readonly HashSet<PlantController> wanting = new HashSet<PlantController>(); // species-aware: plants currently being watered

        /// <summary>Total litres delivered to plants since start (for monitoring).</summary>
        public float TotalDeliveredLitres { get; private set; }

        /// <summary>Total simulated hours the sprinklers have been running (for monitoring).</summary>
        public float RunningHours { get; private set; }

        public bool SpeciesAware => speciesAware;

        /// <summary>Smoothed share (0-1) of the controller's maximum flow that is actually being delivered to plants.</summary>
        public float DeliveryFraction01 { get; private set; }

        /// <summary>True while the spray effects are showing: real delivery is above the start fraction (and has not yet fallen below the stop fraction).</summary>
        public bool IsSpraying { get; private set; }

        private float lastDeliveredLitres;

        /// <summary>Simulated hours in which water was actually delivered (sprinklers can be "on" with nothing left to deliver).</summary>
        public float DeliveringHours { get; private set; }

        /// <summary>How many times a plant started being watered (species-aware mode). A very high count per plant would mean chattering.</summary>
        public int WateringStarts { get; private set; }

        /// <summary>Largest amount (soil moisture 0-1) any plant has ended up above its own stop level after a delivery (species-aware mode).</summary>
        public float MaxOvershoot { get; private set; }

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
            wanting.Remove(plant);
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

            if (mainsSupplyLitresPerHour > 0f) Refill(mainsSupplyLitresPerHour * deltaHours);

            lastDeliveredLitres = 0f;
            bool shouldRun = ShouldRun();
            if (shouldRun != isRunning) SetRunning(shouldRun);
            if (isRunning)
            {
                RunningHours += deltaHours;
                DeliverWater(deltaHours);
            }
            UpdateSpray(deltaHours);
        }

        // The spray follows the water actually delivered (a share of the maximum flow), not the on/off state. A controller can be "on" for long periods
        // while delivering a trickle, in which case nothing sprays. Emission scales with the delivered share.
        private void UpdateSpray(float deltaHours)
        {
            float capacity = flowRateLitresPerHour * deltaHours;
            float instant = capacity > 1e-9f ? Mathf.Clamp01(lastDeliveredLitres / capacity) : 0f;
            DeliveryFraction01 += (instant - DeliveryFraction01) * (1f - Mathf.Exp(-deltaHours / sprayBlendHours));

            bool spraying = isRunning && mode != Mode.Off && (IsSpraying ? DeliveryFraction01 >= sprayStopFraction : DeliveryFraction01 >= sprayStartFraction);
            if (spraying != IsSpraying) IsSpraying = spraying;
            ApplySprayEffects();
        }

        private void StopSpray()
        {
            DeliveryFraction01 = 0f;
            IsSpraying = false;
            ApplySprayEffects();
        }

        private void ApplySprayEffects()
        {
            if (sprayEffects == null) return;
            for (int i = 0; i < sprayEffects.Length; i++)
            {
                ParticleSystem effect = sprayEffects[i];
                if (effect == null) continue;

                if (IsSpraying)
                {
                    var emission = effect.emission;
                    emission.rateOverTimeMultiplier = Mathf.Lerp(0.35f, 1f, DeliveryFraction01); // gentle at low flow, full at maximum flow
                    if (!effect.isPlaying) effect.Play();
                }
                else if (effect.isPlaying) effect.Stop();
            }
        }

        // --- Internals -----------------------------------------------------------

        private bool ShouldRun()
        {
            if (mode == Mode.Off || waterLitres <= 0f) return false;

            if (mode == Mode.Manual) return true;

            if (speciesAware) return UpdateSpeciesWatering();

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

        /// <summary>Soil moisture below which a plant of this species starts being watered (from its ideal range).</summary>
        public float StartLevelFor(PlantController plant)
        {
            Vector2 ideal = plant.Data.IdealSoilMoisture01;
            return ideal.x + startFractionOfIdealRange * (ideal.y - ideal.x);
        }

        /// <summary>Soil moisture at which a plant of this species stops being watered (from its ideal range).</summary>
        public float StopLevelFor(PlantController plant)
        {
            Vector2 ideal = plant.Data.IdealSoilMoisture01;
            return ideal.x + stopFractionOfIdealRange * (ideal.y - ideal.x);
        }

        // Per-plant hysteresis: a plant joins the watering set below its start level and leaves it at its stop level.
        private bool UpdateSpeciesWatering()
        {
            for (int i = 0; i < plants.Count; i++)
            {
                PlantController plant = plants[i];
                if (plant == null || !plant.IsInitialized || !plant.Simulation.IsAlive) { if (plant != null) wanting.Remove(plant); continue; }

                float water = plant.Simulation.Water01;
                if (wanting.Contains(plant)) { if (water >= StopLevelFor(plant)) wanting.Remove(plant); }
                else if (water < StartLevelFor(plant)) { wanting.Add(plant); WateringStarts++; }
            }
            return wanting.Count > 0;
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

            if (speciesAware && mode == Mode.Auto) { DeliverSpeciesAware(budget, deltaHours); return; }

            // Equal split among living plants that are not already full, scaled by each plant's water-share weight.
            // The default weight 1 gives exactly budget / thirstyCount per plant, as before; a lower weight gives that plant
            // proportionally less without taking anything from, or giving anything to, the other plants. (If the weights sum to more
            // than the plant count, shares are scaled down so the budget is never exceeded.)
            int thirsty = 0;
            float totalWeight = 0f;
            for (int i = 0; i < plants.Count; i++)
            {
                if (!IsThirsty(plants[i])) continue;
                thirsty++;
                totalWeight += plants[i].WaterShareWeight;
            }
            if (thirsty == 0)
            {
                if (mode == Mode.Auto) autoWateringActive = false;
                return;
            }

            float scale = totalWeight > thirsty ? thirsty / totalWeight : 1f;
            float share = budget / thirsty;
            float used = 0f;
            for (int i = 0; i < plants.Count; i++)
            {
                if (IsThirsty(plants[i])) used += plants[i].ReceiveWater(share * plants[i].WaterShareWeight * scale);
            }

            waterLitres = Mathf.Max(0f, waterLitres - used);
            TotalDeliveredLitres += used;
            lastDeliveredLitres = used;
            if (waterLitres <= 0f) SetRunning(false);
        }

        // Equal split (scaled by each plant's share weight) among the plants that currently want water, each capped at its own stop level.
        private void DeliverSpeciesAware(float budget, float deltaHours)
        {
            float totalWeight = 0f; int count = 0;
            foreach (PlantController p in wanting) { if (p == null || p.WaterShareWeight <= 0f) continue; totalWeight += p.WaterShareWeight; count++; }
            if (count == 0) { autoWateringActive = false; return; }

            float used = 0f;
            foreach (PlantController p in wanting)
            {
                if (p == null || p.WaterShareWeight <= 0f || !p.Simulation.IsAlive) continue;
                float share = budget * p.WaterShareWeight / totalWeight;
                float room = Mathf.Max(0f, StopLevelFor(p) - p.Simulation.Water01) * p.Data.WaterCapacityLitres; // litres until its own stop level
                float intake = Mathf.Max(0.0001f, p.Simulation.WaterIntakeFactor);
                used += p.ReceiveWater(Mathf.Min(share, room / intake));
            }

            used = Mathf.Min(used, waterLitres);
            waterLitres -= used;
            TotalDeliveredLitres += used;
            lastDeliveredLitres = used;
            if (used > 1e-6f) DeliveringHours += deltaHours;
            foreach (PlantController p in wanting) if (p != null) MaxOvershoot = Mathf.Max(MaxOvershoot, p.Simulation.Water01 - StopLevelFor(p));
            if (waterLitres <= 0f) SetRunning(false);
        }

        private static bool IsThirsty(PlantController plant)
        {
            return plant != null && plant.IsInitialized && plant.Simulation.IsAlive && plant.Simulation.Water01 < 1f && plant.WaterShareWeight > 0f;
        }

        private void SetRunning(bool running)
        {
            bool changed = running != isRunning;
            isRunning = running;
            if (!running) StopSpray(); // sprinklers off: the spray stops at once

            if (changed) RunningChanged?.Invoke(isRunning);
        }

        private void OnValidate()
        {
            reservoirCapacityLitres = Mathf.Max(1f, reservoirCapacityLitres);
            startingWaterLitres = Mathf.Clamp(startingWaterLitres, 0f, reservoirCapacityLitres);
            stopAboveMoisture = Mathf.Max(stopAboveMoisture, startBelowMoisture);
            stopFractionOfIdealRange = Mathf.Max(stopFractionOfIdealRange, startFractionOfIdealRange);
            sprayStopFraction = Mathf.Min(sprayStopFraction, sprayStartFraction);
        }
    }
}
