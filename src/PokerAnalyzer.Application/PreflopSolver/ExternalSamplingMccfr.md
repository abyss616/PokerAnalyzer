# External Sampling MCCFR preflop trainer

## New traversal flow

Training now runs a recursive External Sampling MCCFR traversal from the current root state:

1. **Terminal / leaf node**: evaluate the traverser's utility and return it.
2. **Chance node**: sample one outcome and multiply the sampling reach by that outcome probability.
3. **Opponent node**: sample one action from the opponent policy and multiply both opponent reach and sampling reach by the sampled action probability.
4. **Traverser node**: enumerate every legal action, recurse on each child, compute the node value under the traverser policy, then:
   - update cumulative regret with `(opponentReach / samplingReach) * (actionValue - nodeValue)`
   - update cumulative average strategy with `(traverserReach / samplingReach) * policy(action)`

The trainer now threads an explicit immutable `MccfrTraversalContext` through recursion. Its fields are intentionally named by what they accumulate:

- `TraverserPlayerId`: the player whose counterfactual value is being updated
- `TraverserPolicyReach`: product of the traverser's own policy probabilities on the current path
- `OpponentPolicyReach`: product of the opponents' sampled policy probabilities on the current path
- `ExternalSamplingReach`: probability of the realized sampled chance/opponent prefix

## Old training path that is no longer used by the solver

The solver's main training constructor no longer updates regret by:

- sampling one complete trajectory first,
- replaying every visited traverser node afterward,
- evaluating all actions only for those replayed nodes,
- adding unweighted average-strategy mass directly from raw policy probabilities.

That trajectory replay remains only as a compatibility path for callers that still construct the trainer with a custom `IPreflopTrajectoryTraverser`, and it is no longer the solver's default regret-update flow.
