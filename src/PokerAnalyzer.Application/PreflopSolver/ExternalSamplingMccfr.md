# External-sampling MCCFR preflop trainer

This note describes the **solver-facing** training path in `PreflopRegretTrainer`. The older sampled-trajectory path still exists only as a compatibility/test path.

## Old behavior at a high level

The old path sampled one full trajectory, replayed the traverser's visited decisions, evaluated each sibling action from those replay points, then:

- used raw `actionValue - nodeValue` regret deltas, and
- added raw policy probability into the average-strategy store.

It did **not** keep explicit MCCFR reach terms, so those updates were not importance-weighted by the sampled prefix.

## New traversal flow

Each iteration:

1. Create the root state and choose one traverser player.
2. Recurse with `MccfrTraversalContext`.
3. At a **chance** node, sample one outcome and multiply `samplingReach` by that outcome probability.
4. At an **opponent / non-traverser** node, sample one action from the current policy and multiply both `opponentReach` and `samplingReach` by that action probability.
5. At a **traverser** node, enumerate **every** legal action, recurse on each child, compute the node value under the current traverser policy, then write regret and average-strategy deltas.
6. At a leaf, evaluate the traverser's utility.

Only opponent/chance branches are sampled. Traverser branches are fully enumerated.

## Reach bookkeeping

The traversal tracks three prefix probabilities:

- `traverserReach`: product of the traverser's own policy probabilities on the current prefix.
- `opponentReach`: product of the opponents' policy probabilities on the realized prefix.
- `samplingReach`: probability that external sampling produced the realized prefix.

In the current implementation:

- traverser nodes update `traverserReach`,
- opponent nodes update both `opponentReach` and `samplingReach`,
- chance nodes update `samplingReach` only.

## Regret update location and weighting

Regret is updated **only at traverser infosets**.

For policy `sigma`, child values `v(a)`, and node value

`v_sigma = sum_a sigma(a) * v(a)`

the raw regret delta stored for action `a` is

`delta_r(a) = (opponentReach / samplingReach) * (v(a) - v_sigma)`

The factor `opponentReach / samplingReach` is the external-sampling importance weight.

## CFR+ clipping semantics

The cumulative stored regret uses CFR+ clipping:

`R_plus(a) = max(0, R_plus(a) + delta_r(a))`

Clipping happens **after** all raw deltas targeting the same infoset/action entry have been summed. In parallel mode, worker deltas are merged first, then the shared regret store applies the single floor-at-zero update.

## Average-strategy weighting

Average strategy is accumulated only at traverser infosets with reach-weighted contribution

`delta_s(a) = traverserReach * sigma(a)`

The displayed average policy is the normalized version of those cumulative weights.

## Why training policy must stay separate from UI/display heuristics

Training uses strict regret-matching+ semantics:

- derive policy from **positive cumulative regrets only**;
- if all cumulative regrets are non-positive, fall back to **uniform**.

That keeps the training loop aligned with CFR+/MCCFR. UI or recommendation layers may use different display heuristics when all regrets are non-positive, such as action-value-based smoothing, but feeding those heuristics back into training would change the reach terms and regret targets the trainer is optimizing.
