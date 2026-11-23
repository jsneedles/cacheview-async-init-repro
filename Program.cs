using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(1));
builder.Services.AddRazorComponents();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
app.UseAntiforgery();
app.MapRazorComponents<CacheViewRepro.App>();
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
foreach (var variant in new[] { "plain", "guarded" })
{
    string? previousHtml = null;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var html = await client.GetStringAsync($"/?variant={variant}");
            Console.WriteLine($"{variant} request {attempt}: {started.ElapsedMilliseconds}ms, cached markers unchanged: {previousHtml is null || previousHtml == html}");
            previousHtml = html;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine($"{variant} request {attempt}: TIMED OUT after {started.ElapsedMilliseconds}ms");
        }
    }
}
var concurrent = await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
{
    try
    {
        return await client.GetStringAsync("/?variant=concurrent");
    }
    catch (TaskCanceledException)
    {
        return "TIMED OUT";
    }
}));
Console.WriteLine($"Concurrent cold requests: {concurrent.Count(x => x == "TIMED OUT")} of {concurrent.Length} timed out");
await app.StopAsync();
