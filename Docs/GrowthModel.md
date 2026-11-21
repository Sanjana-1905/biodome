# BioDome growth model

## Versions
| Model | Where | Baseline |
|---|---|---|
| Original (v0) | `PlantSimulation.GrowthModel.Original` | `Assets/Tests/Golden/simulation_golden.json` (unchanged, still passes) |
| Scientific (v1, default in the game) | `PlantSimulation.GrowthModel.Scientific` | `Assets/Tests/Golden/simulation_golden_scientific_v1.json` (new) |

Run the original regression: `-executeMethod BioDome.EditorTools.Tests.SimulationRegression.RunBatch -goldenMode compare`
(default golden file = original baseline, model = Original).
Run the scientific one: add `-goldenFile Assets/Tests/Golden/simulation_golden_scientific_v1.json` (model = Scientific automatically).

## Scientific model
Per factor i in {temperature, humidity, light, soil moisture}:

    stress_i   = distance beyond the species' ideal range / the species' configured tolerance, clamped to 0..1   (unchanged definition)
    response_i = 1 - stress_i^2

    growth rate = (1 / daysToMaturity) * seasonMultiplier * Π response_i * health

* A factor can stop growth alone at its configured tolerance limit (stress 1 gives response 0); mild deviation costs little (quadratic).
* Factors multiply, so independent limitations compound (heat + drought is worse than either).
* Light uses a running average with a 24 simulated-hour time constant (`AcclimatedLight01`), so the natural day/night cycle is averaged
  instead of halting growth every night. Sustained shade still lowers growth. The average is `exp(-dt/24)`-based, so it is step-size independent.
* Nothing is hard-coded per species: all response comes from the species asset (ideal ranges, tolerances, soil range, days to maturity, seasons).

Health, water use and the displayed `Stress01` are unchanged from the original model.

## Why outputs differ from the original baseline (intentional)
The original averaged four stresses (water double), so any single factor could cost at most 20 % of growth: Monstera at 42 C still grew at 80 %.
Under the scientific model that case is 0 %. All differences in `simulation_golden_scientific_v1.json` come from this one change (plus light acclimation).

## Expected growth factor (1.00 = ideal), checked by `GrowthStageTests` against the asset values
| Species | 15 C | 36 C | 42 C | dry air (H 0.15) | dark | drought (soil 0.1) | 36 C + drought |
|---|---|---|---|---|---|---|---|
| Monstera | 0.91 | 0.51 | 0.00 | 0.23 | 0.75 | 0.49 | 0.25 |
| Banana | 0.23 | 0.75 | 0.00 | 0.00 | 0.00 | 0.36 | 0.27 |
| Bird of Paradise | 0.91 | 0.84 | 0.00 | 0.61 | 0.19 | 0.55 | 0.46 |
| Calathea | 0.91 | 0.36 | 0.00 | 0.00 | 0.91 | 0.39 | 0.14 |
| Anthurium | 0.91 | 0.51 | 0.00 | 0.00 | 0.64 | 0.43 | 0.22 |
| Orchid | 0.91 | 0.36 | 0.00 | 0.23 | 0.75 | 0.74 | 0.27 |
