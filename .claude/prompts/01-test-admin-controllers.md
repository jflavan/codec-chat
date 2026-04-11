Write unit tests for the untested admin controllers. Work through them one at a time.

## Target Files (zero coverage)
1. `apps/api/Codec.Api/Controllers/AdminMessagesController.cs`
2. `apps/api/Codec.Api/Controllers/AdminReportsController.cs`
3. `apps/api/Codec.Api/Controllers/AdminServersController.cs`
4. `apps/api/Codec.Api/Controllers/AdminStatsController.cs`
5. `apps/api/Codec.Api/Controllers/AdminSystemController.cs`
6. `apps/api/Codec.Api/Controllers/AdminUsersController.cs`

## Process for Each Controller
1. Read the controller to understand its dependencies and endpoints
2. Read an existing test file (e.g., `ServersControllerTests.cs`) to match patterns
3. Create the test file in `apps/api/Codec.Api.Tests/Controllers/`
4. Test every public action method with at least:
   - Happy path (returns expected result)
   - Auth/permission failure path
   - Not-found path (where applicable)
   - Validation error path (where applicable)
5. Run: `dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj --filter "FullyQualifiedName~<TestClassName>"`
6. Fix any failures before moving to the next controller

## Patterns
- Use `Moq` for service mocks
- Use `FluentAssertions` for assertions
- Set up `ClaimsPrincipal` with admin claims for [Authorize] endpoints
- Use `[Fact]` for single cases, `[Theory]` for parameterized tests
- Name: `MethodName_WhenCondition_ExpectedResult`

## Quality Gate
After writing all tests, run the full suite:
```bash
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj
```
All tests must pass before committing.
