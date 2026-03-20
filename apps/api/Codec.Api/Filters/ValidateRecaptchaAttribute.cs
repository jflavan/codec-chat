using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Codec.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class ValidateRecaptchaAttribute : Attribute, IFilterFactory
{
    public string Action { get; set; } = "login";
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var recaptchaService = serviceProvider.GetRequiredService<RecaptchaService>();
        var options = serviceProvider.GetRequiredService<IOptions<RecaptchaSettings>>();
        var logger = serviceProvider.GetRequiredService<ILogger<ValidateRecaptchaFilter>>();
        return new ValidateRecaptchaFilter(recaptchaService, options, logger, Action);
    }
}

public class ValidateRecaptchaFilter(
    RecaptchaService recaptchaService,
    IOptions<RecaptchaSettings> options,
    ILogger<ValidateRecaptchaFilter> logger,
    string expectedAction) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            await next();
            return;
        }

        // Find the IRecaptchaRequest in the action arguments (runs after model binding)
        var recaptchaRequest = context.ActionArguments.Values
            .OfType<IRecaptchaRequest>()
            .FirstOrDefault();

        var token = recaptchaRequest?.RecaptchaToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("reCAPTCHA token missing from request to {Action}", context.ActionDescriptor.DisplayName);
            context.Result = new BadRequestObjectResult(new { error = "reCAPTCHA token is required." });
            return;
        }

        var (success, score, error) = await recaptchaService.VerifyAsync(token, expectedAction);

        if (!success)
        {
            logger.LogWarning("reCAPTCHA verification failed for {Action}. Score: {Score}, Error: {Error}",
                context.ActionDescriptor.DisplayName, score, error);
            context.Result = new ObjectResult(new { error = "reCAPTCHA verification failed." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
