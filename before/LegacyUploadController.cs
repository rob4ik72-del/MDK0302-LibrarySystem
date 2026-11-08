using System.IO.Compression;
namespace Library.Before;
// Изолированный исходный образец. Не включается в сборку API.
public class LegacyUploadController
{
    private string Root = "App_Data";
    public string upload(string role, string name, byte[] bytes)
    {
        if (role == "Librarian" || role == "Reader")
        {
            if (bytes.Length == 0) return "error";
            if (name.EndsWith(".zip"))
            {
                using var zip = new ZipArchive(new MemoryStream(bytes));
                foreach (var entry in zip.Entries)
                {
                    string path = Path.Combine(Root, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var input = entry.Open();
                    using var output = File.Create(path);
                    input.CopyTo(output);
                }
            }
            else File.WriteAllBytes(Path.Combine(Root, name), bytes);
            return "ok";
        }
        return "error";
    }
    public int count() => Directory.GetFiles(Root).Length;
}
