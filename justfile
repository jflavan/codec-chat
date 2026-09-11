# Codec — development task runner
# https://just.systems  |  run `just` to see all recipes
#
# Requires: .NET SDK 10, Node 22+, Docker.
# On Windows, recipes run under `sh` (provided by Git for Windows).

api      := "apps/api/Codec.Api"
api_test := "apps/api/Codec.Api.Tests/Codec.Api.Tests.csproj"
api_itst := "apps/api/Codec.Api.IntegrationTests/Codec.Api.IntegrationTests.csproj"
apphost  := "apps/aspire/Codec.AppHost"
web      := "apps/web"
admin    := "apps/admin"
sln      := "Codec.sln"

# Read-only tools the scout agent is allowed to use (comma-separated: entries contain spaces)
scout_tools := "Read,Grep,Glob,Bash(just plan*),Bash(just issues*),Bash(just prs*),Bash(just deps-*),Bash(just todo*),Bash(just changed*),Bash(just project-graph*),Bash(git log*),Bash(git diff*),Bash(git status*),Bash(git show*)"

# List all available recipes
default:
    @just --list --unsorted

# ---------------------------------------------------------------- setup ----

# Install all dependencies (npm ci + dotnet restore)
[group('setup')]
install: install-web install-admin restore

# Install web dependencies
[group('setup')]
install-web:
    cd {{web}} && npm ci

# Install admin dependencies
[group('setup')]
install-admin:
    cd {{admin}} && npm ci

# Restore all .NET packages
[group('setup')]
restore:
    dotnet restore {{sln}}

# Create .env files from .env.example where missing
[group('setup')]
init-env:
    #!/usr/bin/env sh
    set -eu
    for d in {{web}} {{admin}}; do
        if [ -f "$d/.env" ]; then
            echo "$d/.env already exists, leaving it alone"
        else
            cp -f "$d/.env.example" "$d/.env"
            echo "created $d/.env — fill in your values"
        fi
    done

# Install the dotnet-ef global tool (needed for migrations)
[group('setup')]
install-ef:
    dotnet tool install --global dotnet-ef || dotnet tool update --global dotnet-ef

# Full first-time setup
[group('setup')]
bootstrap: init-env install install-ef
    @echo "Setup complete. Set Google:ClientId, then run: just dev"

# Set the Google OAuth client ID in Aspire user secrets
[group('setup')]
set-google-client-id client_id:
    cd {{apphost}} && dotnet user-secrets set "Google:ClientId" "{{client_id}}"

# ------------------------------------------------------------------ dev ----

# Run the whole stack via Aspire (dashboard at https://localhost:17222)
[group('dev')]
dev:
    cd {{apphost}} && dotnet run

# Run the API alone at http://localhost:5050
[group('dev')]
dev-api:
    cd {{api}} && dotnet run

# Run the API with hot reload
[group('dev')]
watch-api:
    cd {{api}} && dotnet watch run

# Run the web app alone at http://localhost:5174
[group('dev')]
dev-web:
    cd {{web}} && npm run dev

# Run the admin app alone at http://localhost:5175
[group('dev')]
dev-admin:
    cd {{admin}} && npm run dev

# Start backing services only (postgres, redis, azurite)
[group('dev')]
services-up:
    docker compose up -d postgres redis azurite

# Stop backing services
[group('dev')]
services-down:
    docker compose stop postgres redis azurite

# Tail logs from the backing services
[group('dev')]
services-logs *args:
    docker compose logs -f {{args}}

# ---------------------------------------------------------------- build ----

# Build everything (API, web, admin)
[group('build')]
build: build-api build-web build-admin

# Build the API project
[group('build')]
build-api config="Release":
    dotnet build {{api}} --configuration {{config}}

# Build every .NET project, including tests and the Aspire host
[group('build')]
build-sln config="Release":
    dotnet build {{sln}} --configuration {{config}}

# Build all .NET projects with warnings treated as errors
[group('build')]
build-strict:
    dotnet build {{sln}} --configuration Release -warnaserror

# Force a full, non-incremental .NET rebuild
[group('build')]
rebuild config="Release":
    dotnet build {{sln}} --configuration {{config}} --no-incremental

# Build the web app (bakes PUBLIC_* from apps/web/.env into the bundle)
[group('build')]
build-web:
    cd {{web}} && npm run build

# Build the admin app
[group('build')]
build-admin:
    cd {{admin}} && npm run build

# Serve the production web build
[group('build')]
preview-web:
    cd {{web}} && npm run preview

# Serve the production admin build
[group('build')]
preview-admin:
    cd {{admin}} && npm run preview

# -------------------------------------------------------------- publish ----

# Publish all three apps into artifacts/
[group('publish')]
publish: publish-api publish-web publish-admin

# Publish the API to a deployable directory
[group('publish')]
publish-api out="artifacts/api" config="Release":
    dotnet publish {{api}} --configuration {{config}} --output {{out}}

# Build the web app and stage its Node server bundle
[group('publish')]
publish-web out="artifacts/web": build-web
    rm -rf {{out}}
    mkdir -p {{out}}
    cp -r {{web}}/build/. {{out}}/

# Build the admin app and stage its Node server bundle
[group('publish')]
publish-admin out="artifacts/admin": build-admin
    rm -rf {{out}}
    mkdir -p {{out}}
    cp -r {{admin}}/build/. {{out}}/

# ----------------------------------------------------------------- test ----

# Run every test suite (unit + integration + frontend)
[group('test')]
test: test-api test-api-integration test-web test-admin

# Run every test suite except the Docker-dependent integration tests
[group('test')]
test-fast: test-api test-web test-admin

# API unit tests (xUnit)
[group('test')]
test-api *args:
    dotnet test {{api_test}} {{args}}

# API integration tests (requires Docker for Testcontainers)
[group('test')]
test-api-integration *args:
    dotnet test {{api_itst}} {{args}}

# Web tests (Vitest)
[group('test')]
test-web *args:
    cd {{web}} && npx vitest run {{args}}

# Web tests in watch mode
[group('test')]
test-web-watch:
    cd {{web}} && npm run test:watch

# Admin tests (Vitest)
[group('test')]
test-admin *args:
    cd {{admin}} && npx vitest run {{args}}

# Coverage for web and admin
[group('test')]
coverage:
    cd {{web}} && npm run test:coverage
    cd {{admin}} && npm run test:coverage

# ---------------------------------------------------------------- check ----

# Type-check and lint web + admin
[group('check')]
check: check-web check-admin

# svelte-check + tsc + deprecated-events lint for web
[group('check')]
check-web:
    cd {{web}} && npm run check

# svelte-check + tsc for admin
[group('check')]
check-admin:
    cd {{admin}} && npm run check

# Format C# sources in place
[group('check')]
fmt:
    dotnet format {{sln}}

# Verify C# formatting without writing changes
[group('check')]
fmt-check:
    dotnet format {{sln}} --verify-no-changes

# Everything CI runs, in CI order
[group('check')]
ci: build check test

# Pre-commit gate: build, check, and every test but the slow ones
[group('check')]
verify: build check test-fast

# ------------------------------------------------------------------- db ----

# Create a new EF Core migration (writes migration + Designer.cs + snapshot)
[group('db')]
migration-add name:
    cd {{api}} && dotnet ef migrations add {{name}}

# Apply pending migrations to the dev database
[group('db')]
migrate:
    cd {{api}} && dotnet ef database update

# Print the SQL for all migrations
[group('db')]
migration-script:
    cd {{api}} && dotnet ef migrations script

# List migrations and whether they are applied
[group('db')]
migration-list:
    cd {{api}} && dotnet ef migrations list

# Remove the most recent (unapplied) migration
[group('db')]
migration-remove:
    cd {{api}} && dotnet ef migrations remove

# Open a psql shell against the dev database
[group('db')]
psql:
    docker compose exec postgres psql -U codec -d codec_dev

# --------------------------------------------------------------- docker ----

# Build and run the full stack in Docker
[group('docker')]
compose-up:
    docker compose up -d --build

# Stop the full Docker stack
[group('docker')]
compose-down:
    docker compose down

# Stop the Docker stack and delete its volumes
[confirm("This deletes the postgres volume. Continue?")]
[group('docker')]
compose-reset:
    docker compose down -v

# Build all three container images (same args CD uses)
[group('docker')]
images tag="dev": (image-api tag) (image-web tag) (image-admin tag)

# Build the API container image
[group('docker')]
image-api tag="dev":
    docker build -t codec-api:{{tag}} ./apps/api

# Build the web container image, forwarding PUBLIC_* from apps/web/.env
[group('docker')]
image-web tag="dev":
    #!/usr/bin/env sh
    set -eu
    if [ -f "{{web}}/.env" ]; then
        . "./{{web}}/.env"
    else
        echo "warning: no {{web}}/.env — building with empty PUBLIC_* args"
    fi
    docker build --build-arg PUBLIC_API_BASE_URL="${PUBLIC_API_BASE_URL:-}" --build-arg PUBLIC_GOOGLE_CLIENT_ID="${PUBLIC_GOOGLE_CLIENT_ID:-}" --build-arg PUBLIC_RECAPTCHA_SITE_KEY="${PUBLIC_RECAPTCHA_SITE_KEY:-}" --build-arg PUBLIC_GIPHY_API_KEY="${PUBLIC_GIPHY_API_KEY:-}" --build-arg PUBLIC_LIVEKIT_URL="${PUBLIC_LIVEKIT_URL:-}" -t codec-web:{{tag}} ./{{web}}

# Build the admin container image, forwarding PUBLIC_* from apps/admin/.env
[group('docker')]
image-admin tag="dev":
    #!/usr/bin/env sh
    set -eu
    if [ -f "{{admin}}/.env" ]; then
        . "./{{admin}}/.env"
    else
        echo "warning: no {{admin}}/.env — building with empty PUBLIC_* args"
    fi
    docker build --build-arg PUBLIC_API_BASE_URL="${PUBLIC_API_BASE_URL:-}" --build-arg PUBLIC_GOOGLE_CLIENT_ID="${PUBLIC_GOOGLE_CLIENT_ID:-}" -t codec-admin:{{tag}} ./{{admin}}

# ---------------------------------------------------------------- infra ----

# Validate the Bicep templates
[group('infra')]
bicep-build:
    az bicep build --file infra/main.bicep

# ----------------------------------------------------------------- plan ----

# Show the Next steps backlog from PLAN.md
[group('plan')]
plan:
    @awk '/^## Next steps/{f=1;print;next} /^## /{f=0} f' PLAN.md

# Show a PLAN.md section by heading text, e.g. just plan-section "Server Settings"
[group('plan')]
plan-section pattern:
    @awk -v p="{{pattern}}" '/^#+ /{ n = index($0, " ") - 1; if (f && n <= lvl) f = 0; if (!f && index($0, p) > 0) { f = 1; lvl = n } } f' PLAN.md

# List open task items across PLAN.md and docs/plans/
[group('plan')]
plan-tasks:
    #!/usr/bin/env sh
    found=0
    for f in PLAN.md docs/plans/*.md; do
        [ -f "$f" ] || continue
        if grep -q '^[[:space:]]*- \[ \]' "$f"; then
            found=1
            echo "-- $f"
            grep -n '^[[:space:]]*- \[ \]' "$f" | sed 's/^/  /'
        fi
    done
    if [ "$found" = 0 ]; then
        echo "No open task items. Remaining work is in the PLAN.md Next steps list (just plan)."
    fi

# List plan and design docs, newest first
[group('plan')]
plan-docs:
    @ls -1 docs/plans | sort -r

# Scaffold a dated plan doc in docs/plans/ (gitignored) from docs/plan-template.md
[group('plan')]
plan-new name:
    #!/usr/bin/env sh
    set -eu
    f="docs/plans/$(date +%Y-%m-%d)-{{name}}.md"
    if [ -e "$f" ]; then
        echo "$f already exists"
        exit 1
    fi
    sed "s/{TITLE}/{{name}}/" docs/plan-template.md > "$f"
    echo "created $f"

# Open GitHub issues
[group('plan')]
issues *args:
    @gh issue list {{args}}

# Open pull requests
[group('plan')]
prs *args:
    @gh pr list {{args}}

# TODO/FIXME/HACK markers in application code
[group('plan')]
todo:
    #!/usr/bin/env sh
    grep -rIn --include='*.cs' --include='*.ts' --include='*.svelte' -E 'TODO|FIXME|HACK' apps || echo "No TODO/FIXME/HACK markers found."

# Run 3 scout agents in parallel on a prompt; reports land in artifacts/scout/
[group('plan')]
scout prompt="What are the top 3 things we should do next?" n="3":
    #!/usr/bin/env sh
    set -eu
    command -v claude >/dev/null 2>&1 || { echo "claude CLI not found on PATH"; exit 1; }
    out="artifacts/scout/$(date +%Y%m%d-%H%M%S)"
    mkdir -p "$out"
    echo "Prompt: {{prompt}}"
    echo "Running {{n}} scouts in parallel, writing to $out"
    echo ""
    i=1
    while [ "$i" -le {{n}} ]; do
        printf '%s' "You are scout $i of {{n}} surveying this repo independently; other scouts are running at the same time and you cannot see their work. {{prompt}}" | claude -p --agent scout --allowedTools "{{scout_tools}}" > "$out/scout-$i.md" 2> "$out/scout-$i.log" &
        echo "  started scout $i (pid $!)"
        i=$((i + 1))
    done
    wait
    echo ""
    for f in "$out"/scout-*.md; do
        echo "== $f"
        if [ -s "$f" ]; then
            grep -E '^## ' "$f" | sed 's/^/   /' || echo "   (no ranked items found)"
        else
            echo "   (empty — see ${f%.md}.log)"
        fi
        echo ""
    done
    echo "Full reports: $out"

# --------------------------------------------------------------- impact ----

[private]
_changed base:
    #!/usr/bin/env sh
    { git diff --name-only "{{base}}...HEAD" 2>/dev/null || true; git status --porcelain | cut -c4-; } | sed 's/.* -> //' | sed '/^$/d' | sort -u

[private]
_targets base:
    #!/usr/bin/env sh
    set -eu
    just _changed {{base}} | awk '/^apps\/api\// || /^apps\/aspire\// || /^Codec\.sln$/ || /^global\.json$/ { print "api" } /^apps\/web\// { print "web" } /^apps\/admin\// { print "admin" } /^infra\// { print "infra" }' | sort -u

# Files changed vs a base ref, including uncommitted work
[group('impact')]
changed base="main":
    @just _changed {{base}}

# Work out which builds, checks and tests your diff actually requires
[group('impact')]
plan-build base="main":
    #!/usr/bin/env sh
    set -eu
    t=$(just _targets {{base}})
    if [ -z "$t" ]; then
        echo "No app code changed vs {{base}} — nothing to build."
        exit 0
    fi
    echo "Affected by your diff vs {{base}}:"
    echo "$t" | sed 's/^/  /'
    echo ""
    echo "Recommended:"
    echo "$t" | while read -r x; do
        case "$x" in
            api)   echo "  just build-api && just test-api && just test-api-integration" ;;
            web)   echo "  just build-web && just check-web && just test-web" ;;
            admin) echo "  just build-admin && just check-admin && just test-admin" ;;
            infra) echo "  just bicep-build" ;;
        esac
    done
    echo ""
    echo "Or run the fast subset automatically: just affected {{base}}"

# Build, check and test only what your diff affects
[group('impact')]
affected base="main":
    #!/usr/bin/env sh
    set -eu
    t=$(just _targets {{base}})
    if [ -z "$t" ]; then
        echo "No app code changed vs {{base}} — nothing to run."
        exit 0
    fi
    for x in $t; do
        case "$x" in
            api)   just build-api; just test-api ;;
            web)   just build-web; just check-web; just test-web ;;
            admin) just build-admin; just check-admin; just test-admin ;;
            infra) echo "infra changed — run: just bicep-build" ;;
        esac
    done

# Project reference graph for the .NET projects
[group('impact')]
project-graph:
    #!/usr/bin/env sh
    set -eu
    dotnet sln {{sln}} list | grep '\.csproj$' | while read -r proj; do
        echo "-- $proj"
        dotnet list "$proj" reference 2>/dev/null | grep '\.csproj$' | sed 's/^/     /' || echo "     (no project references)"
    done

# ----------------------------------------------------------------- deps ----

# Full dependency report: outdated, vulnerable and deprecated packages
[group('deps')]
deps: deps-outdated deps-vulnerable deps-deprecated

# Packages with newer versions available
[group('deps')]
deps-outdated:
    #!/usr/bin/env sh
    echo "== apps/web =="
    (cd {{web}} && npm outdated) || true
    echo "== apps/admin =="
    (cd {{admin}} && npm outdated) || true
    echo "== .NET =="
    dotnet list {{sln}} package --outdated || true

# Packages with known vulnerabilities (direct and transitive)
[group('deps')]
deps-vulnerable:
    #!/usr/bin/env sh
    echo "== apps/web =="
    (cd {{web}} && npm audit) || true
    echo "== apps/admin =="
    (cd {{admin}} && npm audit) || true
    echo "== .NET =="
    dotnet list {{sln}} package --vulnerable --include-transitive || true

# Packages the authors have marked deprecated
[group('deps')]
deps-deprecated:
    @dotnet list {{sln}} package --deprecated || true

# Update npm packages within their semver ranges, then refresh lockfiles
[group('deps')]
upgrade-npm: upgrade-web upgrade-admin

# Update web packages within their semver ranges
[group('deps')]
upgrade-web:
    cd {{web}} && npm update && npm install

# Update admin packages within their semver ranges
[group('deps')]
upgrade-admin:
    cd {{admin}} && npm update && npm install

# Bump web packages across major versions (npm-check-updates), then reinstall
[confirm("Rewrites apps/web/package.json with new major versions. Continue?")]
[group('deps')]
upgrade-web-major:
    cd {{web}} && npx --yes npm-check-updates -u && npm install

# Bump admin packages across major versions (npm-check-updates), then reinstall
[confirm("Rewrites apps/admin/package.json with new major versions. Continue?")]
[group('deps')]
upgrade-admin-major:
    cd {{admin}} && npx --yes npm-check-updates -u && npm install

# Apply npm's non-breaking security fixes
[group('deps')]
upgrade-audit-fix:
    cd {{web}} && npm audit fix
    cd {{admin}} && npm audit fix

# Add or upgrade a NuGet package, e.g. just upgrade-nuget Serilog 4.0.0
[group('deps')]
upgrade-nuget package version="" project=api:
    #!/usr/bin/env sh
    set -eu
    if [ -n "{{version}}" ]; then
        dotnet add "{{project}}" package "{{package}}" --version "{{version}}"
    else
        dotnet add "{{project}}" package "{{package}}"
    fi

# Verify an upgrade did not break anything
[group('deps')]
upgrade-verify: install build check test-fast

# ----------------------------------------------------------------- misc ----

# Delete build output (bin, obj, .svelte-kit, build, artifacts)
[confirm("Delete all build output? Continue?")]
[group('misc')]
clean:
    dotnet clean {{sln}}
    rm -rf {{web}}/.svelte-kit {{web}}/build {{admin}}/.svelte-kit {{admin}}/build artifacts

# Delete build output and node_modules
[confirm("Delete build output AND node_modules? Continue?")]
[group('misc')]
clean-all: clean
    rm -rf {{web}}/node_modules {{admin}}/node_modules
