# reCAPTCHA v3 Integration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add invisible reCAPTCHA v3 verification to the login and register endpoints to block bots.

**Architecture:** Frontend loads the reCAPTCHA v3 script and obtains a token at submit time, sending it with the login/register request body. A `[ValidateRecaptcha]` action filter on the backend extracts the token from the bound DTO (via `IRecaptchaRequest` interface), calls Google's siteverify API through a typed `RecaptchaService`, and short-circuits with 400/403 if verification fails. An `Enabled` config flag bypasses verification in dev/test.

**Tech Stack:** ASP.NET Core 10 (action filter, typed HttpClient, IOptions), SvelteKit/Svelte 5, Google reCAPTCHA v3 API, Bicep IaC, GitHub Actions CI/CD

**Spec:** `docs/superpowers/specs/2026-03-19-recaptcha-v3-design.md`

---

### Task 1: Google Cloud — Create reCAPTCHA v3 Site Key

**Files:**
- No code files changed

This task creates the reCAPTCHA key pair using `gcloud` CLI. The site key is public; the secret key is private.

- [ ] **Step 1: Create the reCAPTCHA v3 key**

```bash
gcloud recaptcha keys create \
  --display-name="Codec Chat Web" \
  --web \
  --allow-all-domains \
  --integration-type=score
```

This returns a key ID. Note it down — this is your **site key**.

- [ ] **Step 2: Retrieve the secret key**

The secret key for reCAPTCHA v3 (non-Enterprise) is actually the same as the site key when using the standard `siteverify` endpoint. However, if using Google Cloud reCAPTCHA Enterprise API keys, you'll need to get the API key from the Google Cloud Console → APIs & Services → Credentials. For standard reCAPTCHA v3, create keys at https://www.google.com/recaptcha/admin — the "Secret key" shown there is what you need.

Store both values securely (you'll configure them in Tasks 7 and 8).

- [ ] **Step 3: Commit** (no code changes — this is a manual infrastructure step)

---

### Task 2: Backend — Configuration Model and Service Registration

**Files:**
- Create: `apps/api/Codec.Api/Models/RecaptchaSettings.cs`
- Create: `apps/api/Codec.Api/Models/IRecaptchaRequest.cs`
- Modify: `apps/api/Codec.Api/appsettings.json` (after line 53, add Recaptcha section)
- Modify: `apps/api/Codec.Api/Program.cs:199` (add service registrations near other AddScoped calls)

- [ ] **Step 1: Create RecaptchaSettings config model**

Create `apps/api/Codec.Api/Models/RecaptchaSettings.cs`:

```csharp
namespace Codec.Api.Models;

public class RecaptchaSettings
{
    public string SecretKey { get; set; } = "";
    public double ScoreThreshold { get; set; } = 0.5;
    public bool Enabled { get; set; } = true;
}
```

- [ ] **Step 2: Create IRecaptchaRequest interface**

Create `apps/api/Codec.Api/Models/IRecaptchaRequest.cs`:

```csharp
namespace Codec.Api.Models;

public interface IRecaptchaRequest
{
    string? RecaptchaToken { get; }
}
```

- [ ] **Step 3: Add Recaptcha config section to appsettings.json**

In `apps/api/Codec.Api/appsettings.json`, add after the `"Redis"` block (after line 46):

```json
"Recaptcha": {
  "SecretKey": "",
  "ScoreThreshold": 0.5,
  "Enabled": false
}
```

Note: `Enabled` defaults to `false` in the base config so local dev without keys works out of the box. Production overrides this via environment variable.

- [ ] **Step 4: Register settings and service in Program.cs**

In `apps/api/Codec.Api/Program.cs`, add after the `AddScoped<TokenService>()` line (line 200):

```csharp
builder.Services.Configure<RecaptchaSettings>(builder.Configuration.GetSection("Recaptcha"));
builder.Services.AddHttpClient<RecaptchaService>();
```

Add the `using Codec.Api.Models;` import if not already present (it likely is from other usages).

- [ ] **Step 5: Commit** (build verification deferred to Task 3 since RecaptchaService doesn't exist yet)

```bash
git add apps/api/Codec.Api/Models/RecaptchaSettings.cs apps/api/Codec.Api/Models/IRecaptchaRequest.cs apps/api/Codec.Api/appsettings.json apps/api/Codec.Api/Program.cs
git commit -m "feat(api): add reCAPTCHA config model, interface, and settings"
```

---

### Task 3: Backend — RecaptchaService

**Files:**
- Create: `apps/api/Codec.Api/Services/RecaptchaService.cs`

- [ ] **Step 1: Create RecaptchaService**

Create `apps/api/Codec.Api/Services/RecaptchaService.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Codec.Api.Models;
using Microsoft.Extensions.Options;

namespace Codec.Api.Services;

public class RecaptchaService(HttpClient httpClient, IOptions<RecaptchaSettings> options, ILogger<RecaptchaService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string VerifyUrl = "https://www.google.com/recaptcha/api/siteverify";

    public virtual async Task<(bool Success, double Score, string? Error)> VerifyAsync(string token, string expectedAction)
    {
        try
        {
            var settings = options.Value;
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = settings.SecretKey,
                ["response"] = token
            });

            var response = await httpClient.PostAsync(VerifyUrl, content);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<RecaptchaResponse>(body, JsonOptions);

            if (result is null)
            {
                logger.LogWarning("reCAPTCHA siteverify returned null response");
                return (false, 0, "Invalid reCAPTCHA response.");
            }

            if (!result.Success)
            {
                logger.LogWarning("reCAPTCHA verification failed. Error codes: {Errors}", result.ErrorCodes);
                return (false, 0, "reCAPTCHA verification failed.");
            }

            if (!string.Equals(result.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("reCAPTCHA action mismatch. Expected: {Expected}, Got: {Actual}", expectedAction, result.Action);
                return (false, result.Score, "reCAPTCHA action mismatch.");
            }

            if (result.Score < settings.ScoreThreshold)
            {
                logger.LogInformation("reCAPTCHA score {Score} below threshold {Threshold}", result.Score, settings.ScoreThreshold);
                return (false, result.Score, "reCAPTCHA score too low.");
            }

            return (true, result.Score, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "reCAPTCHA verification request failed");
            return (false, 0, "reCAPTCHA verification unavailable.");
        }
    }

    private class RecaptchaResponse
    {
        public bool Success { get; set; }
        public double Score { get; set; }
        public string? Action { get; set; }
        public string? ChallengeTs { get; set; }
        public string? Hostname { get; set; }
        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Services/RecaptchaService.cs
git commit -m "feat(api): add RecaptchaService for Google siteverify calls"
```

---

### Task 4: Backend — ValidateRecaptcha Action Filter

**Files:**
- Create: `apps/api/Codec.Api/Filters/ValidateRecaptchaAttribute.cs`

- [ ] **Step 1: Create the action filter**

Create `apps/api/Codec.Api/Filters/ValidateRecaptchaAttribute.cs`:

```csharp
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
```

- [ ] **Step 2: Build to verify compilation**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api/Filters/ValidateRecaptchaAttribute.cs
git commit -m "feat(api): add ValidateRecaptcha action filter"
```

---

### Task 5: Backend — Wire Up DTOs and Controller

**Files:**
- Modify: `apps/api/Codec.Api/Models/LoginRequest.cs` (add interface + property)
- Modify: `apps/api/Codec.Api/Models/RegisterRequest.cs` (add interface + property)
- Modify: `apps/api/Codec.Api/Controllers/AuthController.cs:27-28, 115-116` (add filter attribute)

- [ ] **Step 1: Update LoginRequest to implement IRecaptchaRequest**

Replace the full contents of `apps/api/Codec.Api/Models/LoginRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class LoginRequest : IRecaptchaRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    public string? RecaptchaToken { get; set; }
}
```

- [ ] **Step 2: Update RegisterRequest to implement IRecaptchaRequest**

Replace the full contents of `apps/api/Codec.Api/Models/RegisterRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Codec.Api.Models;

public class RegisterRequest : IRecaptchaRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(32, MinimumLength = 2)]
    public string Nickname { get; set; } = string.Empty;

    public string? RecaptchaToken { get; set; }
}
```

- [ ] **Step 3: Add [ValidateRecaptcha] to AuthController actions**

In `apps/api/Codec.Api/Controllers/AuthController.cs`:

Add the using statement at the top with the other usings:
```csharp
using Codec.Api.Filters;
```

Add the attribute to the Register method (before line 28):
```csharp
[HttpPost("register")]
[ValidateRecaptcha(Action = "register")]
public async Task<IActionResult> Register([FromBody] RegisterRequest request)
```

Add the attribute to the Login method (before line 116):
```csharp
[HttpPost("login")]
[ValidateRecaptcha(Action = "login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
```

- [ ] **Step 4: Build to verify compilation**

Run: `cd apps/api/Codec.Api && dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add apps/api/Codec.Api/Models/LoginRequest.cs apps/api/Codec.Api/Models/RegisterRequest.cs apps/api/Codec.Api/Controllers/AuthController.cs
git commit -m "feat(api): wire reCAPTCHA validation to login and register endpoints"
```

---

### Task 6: Backend — Unit Tests

**Files:**
- Create: `apps/api/Codec.Api.Tests/Services/RecaptchaServiceTests.cs`
- Create: `apps/api/Codec.Api.Tests/Filters/ValidateRecaptchaFilterTests.cs`

- [ ] **Step 1: Create RecaptchaServiceTests**

Create `apps/api/Codec.Api.Tests/Services/RecaptchaServiceTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Codec.Api.Tests.Services;

public class RecaptchaServiceTests
{
    private static RecaptchaService CreateService(
        HttpResponseMessage response,
        double scoreThreshold = 0.5)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(handler.Object);
        var options = Options.Create(new RecaptchaSettings
        {
            SecretKey = "test-secret",
            ScoreThreshold = scoreThreshold,
            Enabled = true
        });
        var logger = NullLogger<RecaptchaService>.Instance;
        return new RecaptchaService(httpClient, options, logger);
    }

    private static HttpResponseMessage OkResponse(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body))
        };

    [Fact]
    public async Task VerifyAsync_ValidToken_ReturnsSuccess()
    {
        var service = CreateService(OkResponse(new
        {
            success = true, score = 0.9, action = "login"
        }));

        var (success, score, error) = await service.VerifyAsync("valid-token", "login");

        Assert.True(success);
        Assert.Equal(0.9, score);
        Assert.Null(error);
    }

    [Fact]
    public async Task VerifyAsync_LowScore_ReturnsFailure()
    {
        var service = CreateService(OkResponse(new
        {
            success = true, score = 0.2, action = "login"
        }));

        var (success, score, error) = await service.VerifyAsync("low-score-token", "login");

        Assert.False(success);
        Assert.Equal(0.2, score);
        Assert.Contains("score too low", error);
    }

    [Fact]
    public async Task VerifyAsync_ActionMismatch_ReturnsFailure()
    {
        var service = CreateService(OkResponse(new
        {
            success = true, score = 0.9, action = "register"
        }));

        var (success, _, error) = await service.VerifyAsync("token", "login");

        Assert.False(success);
        Assert.Contains("action mismatch", error);
    }

    [Fact]
    public async Task VerifyAsync_GoogleReturnsFailure_ReturnsFailure()
    {
        var service = CreateService(OkResponse(new
        {
            success = false,
            errorCodes = new[] { "invalid-input-response" }
        }));

        var (success, _, error) = await service.VerifyAsync("bad-token", "login");

        Assert.False(success);
        Assert.Contains("verification failed", error);
    }

    [Fact]
    public async Task VerifyAsync_HttpError_ReturnsFailure()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var (success, _, error) = await service.VerifyAsync("token", "login");

        Assert.False(success);
        Assert.Contains("unavailable", error);
    }

    [Fact]
    public async Task VerifyAsync_CustomThreshold_RespectedCorrectly()
    {
        var service = CreateService(OkResponse(new
        {
            success = true, score = 0.6, action = "login"
        }), scoreThreshold: 0.7);

        var (success, score, _) = await service.VerifyAsync("token", "login");

        Assert.False(success);
        Assert.Equal(0.6, score);
    }
}
```

- [ ] **Step 2: Create ValidateRecaptchaFilterTests**

Create `apps/api/Codec.Api.Tests/Filters/ValidateRecaptchaFilterTests.cs`:

```csharp
using Codec.Api.Filters;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Codec.Api.Tests.Filters;

public class ValidateRecaptchaFilterTests
{
    private static (ValidateRecaptchaFilter Filter, Mock<RecaptchaService> ServiceMock) CreateFilter(
        bool enabled = true)
    {
        var options = Options.Create(new RecaptchaSettings
        {
            SecretKey = "test-secret",
            ScoreThreshold = 0.5,
            Enabled = enabled
        });

        // Create a real RecaptchaService mock - need to mock it properly
        var handler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handler.Object);
        var serviceLogger = NullLogger<RecaptchaService>.Instance;
        var serviceMock = new Mock<RecaptchaService>(httpClient, options, serviceLogger) { CallBase = false };

        var filterLogger = NullLogger<ValidateRecaptchaFilter>.Instance;
        var filter = new ValidateRecaptchaFilter(serviceMock.Object, options, filterLogger, "login");

        return (filter, serviceMock);
    }

    private static ActionExecutingContext CreateContext(object? requestBody = null)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        var arguments = new Dictionary<string, object?>();
        if (requestBody is not null)
        {
            arguments["request"] = requestBody;
        }

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            arguments,
            controller: null!);
    }

    [Fact]
    public async Task Filter_WhenDisabled_CallsNext()
    {
        var (filter, _) = CreateFilter(enabled: false);
        var context = CreateContext(new LoginRequest { Email = "a@b.com", Password = "12345678" });
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Filter_MissingToken_Returns400()
    {
        var (filter, _) = CreateFilter();
        var context = CreateContext(new LoginRequest
        {
            Email = "a@b.com",
            Password = "12345678",
            RecaptchaToken = null
        });

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.IsType<BadRequestObjectResult>(context.Result);
    }

    [Fact]
    public async Task Filter_ValidToken_CallsNext()
    {
        var (filter, serviceMock) = CreateFilter();
        serviceMock.Setup(s => s.VerifyAsync("valid-token", "login"))
            .ReturnsAsync((true, 0.9, (string?)null));

        var context = CreateContext(new LoginRequest
        {
            Email = "a@b.com",
            Password = "12345678",
            RecaptchaToken = "valid-token"
        });
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Filter_FailedVerification_Returns403()
    {
        var (filter, serviceMock) = CreateFilter();
        serviceMock.Setup(s => s.VerifyAsync("bad-token", "login"))
            .ReturnsAsync((false, 0.2, "reCAPTCHA score too low."));

        var context = CreateContext(new LoginRequest
        {
            Email = "a@b.com",
            Password = "12345678",
            RecaptchaToken = "bad-token"
        });

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }
}
```

- [ ] **Step 3: Run unit tests**

Run: `cd apps/api && dotnet test Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~Recaptcha" -v n`
Expected: All tests pass. (`VerifyAsync` is declared `virtual` in Task 3 so Moq can override it.)

- [ ] **Step 4: Commit**

```bash
git add apps/api/Codec.Api.Tests/Services/RecaptchaServiceTests.cs apps/api/Codec.Api.Tests/Filters/ValidateRecaptchaFilterTests.cs
git commit -m "test(api): add unit tests for RecaptchaService and ValidateRecaptchaFilter"
```

---

### Task 7: Backend — Integration Test Config

**Files:**
- Modify: `apps/api/Codec.Api.IntegrationTests/Infrastructure/CodecWebFactory.cs:52` (add Recaptcha:Enabled = false)

- [ ] **Step 1: Disable reCAPTCHA in integration test config**

In `apps/api/Codec.Api.IntegrationTests/Infrastructure/CodecWebFactory.cs`, add after the existing `builder.UseSetting` calls (around line 53):

```csharp
builder.UseSetting("Recaptcha:Enabled", "false");
```

- [ ] **Step 2: Run integration tests to verify nothing is broken**

Run: `cd apps/api && dotnet test Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj -v n`
Expected: All existing tests pass (reCAPTCHA is disabled).

- [ ] **Step 3: Commit**

```bash
git add apps/api/Codec.Api.IntegrationTests/Infrastructure/CodecWebFactory.cs
git commit -m "test(api): disable reCAPTCHA in integration test config"
```

---

### Task 8: Frontend — ApiClient and AppState Changes

**Files:**
- Modify: `apps/web/src/lib/api/client.ts:123-137` (add recaptchaToken param)
- Modify: `apps/web/src/lib/state/app-state.svelte.ts:495-501` (add recaptchaToken param)
- Modify: `apps/web/.env.example` (add PUBLIC_RECAPTCHA_SITE_KEY)

- [ ] **Step 1: Update ApiClient.register() to accept recaptchaToken**

In `apps/web/src/lib/api/client.ts`, replace the `register` method (lines 123-129):

```typescript
async register(email: string, password: string, nickname: string, recaptchaToken?: string): Promise<AuthResponse> {
	return this.requestNoRetry(`${this.baseUrl}/auth/register`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ email, password, nickname, recaptchaToken })
	});
}
```

- [ ] **Step 2: Update ApiClient.login() to accept recaptchaToken**

In `apps/web/src/lib/api/client.ts`, replace the `login` method (lines 131-137):

```typescript
async login(email: string, password: string, recaptchaToken?: string): Promise<AuthResponse> {
	return this.requestNoRetry(`${this.baseUrl}/auth/login`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ email, password, recaptchaToken })
	});
}
```

- [ ] **Step 3: Update AppState.register() and AppState.login()**

In `apps/web/src/lib/state/app-state.svelte.ts`, replace the register and login methods (lines 495-501):

```typescript
async register(email: string, password: string, nickname: string, recaptchaToken?: string): Promise<AuthResponse> {
	return this.api.register(email, password, nickname, recaptchaToken);
}

async login(email: string, password: string, recaptchaToken?: string): Promise<AuthResponse> {
	return this.api.login(email, password, recaptchaToken);
}
```

- [ ] **Step 4: Update .env.example**

In `apps/web/.env.example`, add:

```
PUBLIC_RECAPTCHA_SITE_KEY=
```

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/lib/api/client.ts apps/web/src/lib/state/app-state.svelte.ts apps/web/.env.example
git commit -m "feat(web): add recaptchaToken param to login/register API calls"
```

---

### Task 9: Frontend — LoginScreen reCAPTCHA Integration

**Files:**
- Modify: `apps/web/src/lib/components/LoginScreen.svelte` (load script, get token, add branding)

- [ ] **Step 1: Add reCAPTCHA script loading and token generation**

In `apps/web/src/lib/components/LoginScreen.svelte`, replace the `<script>` block (lines 1-58):

```svelte
<script lang="ts">
	import { fade } from 'svelte/transition';
	import { onMount } from 'svelte';
	import { PUBLIC_RECAPTCHA_SITE_KEY } from '$env/static/public';
	import { getAppState } from '$lib/state/app-state.svelte.js';

	const app = getAppState();
	const siteKey = PUBLIC_RECAPTCHA_SITE_KEY;

	let mode = $state<'signin' | 'signup'>('signin');
	let email = $state('');
	let password = $state('');
	let confirmPassword = $state('');
	let nickname = $state('');
	let error = $state('');
	let isSubmitting = $state(false);

	onMount(() => {
		if (!siteKey) return;
		const script = document.createElement('script');
		script.src = `https://www.google.com/recaptcha/api.js?render=${siteKey}`;
		script.async = true;
		document.head.appendChild(script);
		return () => script.remove();
	});

	async function getRecaptchaToken(action: string): Promise<string | undefined> {
		if (!siteKey) return undefined;
		try {
			return await (window as any).grecaptcha.execute(siteKey, { action });
		} catch {
			return undefined;
		}
	}

	function validateEmail(v: string): boolean {
		return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v);
	}

	function validate(): string | null {
		if (!email.trim()) return 'Email is required.';
		if (!validateEmail(email.trim())) return 'Enter a valid email address.';
		if (!password) return 'Password is required.';
		if (password.length < 8) return 'Password must be at least 8 characters.';
		if (mode === 'signup') {
			if (password !== confirmPassword) return 'Passwords do not match.';
			if (nickname.trim().length < 2) return 'Nickname must be at least 2 characters.';
			if (nickname.trim().length > 32) return 'Nickname must be 32 characters or fewer.';
		}
		return null;
	}

	async function handleSubmit(e: Event) {
		e.preventDefault();
		error = '';
		const validationError = validate();
		if (validationError) {
			error = validationError;
			return;
		}

		isSubmitting = true;
		try {
			const action = mode === 'signup' ? 'register' : 'login';
			const recaptchaToken = await getRecaptchaToken(action);
			const response = mode === 'signup'
				? await app.register(email.trim(), password, nickname.trim(), recaptchaToken)
				: await app.login(email.trim(), password, recaptchaToken);
			await app.handleLocalAuth(response);
		} catch (err: unknown) {
			error = err instanceof Error ? err.message : 'Something went wrong.';
		} finally {
			isSubmitting = false;
		}
	}

	function toggleMode() {
		mode = mode === 'signin' ? 'signup' : 'signin';
		error = '';
	}
</script>
```

- [ ] **Step 2: Add reCAPTCHA branding text and badge CSS**

In the template section of `LoginScreen.svelte`, add branding text after the `</form>` closing tag (after line 172) and before the closing `</div>`:

```svelte
		{#if siteKey}
			<p class="recaptcha-branding">
				This site is protected by reCAPTCHA and the Google
				<a href="https://policies.google.com/privacy" target="_blank" rel="noopener">Privacy Policy</a> and
				<a href="https://policies.google.com/terms" target="_blank" rel="noopener">Terms of Service</a> apply.
			</p>
		{/if}
```

Add to the `<style>` block (before the closing `</style>` tag):

```css
	/* ───── reCAPTCHA ───── */

	:global(.grecaptcha-badge) {
		visibility: hidden !important;
	}

	.recaptcha-branding {
		margin: 0;
		font-family: 'Space Grotesk', monospace;
		font-size: 11px;
		color: var(--text-dim);
		text-align: center;
		line-height: 1.5;
	}

	.recaptcha-branding a {
		color: var(--text-muted);
		text-decoration: underline;
		text-underline-offset: 2px;
	}

	.recaptcha-branding a:hover {
		color: var(--accent);
	}
```

- [ ] **Step 3: Verify the web app builds**

Run: `cd apps/web && npm run check`
Expected: No type errors.

Note: If `PUBLIC_RECAPTCHA_SITE_KEY` isn't set in `.env`, SvelteKit may error on import. Create a local `.env` with an empty value: `PUBLIC_RECAPTCHA_SITE_KEY=`

- [ ] **Step 4: Commit**

```bash
git add apps/web/src/lib/components/LoginScreen.svelte
git commit -m "feat(web): integrate reCAPTCHA v3 into LoginScreen"
```

---

### Task 10: Infrastructure — Bicep and Key Vault

**Files:**
- Modify: `infra/main.bicep` (add param + Key Vault secret module, around lines 13-15 and 166-174)
- Modify: `infra/modules/container-app-api.bicep` (add secret ref + env var, around lines 109-112 and 180-182)
- Modify: `infra/modules/container-app-web.bicep` (add env var, around lines 75-77)

- [ ] **Step 1: Add recaptchaSecretKey param and Key Vault secret to main.bicep**

In `infra/main.bicep`, add the parameter near the other `@secure()` params (after line 15):

```bicep
@description('reCAPTCHA v3 secret key for bot verification.')
@secure()
param recaptchaSecretKey string = ''
```

Add the Key Vault secret module after the existing Google Client ID secret module (after line 174):

```bicep
module recaptchaSecretKv 'modules/key-vault-secret.bicep' = if (!empty(recaptchaSecretKey)) {
  name: 'recaptcha-secret-key'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Recaptcha--SecretKey'
    secretValue: recaptchaSecretKey
  }
}
```

- [ ] **Step 2: Add secret ref and env var to container-app-api.bicep**

In `infra/modules/container-app-api.bicep`, add a new param:

```bicep
@description('Whether reCAPTCHA secret is configured')
param recaptchaConfigured bool = false
```

Add in the `secrets` array (after the Google Client ID secret ref, around line 112):

```bicep
{
  name: 'recaptcha-secret-key'
  keyVaultUrl: '${keyVaultUri}secrets/Recaptcha--SecretKey'
  identity: 'system'
}
```

Add in the `env` array (after the Google Client ID env var, around line 182):

```bicep
{
  name: 'Recaptcha__SecretKey'
  secretRef: 'recaptcha-secret-key'
}
{
  name: 'Recaptcha__Enabled'
  value: 'true'
}
```

To handle the case where the Key Vault secret doesn't exist (when `recaptchaSecretKey` is empty), always create the KV secret (the `main.bicep` module already uses `= ''` default, so an empty secret is stored). Remove the `if (!empty(...))` condition from the KV module:

```bicep
module recaptchaSecretKv 'modules/key-vault-secret.bicep' = {
  name: 'recaptcha-secret-key'
  params: {
    keyVaultName: keyVault.outputs.name
    secretName: 'Recaptcha--SecretKey'
    secretValue: recaptchaSecretKey
  }
}
```

Also add the `recaptchaSiteKey` param to `main.bicep` (this one is NOT `@secure()` since it's a public site key):

```bicep
@description('reCAPTCHA v3 site key (public, used in frontend).')
param recaptchaSiteKey string = ''
```

Wire the params when calling the API container app module in `main.bicep` (find the existing module call for `container-app-api.bicep` and add):

```bicep
recaptchaConfigured: !empty(recaptchaSecretKey)
```

Wire the param when calling the web container app module in `main.bicep` (find the existing module call for `container-app-web.bicep` and add):

```bicep
publicRecaptchaSiteKey: recaptchaSiteKey
```

- [ ] **Step 3: Add PUBLIC_RECAPTCHA_SITE_KEY to container-app-web.bicep**

In `infra/modules/container-app-web.bicep`, add a new param:

```bicep
@description('reCAPTCHA v3 site key for frontend')
param publicRecaptchaSiteKey string = ''
```

Add in the `env` array (after `PUBLIC_GOOGLE_CLIENT_ID`, around line 77):

```bicep
{
  name: 'PUBLIC_RECAPTCHA_SITE_KEY'
  value: publicRecaptchaSiteKey
}
```

- [ ] **Step 4: Build/validate Bicep**

Run: `az bicep build --file infra/main.bicep`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add infra/main.bicep infra/modules/container-app-api.bicep infra/modules/container-app-web.bicep
git commit -m "infra: add reCAPTCHA Key Vault secret and container app env vars"
```

---

### Task 11: CI/CD — GitHub Workflows

**Files:**
- Modify: `.github/workflows/infra.yml` (add secret params to Bicep deployment, around lines 120, 149, 177)
- Modify: `.github/workflows/cd.yml` (add build arg, around line 88)
- Modify: `.github/workflows/ci.yml` (add placeholder build arg, around line 157)

- [ ] **Step 1: Update infra.yml — add reCAPTCHA secrets to Bicep deployment**

In `.github/workflows/infra.yml`, add to each `az deployment group create` command's parameters (preview, deploy, and cert-binding steps — lines 120, 149, 177):

```yaml
recaptchaSecretKey='${{ secrets.RECAPTCHA_SECRET_KEY }}'
recaptchaSiteKey='${{ secrets.PUBLIC_RECAPTCHA_SITE_KEY }}'
```

Add these lines alongside the existing `googleClientId='${{ secrets.GOOGLE_CLIENT_ID }}'` parameter in each step.

- [ ] **Step 2: Update cd.yml — add build arg for web image**

In `.github/workflows/cd.yml`, add to the web Docker build command (around line 88):

```yaml
--build-arg PUBLIC_RECAPTCHA_SITE_KEY=${{ secrets.PUBLIC_RECAPTCHA_SITE_KEY }} \
```

Add this line alongside the existing `--build-arg PUBLIC_GOOGLE_CLIENT_ID=...` line.

- [ ] **Step 3: Update ci.yml — add placeholder build arg**

In `.github/workflows/ci.yml`, add to the web Docker build command (around line 157):

```yaml
--build-arg PUBLIC_RECAPTCHA_SITE_KEY=placeholder \
```

Add this line alongside the existing `--build-arg PUBLIC_GOOGLE_CLIENT_ID=placeholder` line.

- [ ] **Step 4: Update web Dockerfile — add build arg**

In `apps/web/Dockerfile`, add after line 7 (`ARG PUBLIC_GOOGLE_CLIENT_ID`):

```dockerfile
ARG PUBLIC_RECAPTCHA_SITE_KEY
```

And after line 9 (`ENV PUBLIC_GOOGLE_CLIENT_ID=...`):

```dockerfile
ENV PUBLIC_RECAPTCHA_SITE_KEY=${PUBLIC_RECAPTCHA_SITE_KEY}
```

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/infra.yml .github/workflows/cd.yml .github/workflows/ci.yml apps/web/Dockerfile
git commit -m "ci: add reCAPTCHA secrets to infra, CD build args, and CI placeholders"
```

---

### Task 12: Documentation — Update AUTH.md

**Files:**
- Modify: `docs/AUTH.md`

- [ ] **Step 1: Add reCAPTCHA section to AUTH.md**

In `docs/AUTH.md`, add a new section after the "Security Considerations" section (before "Troubleshooting"):

```markdown
## reCAPTCHA v3 Bot Protection

Login and registration endpoints are protected by invisible reCAPTCHA v3. This runs silently — no user interaction is required.

### How It Works

1. The frontend loads the reCAPTCHA v3 script on the login page
2. At form submit time, `grecaptcha.execute()` generates a score-based token
3. The token is sent with the login/register request body as `recaptchaToken`
4. The API's `[ValidateRecaptcha]` action filter verifies the token with Google's siteverify endpoint
5. Requests with missing tokens get 400; failed verification gets 403

### Configuration

**API (`appsettings.json`):**
```json
"Recaptcha": {
  "SecretKey": "<your-secret-key>",
  "ScoreThreshold": 0.5,
  "Enabled": true
}
```

- `SecretKey`: reCAPTCHA v3 secret key (stored in Key Vault in production)
- `ScoreThreshold`: Minimum score (0.0–1.0) to pass verification. Default 0.5.
- `Enabled`: Set to `false` to bypass verification (local dev, tests)

**Frontend (`.env`):**
```
PUBLIC_RECAPTCHA_SITE_KEY=<your-site-key>
```

### Fail-Closed Behavior

If Google's siteverify endpoint is unreachable, verification fails and the request is rejected. Google Sign-In is unaffected and remains available as a fallback.
```

- [ ] **Step 2: Commit**

```bash
git add docs/AUTH.md
git commit -m "docs: add reCAPTCHA v3 section to AUTH.md"
```

---

### Task 13: Final Verification

**Files:** None (verification only)

- [ ] **Step 1: Build the entire solution**

Run: `dotnet build Codec.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run all API unit tests**

Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj -v n`
Expected: All tests pass.

- [ ] **Step 3: Run web type checking**

Run: `cd apps/web && npm run check`
Expected: No type errors.

- [ ] **Step 4: Run web tests**

Run: `cd apps/web && npm test`
Expected: All tests pass.

- [ ] **Step 5: Validate Bicep**

Run: `az bicep build --file infra/main.bicep`
Expected: No errors.

- [ ] **Step 6: Commit any fixes if needed, then done**
