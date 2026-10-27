# AGENTS.md

## Commands

- `dotnet build` is the lint: analyzers run in the build and warnings are errors.
- `dotnet test` runs everything; one project: `dotnet test --project tests/BlazeHarness.Core.Tests.Unit`.
- One test class: `dotnet test --project tests/BlazeHarness.Core.Tests.Unit -- --filter-class "*ResolvedRequestParamsTests"`.
- Run the app: `dotnet run --project src/BlazeHarness.Web`. It binds a free port on `127.0.0.1`, prints a one-time launch URL
  and opens the browser (`--no-browser` or `BLAZEHARNESS_NO_BROWSER=1` to skip, `--port` or `BLAZEHARNESS_PORT` to pin a port).
- `BLAZEHARNESS_HOME` overrides `~/.blazeharness`; point it at a temp folder when experimenting.
- After changing packages, commit every updated `packages.lock.json`.

## Architecture

- 
- `BlazeHarness.Core` has no UI dependency. The agent loop emits an `IAsyncEnumerable<AgentEvent>`; the UI only consumes it.
- `BlazeHarness.Web` is Blazor Server with hand-written CSS and no npm. Singleton services own sessions and runs, not components.
- All paths go through `AppPaths`. Config and other JSON files go through `JsonFileStore<T>` (atomic write, lock, watcher).
- API keys live in `ISecretStore` (OS keychain, owner-only file fallback), never in `config.json`, session files or logs.
- Every request, including `/_blazor`, passes `LocalAccessMiddleware` (exact Host, same-origin Origin, session cookie).
  Don't add endpoints or pipeline steps in front of it.
- Keep the LLM request prefix stable (system prompt, then tools, then append-only history); no timestamps in the prefix.

## Conventions

- Log IDs and counts only, never message or file contents.
- Anything that reads the time takes a `TimeProvider` (registered once in `Program.cs`, never `DateTime.Now` or `TimeProvider.System` inline), so tests can use `FakeTimeProvider`.
- Namespaces under `BlazeHarness.Core`: `Agent`, `Providers`, `Folders`, `Tools`, `Sessions`, `Compaction`, `Sandbox`, `Mcp`, `Skills`.
- Every public type and member needs XML docs (CS1591 is an error in `src/`): a `<summary>`, plus `<remarks>`, `<param>`,
  `<returns>` and `<exception>` where they add something. Use `<inheritdoc />` on interface implementations, and `<param>`
  on the type for positional records. Properties start with "Gets" or "Gets or sets". Method summaries use the imperative.

## Tests

- One test project per source project and kind: `<Project>.Tests.Unit` (no I/O or network) and `<Project>.Tests.Integration`
  (real file system, processes, fake HTTP servers). Shared settings live in `tests/Directory.Build.props`.
- Test files mirror the source layout: a test for `BlazeHarness.Core.Secrets.ApiKeyResolver` lives in
  `Secrets/ApiKeyResolverTests.cs` and uses namespace `BlazeHarness.Core.Tests.Unit.Secrets`. One test class per file.
- xUnit v3 on Microsoft.Testing.Platform, NSubstitute, AwesomeAssertions (`.Should()`).
- `Method_Scenario_Expected` names, `// Arrange` / `// Act` / `// Assert`, class under test in `_sut`.
- Helpers used by more than one test project go in `tests/BlazeHarness.Tests.Shared` (`LoggerFake<T>`, `ScriptedChatClient`). Parallelism is set per project in its `AssemblyInfo.cs`: unit projects run all tests in parallel, the integration project uses the default.
- Use `LoggerFake<T>` for loggers, and pass `TestContext.Current.CancellationToken` to async calls.
