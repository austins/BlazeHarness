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
- API keys live in `ISecretStore` (OS keychain, owner-only file fallback; the file and DPAPI stores share `FileBackedSecretStore`), never in `config.json`, session files or logs.
- Every request, including `/_blazor`, passes `LocalAccessMiddleware` (exact Host, same-origin Origin, session cookie). It also sets the page security headers (CSP with `script-src 'self'`, nosniff, frame denial), so pages must not use inline scripts.
  Don't add endpoints or pipeline steps in front of it.
- Keep the LLM request prefix stable (system prompt, then tools, then append-only history); no timestamps in the prefix.

## Conventions

- Log IDs and counts only, never message or file contents. Log an exception's type (and HTTP status), never its message: it can echo a prompt or reply.
- One type per file, and the namespace matches the folder (enums, records and event types included).
- Anything that reads the time takes a `TimeProvider` (registered once in `Program.cs`, never `DateTime.Now` or `TimeProvider.System` inline), so tests can use `FakeTimeProvider`.
- Namespaces under `BlazeHarness.Core`: `Agent`, `Providers`, `Folders`, `Tools`, `Sessions`, `Compaction`, `Sandbox`, `Mcp`, `Skills`, `Config` (`AppConfig`, `AtomicFile`, `JsonFileStore<T>`), `Secrets`.
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
- Helpers used by more than one test project go in `tests/BlazeHarness.Tests.Shared` (`LoggerFake<T>`, `ScriptedChatClient`, `FakeOpenAiServer` on a Kestrel port 0, `TempFolder`, `TestTargets`, `TestTimeouts.Default` with `.Bounded()`, `FakeTimeProviderExtensions.AdvanceUntilAsync`, `ChatRunnerCollector`). Shared helpers use only the public API, and their namespaces match their folders. Every project sets `Parallelization(Mode = ParallelMode.All)` in its `AssemblyInfo.cs`, so all tests run in parallel.
  - `BlazeHarness.Web.Tests.Integration` holds endpoint and host tests that need real files (`Hosting/`).
  - Tests that touch the real OS keyring or Keychain are opt-in: set `BLAZEHARNESS_TEST_KEYRING=1`.
  - Unit projects do no file I/O; anything that touches the file system belongs in the matching Integration project.
- Mocks and fakes:
  - Use NSubstitute for an interface where a test needs a canned answer, a thrown exception, or a check that a call was or wasn't made (an approver that says yes, a tool that throws or hangs).
  - Write a hand-made fake when it needs state or logic of its own: replaying a script of streamed responses (`ScriptedChatClient`), measuring concurrency (`FakeTool`), keeping events in memory (`RecordingSessionLog`), or recording log entries (`LoggerFake<T>`). A fake used by more than one test class should stay a real class so its behavior is written once.
  - Prefer the real class when it is cheap and deterministic (temp folders, `FakeTimeProvider`, the real file tools) over either.
- Keep tests deterministic, so they pass the same on a slow CI machine and when run in parallel:
  - No timing: never sleep or `Task.Delay` and then assert. Anything that reads the time takes a `TimeProvider`, so tests use `FakeTimeProvider` and `Advance`.
  - To wait for something real (a file, a process, an event), poll for the condition or await a `TaskCompletionSource`, with a generous bound (30 s) that only matters when the test is already failing. A negative check ("it did not happen") may wait a fixed time only when waiting too short can't make it fail.
  - Tests that run in parallel must not share state: use a unique temp folder per test (deleted in `Dispose`), port 0 for servers, and no shared files, environment variables or static fields. Don't depend on test order.
  - Use a timeout only to make a hung test fail, never as part of what the test checks. Keep a tool's own timeouts short or injectable so a test of the timeout path doesn't take seconds.
  - Don't depend on the machine: no real network, no fixed ports, no assumptions about the current directory, locale, time zone or file system case sensitivity. Skip a test that needs an OS feature (`SkipWhen`) instead of letting it fail elsewhere.
  - If a test fails once in a while, treat it as a bug in the test or the code and fix the cause. Don't add retries or longer delays.
- Use `LoggerFake<T>` for loggers, and pass `TestContext.Current.CancellationToken` to async calls.
