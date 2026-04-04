using Microsoft.AspNetCore.Authorization;
using Codec.Api.Services;

namespace Codec.Api.Filters;

public class ActiveUserHandler(IUserService userService) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var user = await userService.ResolveUserAsync(context.User);
        if (user is not null && !user.IsDisabled)
            context.Succeed(requirement);
    }
}
