Write unit tests for the remaining untested controllers and services.

## Controllers (zero unit test coverage)
1. `apps/api/Codec.Api/Controllers/AnnouncementsController.cs`
2. `apps/api/Codec.Api/Controllers/ReportsController.cs`

## Services (zero unit test coverage)
3. `apps/api/Codec.Api/Services/AdminMetricsService.cs`
4. `apps/api/Codec.Api/Services/GitHubIssueService.cs`

Note: `AzureBlobStorageService.cs` requires Azure SDK mocking — skip for now.

## Process
1. Read each source file to understand its dependencies and methods
2. Read 2 existing test files in the same directory to match patterns exactly
3. Create test file with comprehensive coverage:
   - Every public method tested
   - Happy path + error paths
   - Permission checks
   - Not-found handling
4. Run tests after each file
5. Fix all failures before moving on

## Quality Gate
```bash
dotnet test apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj
```
All tests must pass.
