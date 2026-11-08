using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Xml.Linq;
using Library.Tools;
using Library.Core;
Console.OutputEncoding = Encoding.UTF8;
var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "artifacts"))) directory = directory.Parent;
string root = directory?.FullName ?? throw new IOException("Запуск из корня Project");
string verification = Path.Combine(root, "verification");Directory.CreateDirectory(verification);
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
void Save(string name, object data) => File.WriteAllText(Path.Combine(verification, name + ".json"), JsonSerializer.Serialize(data, options));
string command = args.FirstOrDefault() ?? "all";
try
{
    if (command is "all" or "samples")
    {
        Directory.CreateDirectory(Path.Combine(root, "samples"));
        File.WriteAllBytes(Path.Combine(root, "samples/sample.pdf"), Samples.Pdf());
        File.WriteAllBytes(Path.Combine(root, "samples/sample.epub"), Samples.Epub());
        File.WriteAllBytes(Path.Combine(root, "samples/zip-slip.zip"), Samples.Zip(("../escape.pdf", Samples.Pdf())));
    }
    foreach (string name in new[] { "standards", "harmonization", "quality", "threats", "bugs" })
        if (command == "all" || command == name)
        {
            string text = File.ReadAllText(Path.Combine(root, "artifacts", name + ".json"));
            File.WriteAllText(Path.Combine(verification, name + ".json"), text);
            var rows = JsonDocument.Parse(text).RootElement;
            string Escape(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            var lines = new List<string> { string.Join(';', rows[0].EnumerateObject().Select(x => Escape(x.Name))) };
            lines.AddRange(rows.EnumerateArray().Select(row => string.Join(';', row.EnumerateObject().Select(x => Escape(x.Value.ToString())))));
            File.WriteAllLines(Path.Combine(verification, name + ".csv"), lines, new UTF8Encoding(true));
            Console.WriteLine(name + ": строк " + rows.GetArrayLength());
        }
    if (command is "all" or "audit" or "metrics")
    {
        string before = File.ReadAllText(Path.Combine(root, "before/LegacyUploadController.cs"));
        string after = File.ReadAllText(Path.Combine(root, "src/Library.Api/UploadController.cs"));
        string catalog = File.ReadAllText(Path.Combine(root, "src/Library.Api/BooksController.cs"));
        var result = new { before = CodeQuality.Measure(before, "LegacyUploadController"),
            after = CodeQuality.Measure(after, "UploadController"), catalog = CodeQuality.Measure(catalog, "BooksController"),
            beforeAudit = CodeQuality.Audit(before), afterAudit = CodeQuality.Audit(after), catalogAudit = CodeQuality.Audit(catalog) };
        Save("analysis", result);Console.WriteLine(JsonSerializer.Serialize(result, options));
    }
    if (command == "parse-tests")
    {
        string path = args.Length > 1 ? args[1] : Path.Combine(verification, "library-tests.trx");
        var tests = XDocument.Load(path).Descendants().Where(x => x.Name.LocalName == "UnitTestResult")
            .Select(x => new { name = (string?)x.Attribute("testName"), outcome = (string?)x.Attribute("outcome"), duration = (string?)x.Attribute("duration") }).OrderBy(x => x.name).ToArray();
        if (tests.Length == 0) throw new IOException("Нет результатов TRX");
        var result = new { total = tests.Length, passed = tests.Count(x => x.outcome == "Passed"), failed = tests.Count(x => x.outcome == "Failed"), skipped = tests.Count(x => x.outcome is not ("Passed" or "Failed")), tests };
        Save("tests", result);Console.WriteLine($"Всего {result.total}; Passed {result.passed}; Failed {result.failed}; Skipped {result.skipped}");
        if (result.failed > 0 || result.skipped > 0) Environment.ExitCode = 1;
    }
    if (command == "api-checks")
    {
        using var client = new HttpClient { BaseAddress = new Uri(args.Length > 1 ? args[1] : "http://127.0.0.1:5352") };
        using var response = await client.PostAsync("/lab/scenarios", null);response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync();File.WriteAllText(Path.Combine(verification, "api-scenarios.json"), text);Console.WriteLine(text);
        if (JsonDocument.Parse(text).RootElement.EnumerateArray().Any(x => !x.GetProperty("passed").GetBoolean())) Environment.ExitCode = 1;
    }
    if (command == "open-ui")
    {
        var url = new Uri(args[1]);using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };bool ready = false;
        for (int i = 0; i < 30 && !ready; i++)
        {
            try { using var response = await client.GetAsync(new Uri(url, "/health"));ready = response.IsSuccessStatusCode; }
            catch (HttpRequestException) { } catch (TaskCanceledException) { }
            if (!ready) await Task.Delay(500);
        }
        if (!ready) throw new IOException("Сервер ещё не запущен");
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
}
catch (Exception e) { Console.Error.WriteLine("Операция не выполнена: " + e.Message);Environment.ExitCode = 1; }
