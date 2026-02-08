using Microsoft.EntityFrameworkCore;
using PokerAnalyzer.Infrastructure;
using PokerAnalyzer.Infrastructure.Persistence;
using PokerAnalyzer.Web.Components;
using PokerAnalyzer.Web.Services;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();
builder.Services.AddPokerAnalyzer();

builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = builder.Configuration["Api:BaseUrl"];
    if (string.IsNullOrWhiteSpace(baseUrl))
        baseUrl = "http://localhost:5137"; // local dev fallback

    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});
var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
                 ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");

builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});
builder.Services.AddServerSideBlazor().AddCircuitOptions(o => o.DetailedErrors = true);
builder.Services.AddDbContextFactory<PokerDbContext>(opt =>
{
    var dbPath = PokerDbPaths.GetDefaultSqlitePath();
    opt.UseSqlite($"Data Source={PokerDbPaths.GetDefaultSqlitePath()}");
});
var app = builder.Build();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<PokerAnalyzer.Web.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
