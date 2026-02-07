# PokerAnalyzer.Domain

Pure domain model: cards, betting actions, hand history primitives, and a minimal `HandState` state machine.

Key conventions:
- All amounts are integer chips (`ChipAmount`).
- `BettingAction.Amount` for Bet/Raise/AllIn is a **to-amount** (total contribution on the street after the action).
