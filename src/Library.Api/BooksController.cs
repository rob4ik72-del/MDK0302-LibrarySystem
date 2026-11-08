using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Library.Api;

/// <summary>Читает каталог с параметризованным LINQ и изоляцией карантина.</summary>
[ApiController]
[Route("api/books")]
[Authorize]
public sealed class BooksController(LibraryDb db, StorageRoot root) : ControllerBase
{
    /// <summary>Не раскрывает чужие неопубликованные документы.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? query, CancellationToken ct)
    {
        string actor = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        bool librarian = User.IsInRole("Librarian");
        var books = db.Books.Where(x => x.Published || (librarian && x.Owner == actor));
        if (!string.IsNullOrWhiteSpace(query)) books = books.Where(x => x.Title.Contains(query));
        return Ok(await books.Select(x => new { x.Id, x.Title, x.OriginalName, x.Length,
            Status = x.Published ? "Published" : "Quarantined" }).ToArrayAsync(ct));
    }
    /// <summary>Выдаёт только опубликованный образец вне веб-каталога.</summary>
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var book = await db.Books.SingleOrDefaultAsync(x => x.Id == id && x.Published, ct);
        if (book is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(Path.Combine(root.Path, "published", book.StoredName),
            "application/octet-stream", book.OriginalName);
    }
}
