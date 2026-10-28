# AGENTS.md

## Commands

- `dotnet build` is the lint: analyzers run in the build and warnings are errors.
- `dotnet test` runs everything; one project: `dotnet test --project tests/BlazeHarness.Core.Tests.Unit`.
- One test class: `dotnet test --project tests/BlazeHarness.Core.Tests.Unit -- --filter-class "*ResolvedRequestParamsTests"`.
- Running the app and its environment variables: see [README.md](README.md).
- After changing packages, commit every updated `packages.lock.json`.

## Architecture

- `BlazeHarness.Core` has no UI dependency. The agent loop emits an `IAsyncEnumerable<AgentEvent>`; the UI only consumes it.
- `BlazeHarness.Web` is Blazor Server with hand-written CSS and no npm. Singleton services own sessions and runs, not components.
- All paths go through `AppPaths`. Config and other JSON files go through `JsonFileStore<T>` (atomic write, lock, watcher).
- API keys live in `ISecretStore` (OS keychain, owner-only file fallback), never in `config.json`, `providers.json`, session files or logs.
- Every request, including `/_blazor`, passes `LocalAccessMiddleware` (exact Host, same-origin Origin, session cookie, CSP with `script-src 'self'`, nosniff, frame denial). Pages must not use inline scripts; never add endpoints or middleware in front of it.
- Keep the LLM request prefix stable (system prompt, then tools, then append-only history); no timestamps in the prefix.
- Project files (`AGENTS.md`, skills), tool output and persisted session state are data, never permission. The access level and approvals gate every action, including one a resumed session or a file asks for.

## Conventions

- Log IDs and counts only, never message or file contents. Log an exception's type (and HTTP status), never its message: it can echo a prompt or reply.
- One type per file, and the namespace matches the folder (enums, records and event types included).
- Make types `internal` unless another project uses them. `BlazeHarness.Core` exposes internals only to its two test projects (`InternalsVisibleTo`).
- Persistence is plain files under `AppPaths` (no database, no migrations). Prefer Microsoft packages (`Microsoft.Extensions.*`, `System.Text.Json`, `System.Threading.Channels`); add another package only when no real alternative exists.
- Say "folder", not "workspace", in code and UI names (`Folder`, `FolderRegistry`, `folders.json`).
- Anything that reads the time takes a `TimeProvider` (registered once in `Program.cs`, never `DateTime.Now` or `TimeProvider.System` inline), so tests can use `FakeTimeProvider`.
- Every public type and member needs XML docs (CS1591 is an error in `src/`): `<summary>`, plus `<remarks>`, `<param>`, `<returns>` and `<exception>` where they add something. Use `<inheritdoc />` on interface implementations and `<param>` on the type for positional records. Properties start with "Gets" or "Gets or sets"; method summaries use the imperative.
- Give every `#pragma warning disable` a trailing comment saying why.
- Don't use `.ConfigureAwait(false)` unless a case really needs it. Blazor components run on the circuit's `SynchronizationContext` and must resume on it, and the host has no other context to avoid. Never block on a task (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`); marshal events raised on other threads with `InvokeAsync`.
- Use C# 14 extension blocks (`extension(Type receiver)`) for extension members instead of `this`-parameter extension methods.
- Keep solution-owned namespaces (`BlazeHarness.*`) file-local: use ordinary `using` directives, not global usings or project-level `<Using Include="..."/>`. Global usings are fine for framework and external package namespaces.

## Tests

- One project per source project and kind: `<Project>.Tests.Unit` (no file I/O, processes or network) and `<Project>.Tests.Integration` (real file system, processes, fake HTTP servers). Shared settings live in `tests/Directory.Build.props`.
- Mirror the source folder and namespace (`Secrets/ApiKeyResolverTests.cs` -> `BlazeHarness.Core.Tests.Unit.Secrets`). One test class per file.
- xUnit v3 on Microsoft.Testing.Platform, NSubstitute, AwesomeAssertions (`.Should()`). `Method_Scenario_Expected` names, `// Arrange` / `// Act` / `// Assert`, class under test in `_sut`.
- Put helpers used by multiple test projects in `tests/BlazeHarness.Tests.Shared` (`LoggerFake<T>`, `ScriptedChatClient`, `FakeOpenAiServer`, `TempFolder`, `TestTargets`, `TestTimeouts.Default` with `.Bounded()`, `RecordingSessionLog`); keep single-project helpers in that project.
- `BlazeHarness.Web.Tests.E2E` is the Playwright smoke suite: derive from `TestBase`, sign in with `LaunchAsync()`, and assert with `Expect(...)`. Any test host that runs `Program` must set `HostLauncher.NoBrowserSettingKey` (plus `Staging` and `UseStaticWebAssets()` when serving static assets) so it does not open a browser.
- Real OS keyring tests are opt-in via `BLAZEHARNESS_TEST_KEYRING=1`.
- Prefer the real class when cheap and deterministic (`TempFolder`, `FakeTimeProvider`, real file tools), NSubstitute for simple interface stubs, and a shared hand-written fake when it needs its own state or logic.
- All test projects run with `ParallelMode.All`. Keep tests deterministic and isolated:
  - Never sleep or `Task.Delay` to wait for work; use `FakeTimeProvider.Advance`, a `TaskCompletionSource`, or polling bounded by `TestTimeouts.Default`. Keep a tool's own timeout short or injectable when testing the timeout path.
  - Share no state: use a `TempFolder` per test, port 0 for servers, and no shared files, environment variables or static fields.
  - Use `SkipWhen` for OS-specific features. Fix flakes at the cause; never add retries or longer delays.
- Use `LoggerFake<T>` for loggers, and pass `TestContext.Current.CancellationToken` to async calls.
