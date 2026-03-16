using Codec.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireEmailVerifiedAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            await next();
            return;
        }

        // Google users are always verified
        var issuer = user.FindFirst("iss")?.Value;
        if (issuer is "https://accounts.google.com" or "accounts.google.com")
        {
            await next();
            return;
        }

        var sub = user.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            await next();
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<CodecDbContext>();
        var dbUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (dbUser is not null && !dbUser.EmailVerified)
        {
            context.Result = new ObjectResult(new { code = "email_not_verified" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
