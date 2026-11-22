# Species-aware irrigation (Stage 2.5)

## Problem (measured, `Docs/Verification/survival_legacy.log`)
Every irrigation controller (18 bed/planter groups + main + 5 pods) used two fixed thresholds (start below 0.4, stop when the LOWEST plant reaches 0.7)
and delivered water to every plant below 100 % moisture. Species that want drier soil were pushed to saturation and died of over-watering
(dead plants had soil moisture 0.96-0.99 and water stress 0.8-1.0). Over 17 simulated days: 666 of 1,088 greenhouse plants survived (this also happened with the original health model).

## Fix (`IrrigationController`, opt-in `speciesAware`)
* A plant starts being watered when its soil moisture is below `ideal min + 0.25 x (ideal max - ideal min)` of ITS species (`PlantData.IdealSoilMoisture01`).
* It stops at `ideal min + 0.8 x range`. Per-plant hysteresis; delivery is shared only among plants currently wanting water and capped at each plant's own stop level.
* Enabled on the 18 non-pod controllers in `BioDome_Working`. The 5 pod controllers keep the original behaviour (the water-stress pod stays water-stressed).
* Default is OFF in code, so the original, scientific v1 and scientific v2 baselines are untouched and still pass.

## Result (17+ simulated days, untouched greenhouse, `survival_*.log`)
| | Original irrigation | Species-aware |
|---|---|---|
| Greenhouse plants alive | 666 / 1,088 | 1,067 / 1,088 |
| Pod plants alive | 24 / 30 | 24 / 30 |
| Deaths | moss 207, calathea 67, orchid 58, bird of paradise 26, anthurium 24, banana 21, areca 15, monstera 4 | banana 21 |
| Water stress of survivors | 0.00-0.35 | 0.00 for every species |
| Litres delivered / sprinkler-hours (sum over controllers) | 10,545 / 2,480 | 12,748 / 9,271 |

Remaining deaths: the 21 Bananas die of night cold (temperature stress 1.0; ideal 22-32 C), a genuine consequence of the Stage 2 severe-stress health model
in a spring greenhouse. The water-stress pod's 6 plants die by experimental design.

## Efficiency check (17+ simulated days, `Docs/Verification/final_species.log`, `final_legacy.log`)
* No overshoot: largest amount any plant ended above its own stop level = 0.000 on every controller.
* No chattering: 14-22 watering starts per single-species bed over 19 days, 42-96 for the mixed groups, 448 for the main controller (about 3 per plant).
* Accounting is exact: reservoir litres taken = litres gained by plants = the controller's counter (unit test).
* Every species-aware controller shows sprinklers "on" for all 455 h, but they deliver a small fraction of their flow (for example Bed_Orchid 110 L of 1,365 L possible):
  flow is sized close to the beds' consumption, so a trickle is needed nearly continuously; the cap per plant stops any surplus being used.
  The "sprinkler-hours" figure therefore overstates activity; "litres delivered" is the true measure.
* Litres per surviving greenhouse plant over the run: original 10,918 / 466 = 23 L; species-aware 13,473 / 1,088 = 12 L.
* Side effect to be aware of: the spray particle effects play whenever the controller is "on", so in species-aware beds they are on almost constantly.
