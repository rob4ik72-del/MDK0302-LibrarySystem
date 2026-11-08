using System.Security.Cryptography;
using FluentValidation;
using Library.Core;
using Microsoft.EntityFrameworkCore;
namespace Library.Api;

public sealed class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string StoredName { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Length { get; set; }
    public bool Published { get; set; }
}
public sealed class LibraryDb(DbContextOptions<LibraryDb> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
}
public sealed record UploadMetadata(string Title, string Filename, long Length);
/// <summary>Проверяет поля перед чтением и сохранением содержимого.</summary>
public sealed class MetadataValidator : AbstractValidator<UploadMetadata>
{
    public MetadataValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100)
            .Must(x => x is not null && !x.Any(char.IsControl));
        RuleFor(x => x.Filename).Must(FilePolicy.IsSafeName);
        RuleFor(x => x.Length).GreaterThan(0).LessThanOrEqualTo(FilePolicy.InputLimit);
    }
}
public sealed record StorageRoot(string Path);
/// <summary>Сохраняет только полностью проверенный набор в приватном карантине.</summary>
public sealed class UploadService(LibraryDb db, StorageRoot root, MetadataValidator validator)
{
    /// <summary>Выполняет валидацию, транзакцию метаданных и компенсацию файловой записи.</summary>
    public async Task<Book[]> SaveAsync(string title, IFormFile file, string actor, CancellationToken ct)
    {
        var check = validator.Validate(new UploadMetadata(title, file.FileName, file.Length));
        if (!check.IsValid) throw new RejectedFile("Некорректные поля загрузки");
        using var input = file.OpenReadStream();
        var accepted = FilePolicy.Validate(file.FileName, FilePolicy.ReadLimited(input, FilePolicy.InputLimit));
        var rows = accepted.Select(x => new Book { Id = Guid.NewGuid(), Title = title,
            OriginalName = x.OriginalName, Owner = actor, StoredName = Guid.NewGuid().ToString("N") + x.Extension,
            Length = x.Bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(x.Bytes)), Published = false }).ToArray();
        var written = new List<string>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            for (int i = 0; i < rows.Length; i++)
            {
                string path = System.IO.Path.Combine(root.Path, "quarantine", rows[i].StoredName);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                written.Add(path);
                await output.WriteAsync(accepted[i].Bytes, ct);
            }
            db.Books.AddRange(rows);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return rows;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            foreach (string path in written) File.Delete(path);
            throw;
        }
    }
}
