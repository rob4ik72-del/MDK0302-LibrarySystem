using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Library.Api;

/// <summary>Принимает книги только от библиотекаря.</summary>
[ApiController]
[Route("api/books")]
[Authorize(Policy = "LibrarianOnly")]
public sealed class UploadController(UploadService service) : ControllerBase
{
    /// <summary>Создаёт записи карантина и возвращает 201.</summary>
    [HttpPost("upload")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(2 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> Upload([FromForm] string title, IFormFile file, CancellationToken ct)
    {
        string actor = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var rows = await service.SaveAsync(title, file, actor, ct);
        return StatusCode(201, rows.Select(x => new { x.Id, x.Title, x.OriginalName,
            x.Length, x.Sha256, Status = "Quarantined" }));
    }
}
