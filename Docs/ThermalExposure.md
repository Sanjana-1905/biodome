# Temperature stress and exposure duration (expected behaviour, written before implementation)

## Findings
* Greenhouse temperature (EnvironmentController): `mean + swing * sin((hour - 9)/24 * 2pi)`, coldest at 03:00, warmest at 15:00.
  Spring: 16 C mean, 6 C swing = **10 to 22 C**. Autumn 9 to 19 C. Summer 18 to 34 C. Winter 1 to 9 C.
* Banana: ideal 22-32 C, tolerance 8 C. Temperature stress is 0 only at 22 C and above (a moment each afternoon in spring),
  0.75 at the 16 C mean, and 1.0 at 14 C and below (about 11 hours of every spring night). So this is **sustained cold, all day**, not only at night.
* Health: the Stage 2 severe-stress term uses the INSTANTANEOUS temperature stress. It starts at 60 % of tolerance and rises to full at 100 %.
  There is no notion of exposure duration, so a short cold spell and weeks of cold are treated by the same per-hour rule, and a plant is damaged at 03:00
  even though it was within comfort 12 hours earlier.

## Model (scientifically motivated, no tolerance changed, death not disabled)
Chilling and heat injury in plants accumulates with the duration and depth of exposure beyond a threshold and recovers when conditions return.
`ThermalExposure01` is an exponentially weighted average of the temperature stress (stress is already scaled by the species' own tolerance), with the same
24 simulated-hour time constant as light acclimation, starting at 0 (the plant begins acclimated). The severe-damage term uses `ThermalExposure01`
instead of the instantaneous temperature stress. Everything else is unchanged: the averaged stress term, growth response and UI still use the instantaneous value.

    exposure += (temperatureStress - exposure) * (1 - exp(-dt / 24 h))     // step-size independent

## Expected behaviour
| Case (Monstera, tolerance 10 C unless stated) | Expected |
|---|---|
| 3 h at 5 C (stress 1.0) then ideal | exposure about 0.12, no severe damage, health stays about 1.0 |
| 12 h at 5 C then ideal | exposure about 0.39 (below the 0.6 start), no severe damage |
| 24 h continuous | exposure about 0.63, severe damage just starting |
| 72-120 h continuous at 5 C | steady health loss; death after several days (genuine sustained severe cold) |
| Banana, spring day/night cycle 10-22 C | exposure hovers near 0.6-0.8: small severe damage, growth strongly reduced, health roughly stable or slowly declining; no death in 17 days expected |
| Banana, winter-like 1-9 C | exposure about 1.0: deterioration and death, as it should |

The previous model (version 2) stays selectable as `HealthModel.SevereStress`; the new one is `HealthModel.SevereSustained`, with its own baseline `simulation_golden_scientific_v3.json`.

## Results (measured)
* Unit tests (`HealthWiltBloomTests`, 38 pass): Monstera at maximum cold stress, lowest health after 3 h = 1.000, after 12 h = 1.000, after 120 h = 0.37.
  Sustained 42 C: health 1.00 -> 0.79 (72 h) -> 0.15 (144 h); the original health model leaves it at 1.00.
* Banana, 17 days of the real spring cycle (10-22 C): survives with health 0.92 (thermal exposure about 0.70). The previous instantaneous model (v2) kills it.
  Banana in a 30-day winter cycle (1-9 C): dies, so death is not disabled. Summer (18-34 C): health 1.00.
* Untouched 17-day greenhouse in Play Mode (species-aware irrigation + this model): 1,088 of 1,088 greenhouse plants alive, every species at health 1.00 and water stress 0.00.
  The only deaths are the 6 plants of the water-stress pod (by experimental design).
* Baselines: original, scientific v1 and v2 files unchanged and passing; new `simulation_golden_scientific_v3.json` captured after validation.
