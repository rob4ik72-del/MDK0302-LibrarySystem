using Library.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Library.Tests;

public sealed class ApiHarness : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "LibraryTests", Guid.NewGuid().ToString("N"));
    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }
    public ApiHarness(string environment = "Development")
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string,string?> { ["StorageRoot"] = Root }));
        });
        Client = Factory.CreateClient();
    }
    public void Role(string role, string actor = "alice")
    {
        string token = Factory.Services.GetRequiredService<TokenRegistry>().Issue(role, actor);
        Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }
    public async Task<HttpResponseMessage> Upload(byte[] bytes, string filename = "book.pdf", string title = "Книга")
    {
        using var data = new MultipartFormDataContent();data.Add(new StringContent(title), "title");
        data.Add(new ByteArrayContent(bytes), "file", filename);
        return await Client.PostAsync("/api/books/upload", data);
    }
    public void Dispose()
    {
        Client.Dispose();Factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
