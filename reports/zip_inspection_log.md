# PokerAnalyzer Zip Inspection Execution Log

## PLAN
1. Validate current working directory and verify the target zip path exists.
   - Commands:
     - `pwd`
     - `ls -l /mnt/data/PokerAnalyzer.zip`
2. If the target zip is missing, search common filesystem locations for `PokerAnalyzer.zip` and any zip files under `/workspace`.
   - Commands:
     - `ls -la /mnt`
     - `ls -la /mnt/data || true`
     - `find / -maxdepth 4 -name 'PokerAnalyzer.zip' 2>/dev/null | head`
     - `find /workspace -maxdepth 4 -name '*.zip' 2>/dev/null | sed -n '1,40p'`
3. Inspect repository contents as the available PokerAnalyzer material.
   - Commands:
     - `pwd`
     - `find . -maxdepth 2 -mindepth 1 | sed 's#^\./##' | sort | sed -n '1,200p'`
4. Count files by extension (top 20) and total files.
   - Command:
     - Python one-liner using `pathlib` and `Counter` over `/workspace/PokerAnalyzer`.
5. Explicitly verify presence/absence of requested markers.
   - Command:
     - `for pattern in '*.sln' '*.csproj' '*.cs' '*.razor' 'package.json' 'requirements.txt'; do ...; done`
6. Detect suspicious large directories and estimate size.
   - Command:
     - `du -sh` over `.git`, `bin`, `obj`, `node_modules`, and nested `src/*/bin|obj`, `tests/*/bin|obj`.
7. Check for likely data-only files (e.g., hand history XML/txt/log) to classify contents.
   - Command:
     - `find . -type f \( -name '*.xml' -o -name '*.txt' -o -name '*.log' \) | ...`

---

## VERBOSE RUN LOG

### Step 1 — Validate target zip path
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - `pwd`
  - `ls -l /mnt/data/PokerAnalyzer.zip`
- **Assumptions:** Target zip should exist exactly at `/mnt/data/PokerAnalyzer.zip`.
- **Output snippet:**
  - First lines:
    ```text
    /workspace/PokerAnalyzer
    ls: cannot access '/mnt/data/PokerAnalyzer.zip': No such file or directory
    ```
- **Exit status / errors:** `2` (error because target zip path does not exist).
- **Working directory (after):** `/workspace/PokerAnalyzer`

### Step 2 — Search for alternative zip locations
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - `pwd`
  - `ls -la /mnt`
  - `ls -la /mnt/data || true`
  - `find / -maxdepth 4 -name 'PokerAnalyzer.zip' 2>/dev/null | head`
  - `find /workspace -maxdepth 4 -name '*.zip' 2>/dev/null | sed -n '1,40p'`
- **Assumptions:** If `/mnt/data` is absent, zip may still exist elsewhere.
- **Output snippet:**
  - First lines:
    ```text
    /workspace/PokerAnalyzer
    total 8
    drwxr-xr-x  2 root root 4096 Oct 13 14:02 .
    drwxr-xr-x 20 root root 4096 Feb 25 15:50 ..
    ls: cannot access '/mnt/data': No such file or directory
    /workspace/PokerAnalyzer
    ```
  - Last lines:
    ```text
    ls: cannot access '/mnt/data': No such file or directory
    /workspace/PokerAnalyzer
    ```
- **Exit status / errors:** `0` (search commands succeeded; no matching zip files found).
- **Working directory (after):** `/workspace/PokerAnalyzer`

### Step 3 — Inspect top-level tree (depth 2)
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - `pwd`
  - `find . -maxdepth 2 -mindepth 1 | sed 's#^\./##' | sort | sed -n '1,200p'`
- **Assumptions:** Since zip is unavailable, inspect current repository contents directly.
- **Output snippet (full; under 200 lines):**
  ```text
  /workspace/PokerAnalyzer
  .git
  .git/FETCH_HEAD
  .git/HEAD
  .git/branches
  .git/config
  .git/description
  .git/hooks
  .git/index
  .git/info
  .git/logs
  .git/objects
  .git/packed-refs
  .git/refs
  .gitattributes
  .gitignore
  Directory.Build.props
  PokerAnalyzer.sln
  README.md
  src
  src/PokerAnalyzer.Api
  src/PokerAnalyzer.Application
  src/PokerAnalyzer.Domain
  src/PokerAnalyzer.Infrastructure
  src/PokerAnalyzer.Web
  tests
  tests/PokerAnalyzer.Application.Tests
  tests/PokerAnalyzer.Domain.Tests
  ```
- **Exit status / errors:** `0`.
- **Working directory (after):** `/workspace/PokerAnalyzer`

### Step 4 — Count file extensions (top 20)
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - Python command:
    ```bash
    python - <<'PY'
    from pathlib import Path
    from collections import Counter
    root=Path('/workspace/PokerAnalyzer')
    counts=Counter()
    for p in root.rglob('*'):
        if p.is_file():
            ext=p.suffix.lower() or '<no_ext>'
            counts[ext]+=1
    for ext,count in counts.most_common(20):
        print(f'{ext}\t{count}')
    print('TOTAL_FILES',sum(counts.values()))
    PY
    ```
- **Assumptions:** Extension inferred from final suffix; files without suffix grouped as `<no_ext>`.
- **Output snippet (full):**
  ```text
  .cs	75
  .sample	14
  <no_ext>	12
  .csproj	7
  .razor	7
  .md	4
  .json	4
  .props	1
  .sln	1
  .css	1
  .pack	1
  .idx	1
  .rev	1
  TOTAL_FILES 129
  ```
- **Exit status / errors:** `0`.
- **Working directory (after):** `/workspace/PokerAnalyzer`

### Step 5 — Explicit marker presence/absence
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - ```bash
    cd /workspace/PokerAnalyzer
    for pattern in '*.sln' '*.csproj' '*.cs' '*.razor' 'package.json' 'requirements.txt'; do
      if find . -name "$pattern" -print -quit | grep -q .; then
        echo "PRESENT $pattern"
        find . -name "$pattern" | sed 's#^./##' | sort | sed -n '1,20p'
      else
        echo "ABSENT $pattern"
      fi
      echo '---'
    done
    ```
- **Assumptions:** Listing first up-to-20 matches is sufficient for verification.
- **Output snippet (first 20 + last 20 lines):**
  - First 20:
    ```text
    PRESENT *.sln
    PokerAnalyzer.sln
    ---
    PRESENT *.csproj
    src/PokerAnalyzer.Api/PokerAnalyzer.Api.csproj
    src/PokerAnalyzer.Application/PokerAnalyzer.Application.csproj
    src/PokerAnalyzer.Domain/PokerAnalyzer.Domain.csproj
    src/PokerAnalyzer.Infrastructure/PokerAnalyzer.Infrastructure.csproj
    src/PokerAnalyzer.Web/PokerAnalyzer.Web.csproj
    tests/PokerAnalyzer.Application.Tests/PokerAnalyzer.Application.Tests.csproj
    tests/PokerAnalyzer.Domain.Tests/PokerAnalyzer.Domain.Tests.csproj
    ---
    PRESENT *.cs
    src/PokerAnalyzer.Api/Controllers/HandAnalysisController.cs
    src/PokerAnalyzer.Api/Controllers/HandHistoriesController.cs
    src/PokerAnalyzer.Api/Controllers/PositionAssigner.cs
    src/PokerAnalyzer.Api/Program.cs
    src/PokerAnalyzer.Application/Analysis/DecisionReview.cs
    src/PokerAnalyzer.Application/Analysis/DecisionSeverity.cs
    src/PokerAnalyzer.Application/Analysis/HandAnalysisResult.cs
    ```
  - Last 20:
    ```text
    src/PokerAnalyzer.Application/Analysis/IStoredHandAnalysisService.cs
    src/PokerAnalyzer.Application/Analysis/StoredHandAnalysisService.cs
    src/PokerAnalyzer.Application/Engines/IMonteCarloReferenceEngine.cs
    src/PokerAnalyzer.Application/Engines/IStrategyEngine.cs
    src/PokerAnalyzer.Application/Engines/Recommendation.cs
    src/PokerAnalyzer.Application/ServiceCollectionExtensions.cs
    src/PokerAnalyzer.Domain/Cards/HoleCards.cs
    src/PokerAnalyzer.Domain/Cards/Rank.cs
    src/PokerAnalyzer.Domain/Cards/Suit.cs
    src/PokerAnalyzer.Domain/Entities/Board.cs
    src/PokerAnalyzer.Domain/Entities/Card.cs
    src/PokerAnalyzer.Domain/Entities/Hand.cs
    ---
    PRESENT *.razor
    src/PokerAnalyzer.Web/Components/App.razor
    src/PokerAnalyzer.Web/Components/Layout/MainLayout.razor
    src/PokerAnalyzer.Web/Components/Pages/HandHistoryUpload.razor
    src/PokerAnalyzer.Web/Components/Pages/Home.razor
    src/PokerAnalyzer.Web/Components/Pages/LeakageStats.razor
    src/PokerAnalyzer.Web/Components/Routes.razor
    src/PokerAnalyzer.Web/Components/_Imports.razor
    ---
    ABSENT package.json
    ---
    ABSENT requirements.txt
    ---
    ```
- **Exit status / errors:** `0`.
- **Working directory (after):** `/workspace/PokerAnalyzer`

### Step 6 — Suspicious large directory check
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - ```bash
    cd /workspace/PokerAnalyzer
    for d in .git bin obj node_modules src/*/bin src/*/obj tests/*/bin tests/*/obj; do
      for m in $d; do
        if [ -d "$m" ]; then
          size=$(du -sh "$m" 2>/dev/null | cut -f1)
          echo "$m\t$size"
        fi
      done
    done | sort -u
    ```
- **Assumptions:** Candidate directories indicate build artifacts or vendored dependencies.
- **Output snippet:**
  ```text
  .git	552K
  ```
- **Exit status / errors:** `0`.
- **Working directory (after):** `/workspace/PokerAnalyzer`

### Step 7 — Data-file scan (xml/txt/log)
- **Working directory (before):** `/workspace/PokerAnalyzer`
- **Command(s):**
  - `cd /workspace/PokerAnalyzer`
  - `find . -type f \( -name '*.xml' -o -name '*.txt' -o -name '*.log' \) | sed 's#^./##' | sort | sed -n '1,120p'`
- **Assumptions:** Presence of hand-history-like data would likely appear in XML/TXT/LOG.
- **Output snippet:**
  ```text
  [no output]
  ```
- **Exit status / errors:** `0` (command completed; no matching files found).
- **Working directory (after):** `/workspace/PokerAnalyzer`

---

## Findings
- The requested source archive `/mnt/data/PokerAnalyzer.zip` is **not present** in this environment, and no `PokerAnalyzer.zip` was found via filesystem search.
- The available directory `/workspace/PokerAnalyzer` contains a **.NET solution and source code**, not merely data files:
  - Found: `.sln`, `.csproj`, `.cs`, `.razor`
  - Not found: `package.json`, `requirements.txt`
- Extension distribution confirms source-heavy contents (not XML hand-history-only dataset).
- No suspicious large build/dependency directories were found except `.git` (~552K).
- No `.xml`, `.txt`, or `.log` files were found in the repo scan.

## DEBUG NOTES
If someone got a different result, the most likely causes are:
1. The zip was mounted at a different path in their runtime (e.g., `/mnt/data` exists for them but not here).
2. They ran commands from a different working directory or against a different checkout state.
3. Additional files were generated before inspection (e.g., build created `bin/obj`).
4. Filesystem permissions or container image differences altered `find` visibility.
