# Simulation pacing

## Cause of the "plants grow in 2 seconds" effect (measured in real Play Mode)
`Docs/Verification/pacing_default.csv` (before) vs `pacing_fixed_1x.csv` (after), produced by `PlayModePacingProbe` on `BioDome_Working`.

* The clock is correct: 0.4 simulated hours per real second (sim time 10.008 h -> 129.7 h after 300 s, exactly +0.4 h/s). Growth is applied once per step.
* Simulated growth is slow: Monstera seedlings gain about +0.10 growth per real 5 minutes.
* The visual was wrong, not the simulation. Plants start with the mesh saved in the scene, which does not match their starting growth
  (frame 1: Orchid showed 0.45 growth while simulating 0.78; Calathea 0.78 vs 0.91; Banana 0.83 vs 0.93). The correct meshes were then built a few per
  frame, so over the first 1-2 s every plant visibly "grew" to its real start size.

## Fix
`GreenhouseManager.PrimeVisuals()` builds the 39 distinct start-growth meshes before the first frame (one-time cost about 335-363 ms at Play start).
After the fix, displayed growth equals simulated growth from frame 1 for all six species.

## Pace
1x = 0.4 simulated hours per real second (one simulated day per real minute), unchanged. Species still differ: e.g. over 300 s at 1x Monstera seedlings go 0.153 -> 0.255.
New `SimulationSpeedBar` (Pause, 1x, 2x, 5x, 10x) calls `GreenhouseManager.SetSpeedMultiplier`; environment, irrigation and plants share the one clock.
10x for 30 s gives the same growth as 1x for 300 s (0.2545 vs 0.2546 for Monstera), confirming speed and frame-rate independence.
