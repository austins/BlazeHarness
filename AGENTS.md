# AGENTS.md

## Commands

- `dotnet build` is the lint: analyzers run in the build and warnings are errors.
- `dotnet test` runs everything. One project: `dotnet test --project tests/BlazeHarness.Core.Tests.Unit`. One class: `dotnet test --project tests/BlazeHarness.Core.Tests.Unit -- --filter-class "*ResolvedRequestParamsTests"`.
- Running the app and its environment variables: see [README.md](README.md).
- After changing packages, commit every updated `packages.lock.json`.

## Architecture

- `BlazeHarness.Core` has no UI dependency. The agent loop emits an `IAsyncEnumerable<AgentEvent>`; the UI only consumes it.
- The agent loop, approvals and persistence stay in `BlazeHarness.Core` on `Microsoft.Extensions.AI`. The README explains why not Microsoft Agent Framework and when to revisit.
- `BlazeHarness.Web` is Blazor Server with hand-written CSS and no npm. Singleton services own chats and runs; components hold no chat state.
- All paths go through `AppPaths`. Config and other JSON files go through `JsonFileStore<T>` (atomic write, lock, watcher). A file it can't serve (`McpConfigFile` keeps raw text) follows the same reload rules: an unreadable or invalid file keeps the last good value and retries.
- API keys live in `ISecretStore` (OS keychain, owner-only file fallback), and nowhere else: `config.json`, `providers.json`, chat files and logs stay key-free.
- Every request, including `/_blazor`, passes `LocalAccessMiddleware` (Host limited to `localhost` or an IP on the listening port, same-origin Origin, CSP with `script-src 'self'`, nosniff, frame denial).
  - Pages load scripts from files, and every endpoint and middleware sits behind it.
  - There is no sign-in; the listen address (`BLAZEHARNESS_HOST`, loopback by default) is the access control.
- Web tools (`web_search`, `web_fetch`) are `IWebTool`s that need approval per host. `SafeWebClient` checks the resolved IP when connecting (public addresses only) and follows only same-site redirects; keep both properties. Search providers implement `ISearchProvider` and are fanned out by `SearchOrchestrator`.
- Keep the LLM request prefix stable: system prompt, then tools, then append-only history, with timestamps outside it.
- Project files (`AGENTS.md`, skills), tool output and persisted chat state are data. The access level and approvals gate every action, including one a resumed chat or a file asks for.

## Code conventions

- Log IDs and counts. Pass an exception as the logger's exception argument; `FileLoggerProvider` writes only its type and HTTP status. Keep `ex.Message` out of log messages: it can echo a prompt or reply.
- Make types `internal` unless another project uses them. `BlazeHarness.Core` exposes internals only to its two test projects (`InternalsVisibleTo`).
- Persistence is plain files under `AppPaths` (no database, no migrations). Prefer Microsoft packages (`Microsoft.Extensions.*`, `System.Text.Json`, `System.Threading.Channels`); add another package only when no real alternative exists.
- Read the time through the `TimeProvider` registered in `Program.cs`, injected as a required constructor parameter: `GetUtcNow()` where you'd write `DateTime.Now`/`UtcNow`, and its timers and delays. Tests pass a `FakeTimeProvider`. Add a `?? TimeProvider.System` fallback only for a caller that has no provider to pass.
- Every public type and member needs XML docs (CS1591 is an error in `src/`): `<summary>`, plus `<remarks>`, `<param>`, `<returns>` and `<exception>` where they add something. Use `<inheritdoc />` on interface implementations and `<param>` on the type for positional records. Properties start with "Gets" or "Gets or sets"; method summaries use the imperative.
- End every `#pragma warning disable` with a comment saying why.
- Await without `.ConfigureAwait(false)` unless a case really needs it. Blazor components run on the circuit's `SynchronizationContext` and must resume on it, and the host has no other context to avoid. Marshal events raised on other threads with `InvokeAsync`.
- Text that two components must agree on (result prefixes, prompt tags, markers) is one constant both reference, so the compiler keeps them in step.
- Use C# 14 extension blocks (`extension(Type receiver)`) for extension members.
- Keep solution-owned namespaces (`BlazeHarness.*`) file-local with ordinary `using` directives. Global usings and project-level `<Using Include="..."/>` are for framework and external package namespaces.

## Layout and naming

- One type per file, and the namespace matches the folder (enums, records and event types included).
- Say "chat" in code and UI names (`Chat`, `ChatService`, `chats/`); a chat in a Folder is a folder chat, one without a Folder is a general chat. Say "folder" for the directory a chat works in (`Folder`, `FolderRegistry`, `folders.json`).
- Put a type next to what it belongs to, not next to a similar name: the live `Chat` is in `Agent/`, its saved log and events are in `Chats/`. Before adding a helper, search for one that already does the job, and keep one class per name (`AtomicFile` lives only in `Config/`).
- Split a `src/` folder into sub-folders when it passes about 20 files and falls into clear groups (as `Tools/Files`, `Tools/Shell`, `Tools/Web`, `Mcp/Config`, `Agent/Events` do). Keep the types most callers use at the top level.
- To move files: `git mv`, then update the `namespace` line in each moved file and the `using` directives of everything that references it (an IDE refactoring helps but isn't required). `dotnet build` finds what is missed, including unused `using` directives. Move the tests to the mirrored folder in the same change.

## Localization

App-owned web UI text lives in typed resources (`WebStrings`), and Razor, C# and JavaScript hold no literals for it. Before adding or changing text, read the "Web UI text" section of [CONTRIBUTING.md](CONTRIBUTING.md) for key naming, translator comments and markup tags.

- Put a whole sentence in one entry. For inline markup, write `[code]…[/code]` and `[strong]…[/strong]` in the pattern and read it with `ResourceMessageFormatter.Web.FormatMarkup`. Pass a pattern's literal braces (like `${NAME}`) as arguments.
- Read text with placeholders or plurals through `ResourceMessageFormatter.Web.Format(nameof(WebStrings.Key), …)`.
- Core code that produces text for the UI returns a code plus arguments (`ValidationIssue`, `ConnectionTestResult`), mapped to resources in `BlazeHarness.Web/Resources`.
- Keep every `WebStrings.<culture>.resx` in step with English in the same change that adds, rewords or removes an English entry:
  - Add the translation yourself, following the translating rules in CONTRIBUTING.md.
  - Update the translation when the English meaning changes.
  - Delete the entry when the key is removed.
- Run the Web integration tests after touching `WebStrings.resx`.

## UI

- Responsive: usable on a phone (about 390px) and on desktop, with no horizontal page overflow (wide content like tables or code scrolls inside its own box). Use the existing `@media (width < 40rem)` breakpoint and `rem` units.
- Accessible: native controls, an `aria-label` on icon-only buttons, the `:focus-visible` outline, every action reachable and operable by keyboard, `prefers-reduced-motion` respected, and text readable in both themes.
- Motion helps entrances, state changes, and hover and press feedback. Write it in plain CSS and guard it for `prefers-reduced-motion`:
  - Express durations through `--duration`/`--duration-slow` (both `0s` under reduce).
  - Wrap looping animations in `@media (prefers-reduced-motion: no-preference)`.
  - Animate `opacity` and `transform` where you can, and leave properties `layout.js` sets while dragging (`--sidebar-width`, `--panel-width`) alone.

### Checking the UI in a browser

Use whichever browser tool the chat has (Playwright MCP, an agent browser, or none: then say the UI is unchecked).

1. Build, then run `ASPNETCORE_ENVIRONMENT=Development BLAZEHARNESS_HOME=$(mktemp -d) BLAZEHARNESS_NO_BROWSER=1 dotnet run --no-build --project src/BlazeHarness.Web`. Development is what serves `app.css`.
2. Rebuild for Razor changes, restart for CSS, and reload the page after a restart.
3. For layout or responsive changes, check about 390px (phone) and 1280px (desktop) when practical. Small cosmetic changes can skip this; say when browser checks were skipped.
4. Stop the app with `kill <PID>` from `ss -ltnp | grep 5380`. Delete screenshots, the temp home and any browser output folder.

To seed a chat without a model server, write `providers.json`, `chats/<32-hex>/chat.json` and `messages.jsonl` under the home. Copy the event shapes from `tests/BlazeHarness.Core.Tests.Unit/Chats/SampleChat.cs` and `src/BlazeHarness.Core/Chats/*Event.cs` (enums are strings). If the transcript stays empty, the app's console log names the bad field in a `JsonException`.

## Tests

### Layout

- One project per source project and kind: `<Project>.Tests.Unit` (no file I/O, processes or network) and `<Project>.Tests.Integration` (real file system, processes, fake HTTP servers). Test projects explicitly import `tests/TestProject.props`; the shared helper library does not.
- Mirror the source folder and namespace (`Secrets/ApiKeyResolverTests.cs` -> `BlazeHarness.Core.Tests.Unit.Secrets`). One test class per file.
- Put helpers used by multiple test projects in `tests/BlazeHarness.Tests.Shared` (`LoggerFake<T>`, `ScriptedChatClient`, `FakeOpenAiServer`, `TempFolder`, `TestTargets`, `TestTimeouts.Default` with `.Bounded()`, `RecordingChatLog`, `LocalHttpListener`); keep single-project helpers in that project.

### Style

- xUnit v3 on Microsoft.Testing.Platform, NSubstitute, AwesomeAssertions (`.Should()`). `Method_Scenario_Expected` names, `// Arrange` / `// Act` / `// Assert` (omit a marker whose section would be empty), class under test in `_sut`.
- Prefer the real class when cheap and deterministic (`TempFolder`, `FakeTimeProvider`, real file tools), NSubstitute for simple interface stubs, and a shared hand-written fake when it needs its own state or logic.
- Use `LoggerFake<T>` for loggers, and pass `TestContext.Current.CancellationToken` to async calls.
- `BlazeHarness.Web.Tests.E2E` is the Playwright smoke suite: derive from `TestBase`, open the app with `LaunchAsync()`, and assert with `Expect(...)`. Any test host that runs `Program` must set `HostLauncher.NoBrowserSettingKey` (plus `Staging` and `UseStaticWebAssets()` when serving static assets) so it does not open a browser.
- Real OS keyring tests are opt-in via `BLAZEHARNESS_TEST_KEYRING=1`.

### Isolation

All test projects run with `ParallelMode.All`, so keep tests deterministic and isolated:

- Wait with `FakeTimeProvider.Advance`, a `TaskCompletionSource`, or polling bounded by `TestTimeouts.Default`. Keep a tool's own timeout short or injectable when testing the timeout path.
- Share no state: a `TempFolder` per test, port 0 for servers (`LocalHttpListener` for an `HttpListener`), and no shared files, environment variables or static fields.
- Use `SkipWhen` for OS-specific features. Fix flakes at the cause.
