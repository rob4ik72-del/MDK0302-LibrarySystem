using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace Library.Core;

public sealed record AcceptedFile(string OriginalName, byte[] Bytes, string Extension);
public sealed class RejectedFile(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Проверяет формат и пути до записи недоверенного содержимого.</summary>
public static class FilePolicy
{
    public const int BookLimit = 1024 * 1024;
    public const int InputLimit = 2 * BookLimit;
    public const int InflatedLimit = 3 * BookLimit;
    public const int EntryLimit = 12;

    /// <summary>Проверяет сегменты пути с учётом имён Windows.</summary>
    public static bool IsSafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            name.Any(c => char.IsControl(c) || c is ':' or '"' or '*' or '?' or '<' or '>' or '|'))
            return false;
        string normalized = name.Replace('\\', '/');
        if (normalized.StartsWith('/')) return false;
        foreach (string part in normalized.Split('/'))
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) return false;
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }.Contains(stem)) return false;
            if (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                stem[3] is >= '1' and <= '9') return false;
        }
        return true;
    }

    /// <summary>Читает поток с ограничением фактически полученных байтов.</summary>
    public static byte[] ReadLimited(Stream stream, int limit)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            if (output.Length + count > limit) throw new RejectedFile("Превышен размер файла", 413);
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    /// <summary>Возвращает проверенный набор; сохранение выполняется отдельно.</summary>
    public static AcceptedFile[] Validate(string filename, byte[] bytes)
    {
        if (!IsSafeName(filename)) throw new RejectedFile("Небезопасное имя файла");
        if (bytes.Length == 0) throw new RejectedFile("Пустой файл");
        if (bytes.Length > InputLimit) throw new RejectedFile("Входной файл больше 2 МиБ", 413);
        if (Path.GetExtension(filename).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return ValidateZip(bytes);
        return [ValidateBook(filename, bytes)];
    }

    private static AcceptedFile ValidateBook(string name, byte[] bytes)
    {
        if (bytes.Length == 0) throw new RejectedFile("Пустая книга");
        if (bytes.Length > BookLimit) throw new RejectedFile("Книга больше 1 МиБ", 413);
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".pdf")
        {
            if (!bytes.AsSpan().StartsWith("%PDF-"u8) ||
                !Encoding.ASCII.GetString(bytes.AsSpan(Math.Max(0, bytes.Length - 1024))).Contains("%%EOF"))
                throw new RejectedFile("Сигнатура PDF не соответствует расширению");
        }
        else if (extension == ".epub") ValidateEpub(bytes);
        else throw new RejectedFile("Разрешены PDF, EPUB и ZIP-пакеты");
        return new(name, bytes, extension);
    }

    private static AcceptedFile[] ValidateZip(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            if (zip.Entries.Count is 0 or > EntryLimit) throw new RejectedFile("От 1 до 12 записей ZIP");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<AcceptedFile>();
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (!IsSafeName(entry.FullName) || !names.Add(entry.FullName.Replace('\\', '/')) ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new RejectedFile("Небезопасный путь, ссылка или дубль ZIP");
                if (entry.Length > BookLimit) throw new RejectedFile("Книга больше 1 МиБ", 413);
                if (entry.Length > 0 && (double)entry.Length / Math.Max(1, entry.CompressedLength) > 200)
                    throw new RejectedFile("Коэффициент сжатия больше 200");
                using var stream = entry.Open();
                byte[] content = ReadLimited(stream, BookLimit);
                total += content.Length;
                if (total > InflatedLimit) throw new RejectedFile("Объём распаковки больше 3 МиБ", 413);
                files.Add(ValidateBook(entry.FullName, content));
            }
            return files.ToArray();
        }
        catch (InvalidDataException) { throw new RejectedFile("Повреждённый ZIP"); }
    }

    private static void ValidateEpub(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            if (zip.Entries.Count > EntryLimit) throw new RejectedFile("Слишком много записей EPUB");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var e in zip.Entries)
            {
                if (!IsSafeName(e.FullName) || !names.Add(e.FullName) ||
                    ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new RejectedFile("Небезопасная структура EPUB");
                using var input = e.Open();
                total += ReadLimited(input, BookLimit).Length;
                if (total > InflatedLimit) throw new RejectedFile("Большой EPUB", 413);
            }
            var mime = zip.GetEntry("mimetype") ?? throw new RejectedFile("Нет mimetype EPUB");
            using var mimeReader = new StreamReader(mime.Open());
            if (mimeReader.ReadToEnd() != "application/epub+zip") throw new RejectedFile("Неверный mimetype");
            var container = zip.GetEntry("META-INF/container.xml") ?? throw new RejectedFile("Нет container.xml");
            using var xml = XmlReader.Create(container.Open(), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8192 });
            var doc = XDocument.Load(xml);
            string? package = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "rootfile")?
                .Attribute("full-path")?.Value;
            if (package is null || !IsSafeName(package) || zip.GetEntry(package) is null)
                throw new RejectedFile("Нет пакета EPUB");
        }
        catch (InvalidDataException) { throw new RejectedFile("Повреждённый EPUB"); }
        catch (XmlException) { throw new RejectedFile("Небезопасный XML EPUB"); }
    }
}
