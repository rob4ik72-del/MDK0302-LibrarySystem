using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Library.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;


namespace Library.Api;

public static class LibraryHost
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        string storage = builder.Configuration["StorageRoot"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(storage);
        Directory.CreateDirectory(Path.Combine(storage, "quarantine"));
        Directory.CreateDirectory(Path.Combine(storage, "published"));
        builder.Services.AddSingleton<StorageRoot>(services => new StorageRoot(
            services.GetRequiredService<IConfiguration>()["StorageRoot"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data")));
        builder.Services.AddSingleton<TokenRegistry>();
        builder.Services.AddSingleton<MetadataValidator>();
        builder.Services.AddScoped<UploadService>();
        builder.Services.AddDbContext<LibraryDb>(o => o.UseSqlite("Data Source=" + Path.Combine(storage, "library.db")));
        builder.Services.AddControllers();
        builder.Services.AddAuthentication("LabToken").AddScheme<AuthenticationSchemeOptions, TokenHandler>("LabToken", null);
        builder.Services.AddAuthorization(o => o.AddPolicy("LibrarianOnly", p => p.RequireAuthenticatedUser().RequireRole("Librarian")));
        builder.Services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 3, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        storage = app.Services.GetRequiredService<StorageRoot>().Path;
        Directory.CreateDirectory(storage);
        Directory.CreateDirectory(Path.Combine(storage, "quarantine"));
        Directory.CreateDirectory(Path.Combine(storage, "published"));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDb>();
            db.Database.EnsureCreated();
            if (!db.Books.Any())
            {
                byte[] sample = Samples.Pdf();
                File.WriteAllBytes(Path.Combine(storage, "published", "sample.pdf"), sample);
                db.Books.Add(new Book { Id = Guid.Parse("11111111-1111-4111-8111-111111111111"), Title = "Учебный документ библиотеки",
                    OriginalName = "sample.pdf", StoredName = "sample.pdf", Owner = "system", Published = true, Length = sample.Length });
                db.SaveChanges();
            }
        }
        var auditLock = new object();
        app.Use(async (context, next) => {
            try { await next(); }
            catch (RejectedFile error) {
                context.Response.StatusCode = error.Status;
                await context.Response.WriteAsJsonAsync(new { code = "upload_rejected", message = error.Message });
            }
            catch (Exception error) {
                app.Logger.LogError(error, "Ошибка запроса {RequestId}", context.TraceIdentifier);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { code = "server_error", requestId = context.TraceIdentifier });
            }
            finally {
                if (context.Request.Path == "/api/books/upload") lock (auditLock)
                    File.AppendAllText(Path.Combine(storage, "audit.jsonl"), JsonSerializer.Serialize(new {
                        requestId = context.TraceIdentifier, actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                        status = context.Response.StatusCode, at = DateTimeOffset.UtcNow }) + Environment.NewLine);
            }
        });
        app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
        app.MapControllers();app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        LabEndpoints.Map(app);
        return app;
    }
}
