# Stage 4: experimental simulation and comparison

Measured in Play Mode on the untouched `BioDome_Working` scene (30 Monstera, 6 per pod, start growth 0.60, health 1.00, soil 0.60), 8 simulated h/s.
Raw data: `Docs/Verification/experiment_daily.csv` (every day), `experiment_observation.log`. Stress columns are time-weighted means over the day just ended.

| Day | Group | Alive | Growth | Health | Soil | Stress | T / H / L / W stress |
|---|---|---|---|---|---|---|---|
| 1 | all five groups | 6/6 | 0.600 | 1.000 | 0.60 | 0.00 | identical |
| 7 | Control | 6/6 | 0.785 | 1.000 | 0.55 | 0.12 | .30 / .00 / .28 / .00 |
| 7 | Heat +10 C | 6/6 | 0.820 | 1.000 | 0.56 | 0.07 | .07 / .00 / .28 / .00 |
| 7 | Low humidity -0.30 | 6/6 | 0.746 | 1.000 | 0.54 | 0.19 | .30 / .37 / .28 / .00 |
| 7 | Low light x0.40 | 6/6 | 0.778 | 1.000 | 0.60 | 0.12 | .30 / .00 / .30 / .00 |
| 7 | Water stress | 6/6 | 0.785 | 1.000 | 0.56 | 0.12 | .30 / .00 / .28 / .00 |
| 14 | Control | 6/6 | 1.000 | 1.000 | 0.66 | 0.09 | .12 / .03 / .30 / .00 |
| 14 | Heat | 6/6 | 1.000 | 1.000 | 0.68 | 0.18 | .58 / .03 / .30 / .00 |
| 14 | Low humidity | 6/6 | 0.904 | 0.952 | 0.69 | 0.21 | .12 / .62 / .30 / .00 |
| 14 | Low light | 6/6 | 0.990 | 1.000 | 0.49 | 0.08 | .12 / .03 / .24 / .00 |
| 14 | Water stress | 6/6 | 1.000 | 1.000 | 0.44 | 0.09 | .12 / .03 / .30 / .00 |
| 30 | Control | 6/6 | 1.000 | 1.000 | 0.49 | 0.14 | .42 / .00 / .30 / .00 |
| 30 | Heat | 6/6 | 1.000 | 1.000 | 0.45 | 0.06 | .00 / .00 / .30 / .00 |
| 30 | Low humidity | 6/6 | 1.000 | 1.000 | 0.64 | 0.19 | .42 / .25 / .30 / .00 |
| 30 | Low light | 6/6 | 1.000 | 1.000 | 0.62 | 0.15 | .42 / .00 / .34 / .00 |
| 30 | Water stress | 6/6 | 1.000 | 1.000 | 0.54 | 0.14 | .42 / .00 / .30 / .00 |

Water-stress pod between the sample days (daily log): soil 0.50 on day 13, 0.16 on day 19, health low of 0.52 on day 22, then back to 1.00 by day 28 as the pod's own
controller catches up (all 6 alive throughout).

## Findings
* Equivalence: all 30 plants start with identical data, growth, health, water, age, state and stage before the first tick (checked at Start, before any tick).
* Isolation: every pod plant carries only its own pod's profile; no zone-profile plant is outside a pod (0 leaks); display-plant and control bit-identity tests pass.
* Heat (+10 C) is warmer than a spring greenhouse needs: it grows fastest early (0.820 vs 0.785 on day 7) and carries more temperature stress only when the greenhouse warms (day 14, 0.58 vs 0.12). It is not a "worse" group by construction.
* Low humidity is clearly worse: growth 0.904 and health 0.952 on day 14 against 1.000 for control.
* Low light grows slightly slower early (0.778 vs 0.785 on day 7, 0.990 vs 1.000 on day 14) and catches up.
* Water stress needed calibration, measured over 30 days: share 0.25 killed all six plants by day 30 (soil 0 by day 14); 0.35 killed them by day 19; 0.5 produced
  no stress. 0.4 gives visible drought stress and recovery with no deaths. The request names "water intake 0.50"; the existing mechanism is the irrigation
  share weight (the `WaterIntakeFactor` stays 1), so the share weight was set to 0.40 (preset, asset and the six scene overrides).
  Note the response to the share is steep (a cliff between 0.35 and 0.40) because the pod controllers use the original fixed thresholds.

## Dashboard
Comparison panel now shows the simulation day, clock, season and greenhouse temperature; growth/health/soil/stress bars; growth and health difference from control
(percentage points); and the four stress sources per group.
