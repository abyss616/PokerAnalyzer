# PokerAnalyzer – Fast Context Bootstrap README

## Purpose of This Document

This README is optimized for **rapid context reconstruction**.

If you are an AI assistant (or a new developer) joining this repository in a fresh session, your goal is to:
1. Understand the **domain model**
2. Understand the **runtime flows**
3. Know **where to look** for changes
4. Avoid re-deriving design intent from code

This document prioritizes **speed and correctness** over marketing or narrative description.

---

## High-Level System Overview

**PokerAnalyzer** is a layered .NET solution for:
- Uploading and storing poker hand histories (XML)
- Replaying hands into a deterministic state machine
- Comparing *actual* hero actions vs *recommended* actions from a strategy engine
- Producing structured analysis results

### Architecture (intended dependencies)

- **Domain**: Pure poker rules/state (no external dependencies)
- **Application**: Orchestrates analysis use-cases (depends on Domain + Engine abstractions)
- **Infrastructure**: Persistence, ingestion/parsing, and engine implementations
- **API**: HTTP surface (upload + analysis endpoints)
- **Web**: Blazor UI shell calling the API
- **Tests**: Domain + Application tests

**Dependency rule (inward-only):**  
`Web` → `API` → `Infrastructure` → `Application` → `Domain`  
`Domain` depends on nothing else.

---

## Solution Structure (authoritative)

```
PokerAnalyzer/
├─ PokerAnalyzer.sln
├─ src/
│  ├─ PokerAnalyzer.Domain
│  ├─ PokerAnalyzer.Application
│  ├─ PokerAnalyzer.Infrastructure
│  ├─ PokerAnalyzer.Api
│  └─ PokerAnalyzer.Web
└─ tests/
   ├─ PokerAnalyzer.Domain.Tests
   └─ PokerAnalyzer.Application.Tests
```

---

## Runtime Flows (what happens when)

### 1) Upload XML hand history (UI → API → DB)
1. User selects a file in `PokerAnalyzer.Web`
2. `ApiClient` posts multipart/form-data to `PokerAnalyzer.Api`
3. API controller receives `IFormFile`
4. Ingestion service stores raw XML + derived session/hand records into the DB

**Where to look**
- `src/PokerAnalyzer.Web/Services/ApiClient.cs`
- `src/PokerAnalyzer.Api/Controllers/HandHistoriesController.cs`
- `src/PokerAnalyzer.Infrastructure/HandHistories/HandHistoryIngestService.cs`
- `src/PokerAnalyzer.Infrastructure/Persistence/PokerDbContext.cs`

### 2) Analyze a hand (API → Application → Engine → Result)
1. Client posts a hand payload to the API `/analyze`
2. Application `HandAnalyzer` replays actions into `HandState`
3. On hero decision points, calls `IStrategyEngine.Recommend(...)`
4. Compares actual vs recommended and emits a result (decisions, severities, etc.)

**Where to look**
- `src/PokerAnalyzer.Api/Program.cs` (minimal API endpoint + request mapping)
- `src/PokerAnalyzer.Application/Analysis/HandAnalyzer.cs`
- `src/PokerAnalyzer.Application/Engines/IStrategyEngine.cs`
- `src/PokerAnalyzer.Infrastructure/Engines/DummyStrategyEngine.cs`

---

## Domain Layer (`PokerAnalyzer.Domain`)

### Core responsibility
A deterministic poker state machine used by analysis and engines.

### Most important type: `HandState`
If something is “wrong” about legality, betting, stacks, pot, or street transitions—start here.

**Key responsibilities**
- Represents full game state (players, stacks, pot, current street, current bet, etc.)
- Computes legal actions for a player
- Applies `BettingAction` and updates state
- Advances streets

**Key methods**
- `CreateNewHand(...)`
- `Apply(BettingAction)`
- `AdvanceStreet(Street, IEnumerable<Card>?)`
- `GetLegalActions(PlayerId)`
- `GetToCall(PlayerId)`
- `Clone()`

### Chip arithmetic: `ChipAmount`
All bet sizing arithmetic should flow through `ChipAmount` to avoid mixing primitives.
If you see comparisons like `ChipAmount != long`, fix at boundary (convert `long` → `ChipAmount`).

---

## Application Layer (`PokerAnalyzer.Application`)

### `HandAnalyzer`
The main orchestration entry point for analysis.

Responsibilities:
- Replays a hand into `HandState`
- Calls `IStrategyEngine` at hero decision points
- Scores mismatches and produces structured output

Search targets:
- `Analyze(`
- `Score(`
- `DecisionSeverity`
- `DecisionReview`

---

## Infrastructure Layer (`PokerAnalyzer.Infrastructure`)

### Persistence
- `PokerDbContext` defines EF mappings and relationships
- Watch for database provider differences (SQL Server vs PostgreSQL types)

**Common pitfall**: column types such as `"text"` may need to be adjusted for SQL Server.
Prefer provider-agnostic EF types unless you have a deliberate reason.

### Ingestion
- `HandHistoryIngestService` is the XML ingest pipeline
- It stores raw XML and extracts key metadata (hash/session linkage/etc.)

### Engines
- `DummyStrategyEngine` is a placeholder engine
- Replace/extend it for actual strategy logic, solvers, or external integrations

---

## API Layer (`PokerAnalyzer.Api`)

### Endpoints
- `POST /api/hand-histories/upload-xml`  
  Receives a file (multipart) and stores it via ingestion.
- `POST /analyze`  
  Accepts a hand payload and returns an analysis result.

**Key files**
- `src/PokerAnalyzer.Api/Controllers/HandHistoriesController.cs`
- `src/PokerAnalyzer.Api/Program.cs`

---

## Web Layer (`PokerAnalyzer.Web`)

Blazor UI that:
- Allows selecting a file
- Calls API endpoints via `ApiClient`

**Key files**
- `src/PokerAnalyzer.Web/Services/ApiClient.cs`
- `src/PokerAnalyzer.Web/Components/Pages/HandHistoryUpload.razor`

---

## Tests

- `PokerAnalyzer.Domain.Tests`: deterministic domain behavior, parsing
- `PokerAnalyzer.Application.Tests`: analysis orchestration and scoring behavior

If refactoring `HandState` or `HandAnalyzer`, update or extend tests first.

---

## Quick “Where Do I Change X?” Map

| Goal | Start here |
|---|---|
| Betting legality / pot / stacks wrong | `Domain/Game/HandState.cs` |
| Chip type mismatch | `Domain/Game/ChipAmount.cs` + call sites |
| Hero decision scoring logic | `Application/Analysis/HandAnalyzer.cs` |
| Recommended action logic | `Infrastructure/Engines/*StrategyEngine*.cs` |
| XML upload fails | `Api/Controllers/HandHistoriesController.cs` + `Web/Services/ApiClient.cs` |
| EF mapping/provider issues | `Infrastructure/Persistence/PokerDbContext.cs` |
| UI file select not triggering | `Web/.../HandHistoryUpload.razor` |

---

## “New Chat” Bootstrap Checklist (for AI assistants)

When a new chat starts, do the following in order:
1. Scan `src/` for project boundaries and confirm layers exist as documented.
2. Identify the two main flows:
   - Upload XML hand history
   - Analyze a hand
3. Open and summarize (mentally) these key files:
   - `Domain/Game/HandState.cs`
   - `Application/Analysis/HandAnalyzer.cs`
   - `Infrastructure/HandHistories/HandHistoryIngestService.cs`
   - `Infrastructure/Persistence/PokerDbContext.cs`
   - `Api/Program.cs` and `Api/Controllers/HandHistoriesController.cs`
   - `Web/Services/ApiClient.cs` and upload razor page
4. Note provider assumptions (SQL Server vs PostgreSQL) in EF mapping.
5. List any newly added folders or engine implementations that are not documented here.

This checklist is designed to reconstruct full working context in minutes.

---

## Preflop CFR+ Solver (Approximate, 6-max-oriented abstraction)

A preflop-only CFR+ module is now available in `PokerAnalyzer.Application/PreflopSolver`.

### What it does
- Builds a compact preflop game tree with the configured microstakes sizing set:
  - Opens: 2.5bb (UTG/HJ/CO/BTN), SB first-in 3.0bb or limp
  - 3bet: 9bb IP / 10bb OOP / 10.5bb SB vs BTN
  - 4bet: 22bb or jam when stack-commit rules apply
- Solves mixed strategies using CFR+ (regret-matching+ and averaged strategy output).
- Uses pluggable continuation EV for call-to-flop nodes (`IContinuationValueProvider`).
- Evaluates all-in terminals with an approximate equity model and utility caching.

### API
- `IPreflopSolverService.SolvePreflop(PreflopSolverConfig config)`
  - Returns node/hand mixed strategies and per-node EV table.
- `IPreflopSolverService.QueryStrategy(PreflopNodeState state, HoleCards heroHand)`
  - Returns `{ action -> frequency }`, best action, and EV estimate.

### Rake / stacks / sizing controls
- Rake is configured through `RakeConfig(percent, capBb, noFlopNoDrop)` in `PreflopSolverConfig`.
- Effective stack is configured via `PreflopSolverConfig.StartingStackBb` and query node state.
- Sizing set is implemented in `PreflopGameTreeBuilder` and can be edited there.

### Engine selection
- Default strategy engine now uses the solver-backed engine.
- Set environment variable `POKER_ANALYZER_USE_LEGACY_HEURISTIC=1` to use the old dummy heuristic engine.

### Single-hand output
The strategy engine explanation includes a readable preflop log string with:
- node id
- chosen best action
- EV estimate
