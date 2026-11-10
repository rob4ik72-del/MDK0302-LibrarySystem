using System.Net;
using System.Text.Json;
using Library.Core;
namespace Library.Api;
public static class LabEndpoints
{
    public static string ProjectRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null) { if (Directory.Exists(Path.Combine(dir.FullName, "artifacts"))) return dir.FullName; dir = dir.Parent; }
        throw new DirectoryNotFoundException("Открывайте программу из корня решения");
    }
    private static bool Allowed(HttpContext c, IWebHostEnvironment env) =>
        env.IsDevelopment() && c.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    public static void Map(WebApplication app)
    {
        string Root() => app.Configuration["LabContentRoot"] ?? ProjectRoot();
        app.MapGet("/", (HttpContext c, IWebHostEnvironment env) => Allowed(c, env)
            ? Results.Content(File.ReadAllText(Path.Combine(Root(), "web/index.html")), "text/html; charset=utf-8") : Results.NotFound());
        app.MapGet("/lab/diagram/dfd", (HttpContext c, IWebHostEnvironment env) =>
            Allowed(c, env) ? Results.File(Path.Combine(Root(), "docs", "diagrams", "DFD.svg"),
                "image/svg+xml") : Results.NotFound());
        app.MapGet("/lab/data/{name}", (string name, HttpContext c, IWebHostEnvironment env) => {
            if (!Allowed(c, env) || !new[] { "standards", "harmonization", "quality", "threats", "analysis", "tests" }.Contains(name)) return Results.NotFound();
            string path = Path.Combine(Root(), name is "analysis" or "tests" ? "verification" : "artifacts", name + ".json");
            return File.Exists(path) ? Results.Content(File.ReadAllText(path), "application/json") : Results.Ok(new { status = "Сначала выполните Run_Tests.cmd" });
        });
        app.MapGet("/lab/session/{role}", (string role, HttpContext c, IWebHostEnvironment env, TokenRegistry tokens) => {
            if (!Allowed(c, env) || role is not ("Reader" or "Librarian")) return Results.NotFound();
            return Results.Ok(new { token = tokens.Issue(role, Guid.NewGuid().ToString("N")) });
        });
        app.MapGet("/lab/sample/{kind}", (string kind, HttpContext c, IWebHostEnvironment env) => {
            if (!Allowed(c, env)) return Results.NotFound();
            return kind switch { "pdf" => Results.File(Samples.Pdf(), "application/pdf", "sample.pdf"),
                "epub" => Results.File(Samples.Epub(), "application/epub+zip", "sample.epub"),
                "zip-slip" => Results.File(Samples.Zip(("../escape.pdf", Samples.Pdf())), "application/zip", "unsafe.zip"),
                _ => Results.NotFound() };
        });
        app.MapPost("/lab/scenarios", async (HttpContext c, IWebHostEnvironment env, TokenRegistry tokens) => {
            if (!Allowed(c, env)) return Results.NotFound();
            var result = await DemoScenarios.Run("http://127.0.0.1:" + c.Connection.LocalPort, tokens);
            return Results.Ok(result);
        });
    }
}
