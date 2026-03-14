# BB Option vs Limp(s) Fix — Actual Code Changes

This document records the concrete code changes introduced to fix the preflop BB-option-vs-limp crash.

## 1) `SolverLegalActionGenerator` changes

**File:** `src/PokerAnalyzer.Domain/Game/SolverLegalActionGenerator.cs`

### Added BB-option-vs-limp branch before generic `toCall == 0` handling

```csharp
if (IsBigBlindOptionVsLimpPreflopSpot(state, acting))
{
    actions.Add(new LegalAction(ActionType.Check));

    var minTotalBetFacingLimp = state.CurrentBetSize + state.LastRaiseSize;
    var raiseToFivePointFiveBb = ResolveFacingLimpRaiseFivePointFiveBb(state.Config.BigBlind);
    var raiseToNineBb = ResolveFacingLimpRaiseNineBb(state.Config.BigBlind);

    TryAddRaiseTarget(actions, raiseToFivePointFiveBb, minTotalBetFacingLimp, maxTotalBet);
    TryAddRaiseTarget(actions, raiseToNineBb, minTotalBetFacingLimp, maxTotalBet);

    return actions.AsReadOnly();
}
```

### Added BB-option-vs-limp detector

```csharp
private static bool IsBigBlindOptionVsLimpPreflopSpot(SolverHandState state, SolverPlayerState acting)
{
    if (state.Street != Street.Preflop)
        return false;

    if (acting.Position != Position.BB)
        return false;

    if (state.ToCall.Value != 0)
        return false;

    if (acting.CurrentStreetContribution != state.CurrentBetSize)
        return false;

    var hasPriorAggressiveAction = state.ActionHistory.Any(a =>
        a.ActionType == ActionType.Bet ||
        a.ActionType == ActionType.Raise ||
        a.ActionType == ActionType.AllIn);

    if (hasPriorAggressiveAction)
        return false;

    return state.ActionHistory.Any(a => a.ActionType == ActionType.Call);
}
```

---

## 2) `SolverStateStepper` changes

**File:** `src/PokerAnalyzer.Domain/Game/SolverStateStepper.cs`

### Narrowly allowed raise with `ToCall == 0` for BB-option-vs-limp only

```csharp
case ActionType.Raise:
{
    var toCall = state.ToCall;
    var isBigBlindOptionVsLimp = IsBigBlindOptionVsLimpPreflopSpot(state, acting);
    if (toCall.Value <= 0 && !isBigBlindOptionVsLimp)
        throw new InvalidOperationException($"Player {acting.PlayerId} cannot raise when there is no outstanding bet.");

    var toAmount = RequireTargetAmount(action, acting, "Raise");
    ...
}
```

### Added matching BB-option-vs-limp detector

```csharp
private static bool IsBigBlindOptionVsLimpPreflopSpot(SolverHandState state, SolverPlayerState acting)
{
    if (state.Street != Street.Preflop)
        return false;

    if (acting.Position != Position.BB)
        return false;

    if (state.ToCall.Value != 0)
        return false;

    if (acting.CurrentStreetContribution != state.CurrentBetSize)
        return false;

    var hasAggressivePreflopAction = state.ActionHistory.Any(a =>
        a.ActionType == ActionType.Bet ||
        a.ActionType == ActionType.Raise ||
        a.ActionType == ActionType.AllIn);

    if (hasAggressivePreflopAction)
        return false;

    return state.ActionHistory.Any(a => a.ActionType == ActionType.Call);
}
```

---

## 3) `SolverHandState.ValidateActionHistoryConsistency()` changes

**File:** `src/PokerAnalyzer.Domain/Game/SolverHandState.cs`

### Refined raise validation

```csharp
case ActionType.Raise:
    var isBigBlindOptionVsLimpRaise = IsBigBlindOptionVsLimpRaiseAction(i, action, playerState, currentBet);
    if (toCall.Value == 0 && !isBigBlindOptionVsLimpRaise)
        throw new InvalidOperationException($"Action history inconsistency: player {action.PlayerId} raised when no bet was outstanding.");
    ValidateAggressiveAmount(action, playerState.Contribution, "Raise");
    ...
```

### Added helper for narrow historical exception

```csharp
private bool IsBigBlindOptionVsLimpRaiseAction(
    int actionIndex,
    SolverActionEntry action,
    ActionValidationState playerState,
    ChipAmount currentBet)
{
    if (Street != Street.Preflop)
        return false;

    var actor = Players.FirstOrDefault(p => p.PlayerId == action.PlayerId);
    if (actor is null || actor.Position != Position.BB)
        return false;

    if (playerState.Contribution != currentBet)
        return false;

    var hasAggressiveAction = ActionHistory.Take(actionIndex).Any(a =>
        a.ActionType == ActionType.Bet ||
        a.ActionType == ActionType.Raise ||
        a.ActionType == ActionType.AllIn);

    if (hasAggressiveAction)
        return false;

    return ActionHistory.Take(actionIndex).Any(a => a.ActionType == ActionType.Call);
}
```

---

## 4) Test updates

### `tests/PokerAnalyzer.Domain.Tests/SolverLegalActionGeneratorTests.cs`
- Updated BB-option-after-limp test to assert exactly:
  - `Check`
  - `Raise(11)` (5.5bb at 2-chip BB)
  - `Raise(18)` (9bb at 2-chip BB)
- Added assertions that `Fold` and `Call` are absent in BB-option spot.

### `tests/PokerAnalyzer.Domain.Tests/SolverStateStepperTests.cs`
- Added `Step_BigBlindOptionVsLimp_RaiseIsExecutable()`.
- Verifies stepping a generated BB-option raise-over-limp does not throw.

### `tests/PokerAnalyzer.Domain.Tests/SolverHandStateTests.cs`
- Added `Constructor_ActionHistory_BigBlindOptionVsLimpRaise_DoesNotThrow()`.
- Verifies action-history validation accepts legal BB raise-over-limp sequence.

---

## Notes
- This is a narrow rules exception, not a global relaxation of raise legality.
- Existing normal facing-limp behavior for non-BB players remains unchanged.
