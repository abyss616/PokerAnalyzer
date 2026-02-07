# PokerAnalyzer.Web

Blazor (server-interactive) UI project intended to be added to your existing PokerAnalyzer solution.

## Add to solution

From the solution root:

```bash
dotnet sln add src/PokerAnalyzer.Web/PokerAnalyzer.Web.csproj
```

## Configure API base URL

Edit `appsettings.json`:

```json
{
  "Api": { "BaseUrl": "http://localhost:5137" }
}
```

The UI expects an endpoint:

- `POST /hand-history/upload` (multipart/form-data with field name `file`)

## Run

```bash
dotnet run --project src/PokerAnalyzer.Web
```
