using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "GlobalAdmin")]
[Route("admin/messages")]
public class AdminMessagesController(CodecDbContext db) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> SearchMessages([FromQuery] string search, [FromQuery] PaginationParams p)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Length < 2)
            return BadRequest(new { error = "Search term must be at least 2 characters." });

        var escaped = search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var term = $"%{escaped}%";

        var query = db.Messages.AsNoTracking()
            .Where(m => EF.Functions.ILike(m.Body, term));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(m => new
            {
                m.Id, Content = m.Body, m.CreatedAt, m.ChannelId,
                AuthorName = m.AuthorUser!.DisplayName,
                ChannelName = m.Channel!.Name,
                ServerName = m.Channel.Server!.Name,
                ServerId = m.Channel.ServerId
            })
            .ToListAsync();

        return Ok(PaginatedResponse<object>.Create(items.Cast<object>().ToList(), totalCount, p.Page, p.PageSize));
    }
}
