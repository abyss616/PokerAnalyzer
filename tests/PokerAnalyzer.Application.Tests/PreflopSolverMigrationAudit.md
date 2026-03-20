# Preflop solver MCCFR/CFR+ test audit

## Still valid as-is
- `TrainingRegretMatchingPolicyProviderTests.TryGetPolicy_AllZeroRegrets_ReturnsUniform` — training policy now intentionally falls back to a uniform strategy when all cumulative regrets are non-positive.
- `PreflopRegretTrainerTests.InMemoryRegretStore_CfrPlus*` — these store-level tests already assert CFR+ clipping semantics instead of the pre-migration additive heuristic.
- `ExternalSamplingMccfrTrainerTests.RunIteration_UpdatesRegretOnlyAtTraverserNodes` — still matches external-sampling MCCFR.

## Updated expectations / coverage
- Added focused external-sampling tests for:
  - traverser nodes enumerating every legal action,
  - opponent and chance nodes sampling a single branch,
  - regret weighting by `opponentReach / samplingReach`, and
  - average-strategy accumulation weighted by traverser reach.

## Removed old assumptions
- Removed legacy compatibility-path assertions that treated solver-training regret updates as raw `actionValue - nodeValue` without MCCFR reach weighting.
- Removed legacy compatibility-path assertions that accumulated average strategy directly from raw policy probabilities across iterations.
- Removed legacy compatibility-path assertions that anchored migration-sensitive expectations to partially specified policy dictionaries rather than the external-sampling traversal used by the main solver path.
