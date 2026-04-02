using Microsoft.AspNetCore.Authorization;
using Codec.Api.Services;

namespace Codec.Api.Filters;

public class GlobalAdminHandler(IUserService userService) : AuthorizationHandler<GlobalAdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GlobalAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var (user, _) = await userService.GetOrCreateUserAsync(context.User);
        if (user.IsGlobalAdmin)
            context.Succeed(requirement);
    }
}
