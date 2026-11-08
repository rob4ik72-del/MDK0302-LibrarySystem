using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Library.Core;
using Xunit;
namespace Library.Tests;

public sealed class UploadTests
{
    [Fact]
    public async Task InternalFailureDoesNotLeakPathOrStack()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        string target = Path.Combine(h.Root, "quarantine");
        Directory.Delete(target);File.WriteAllText(target, "blocked");
        var response = await h.Upload(Samples.Pdf());
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        string text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("server_error").And.NotContain(h.Root).And.NotContain("StackTrace");
    }
    [Fact]
    public async Task LibrarianUploadsPdfToQuarantine()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        var r = await h.Upload(Samples.Pdf());r.StatusCode.Should().Be(HttpStatusCode.Created);
        string result = await r.Content.ReadAsStringAsync();result.Should().Contain("Quarantined");
        var files = Directory.GetFiles(Path.Combine(h.Root, "quarantine"));files.Should().HaveCount(1);
        Path.GetFileName(files[0]).Should().NotBe("book.pdf");
    }
    [Fact]
    public async Task LibrarianUploadsEpub()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(Samples.Epub(), "book.epub")).StatusCode.Should().Be(HttpStatusCode.Created);
    }
    [Fact]
    public async Task SafeZipCreatesTwoBooks()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(Samples.Zip(("a.pdf", Samples.Pdf()), ("b.epub", Samples.Epub())), "books.zip"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        Directory.GetFiles(Path.Combine(h.Root, "quarantine")).Should().HaveCount(2);
    }
    [Fact]
    public async Task ReaderCannotUpload()
    {
        using var h = new ApiHarness();h.Role("Reader");
        (await h.Upload(Samples.Pdf())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
    [Fact]
    public async Task MissingTokenRejected()
    {
        using var h = new ApiHarness();
        (await h.Upload(Samples.Pdf())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Fact]
    public async Task InvalidTokenRejected()
    {
        using var h = new ApiHarness();h.Client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        (await h.Upload(Samples.Pdf())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("..\\escape.pdf")]
    [InlineData("/absolute.pdf")]
    [InlineData("C:\\escape.pdf")]
    [InlineData("a/../../escape.pdf")]
    public async Task ZipSlipLeavesNoFiles(string path)
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(Samples.Zip((path, Samples.Pdf())), "bad.zip")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.GetFiles(Path.Combine(h.Root, "quarantine")).Should().BeEmpty();
        File.Exists(Path.Combine(h.Root, "escape.pdf")).Should().BeFalse();
    }
    [Fact]
    public async Task SpoofedPdfRejected()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload("MZ executable"u8.ToArray())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task ExecutableRejected()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload("MZ"u8.ToArray(), "bad.exe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task EmptyFileRejected()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload([])).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task ExactOneMebibyteAccepted()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        byte[] bytes = new byte[FilePolicy.BookLimit];Samples.Pdf().CopyTo(bytes,0);"%%EOF"u8.CopyTo(bytes.AsSpan(bytes.Length-5));
        (await h.Upload(bytes)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
    [Fact]
    public async Task OneByteOverBookLimitRejected()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(new byte[FilePolicy.BookLimit+1])).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }
    [Fact]
    public async Task DuplicateNamesRejected()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(Samples.Zip(("a.pdf", Samples.Pdf()), ("A.pdf", Samples.Pdf())), "bad.zip"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task MixedZipSavesNothing()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(Samples.Zip(("a.pdf", Samples.Pdf()), ("bad.exe", "MZ"u8.ToArray())), "bad.zip"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.GetFiles(Path.Combine(h.Root, "quarantine")).Should().BeEmpty();
    }
    [Fact]
    public async Task FourthUploadRateLimited()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        for (int i=0;i<3;i++) (await h.Upload(Samples.Pdf())).StatusCode.Should().Be(HttpStatusCode.Created);
        (await h.Upload(Samples.Pdf())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
    [Fact]
    public async Task OtherLibrarianCannotSeeQuarantine()
    {
        using var h = new ApiHarness();h.Role("Librarian", "alice");await h.Upload(Samples.Pdf(),title:"Private title");
        h.Role("Librarian", "bob");
        (await h.Client.GetStringAsync("/api/books")).Should().NotContain("Private title");
    }
    [Fact]
    public async Task QuarantineCannotBeDownloaded()
    {
        using var h = new ApiHarness();h.Role("Librarian");var r=await h.Upload(Samples.Pdf());
        string id=JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement[0].GetProperty("id").GetString()!;
        (await h.Client.GetAsync("/api/books/"+id+"/file")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task ReaderDownloadsPublishedSample()
    {
        using var h = new ApiHarness();h.Role("Reader");
        var r=await h.Client.GetAsync("/api/books/11111111-1111-4111-8111-111111111111/file");
        r.StatusCode.Should().Be(HttpStatusCode.OK);r.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }
    [Fact]
    public async Task SqlLikeQueryRemainsData()
    {
        using var h = new ApiHarness();h.Role("Reader");
        var r=await h.Client.GetAsync("/api/books?query="+Uri.EscapeDataString("' OR 1=1 --"));
        r.StatusCode.Should().Be(HttpStatusCode.OK);(await r.Content.ReadAsStringAsync()).Should().Be("[]");
    }
    [Fact]
    public async Task ProductionHidesLabSession()
    {
        using var h = new ApiHarness("Production");
        (await h.Client.GetAsync("/lab/session/Librarian")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task RejectedUploadIsAuditedWithoutToken()
    {
        using var h = new ApiHarness();h.Role("Reader");await h.Upload(Samples.Pdf());
        string log=File.ReadAllText(Path.Combine(h.Root,"audit.jsonl"));
        log.Should().Contain("403").And.Contain("alice").And.NotContain("Bearer");
    }
    [Fact]
    public async Task LongTitleRejected()
    {
        using var h = new ApiHarness();h.Role("Librarian");
        (await h.Upload(Samples.Pdf(),title:new string('A',101))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
