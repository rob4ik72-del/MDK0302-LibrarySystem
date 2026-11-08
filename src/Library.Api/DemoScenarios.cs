using System.Net.Http.Headers;
using Library.Core;
namespace Library.Api;
public sealed record HttpCheck(string Name, int Expected, int Actual, bool Passed);
public static class DemoScenarios
{
    public static async Task<HttpCheck[]> Run(string address, TokenRegistry tokens)
    {
        var checks = new List<HttpCheck>();
        async Task Check(string name, byte[] bytes, string filename, int expected, string? role = "Librarian")
        {
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            if (role is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Issue(role, Guid.NewGuid().ToString()));
            using var data = new MultipartFormDataContent();
            data.Add(new StringContent("Учебная книга"), "title");data.Add(new ByteArrayContent(bytes), "file", filename);
            using var response = await client.PostAsync("/api/books/upload", data);
            checks.Add(new(name, expected, (int)response.StatusCode, (int)response.StatusCode == expected));
        }
        await Check("PDF в карантин", Samples.Pdf(), "sample.pdf", 201);
        await Check("EPUB в карантин", Samples.Epub(), "sample.epub", 201);
        await Check("Безопасный ZIP", Samples.Zip(("book.pdf", Samples.Pdf())), "books.zip", 201);
        await Check("Zip Slip ../", Samples.Zip(("../escape.pdf", Samples.Pdf())), "unsafe.zip", 400);
        await Check("Подмена PDF", "MZ executable"u8.ToArray(), "fake.pdf", 400);
        await Check("Запрещённый EXE", "MZ"u8.ToArray(), "bad.exe", 400);
        await Check("Reader без права записи", Samples.Pdf(), "book.pdf", 403, "Reader");
        await Check("Нет токена", Samples.Pdf(), "book.pdf", 401, null);
        await Check("Пустой файл", [], "book.pdf", 400);
        await Check("Более 1 МиБ", new byte[FilePolicy.BookLimit + 1], "book.pdf", 413);
        await Check("Дубли ZIP", Samples.Zip(("a.pdf", Samples.Pdf()), ("a.pdf", Samples.Pdf())), "dup.zip", 400);
        using var rateClient = new HttpClient { BaseAddress = new Uri(address) };
        rateClient.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Issue("Librarian", Guid.NewGuid().ToString()));
        int last = 0;
        for (int i = 0; i < 4; i++)
        {
            using var data = new MultipartFormDataContent();data.Add(new StringContent("Книга"), "title");
            data.Add(new ByteArrayContent(Samples.Pdf()), "file", "book.pdf");
            using var response = await rateClient.PostAsync("/api/books/upload", data);last = (int)response.StatusCode;
        }
        checks.Add(new("Четвёртая загрузка за минуту", 429, last, last == 429));
        return checks.ToArray();
    }
}
