# Contributing

Changes should follow the repository guidance in [AGENTS.md](AGENTS.md). Run `dotnet build` for code changes and the smallest relevant test command.

## Translating the web UI

The web UI is being moved into `src/BlazeHarness.Web/Resources/WebStrings.resx`. To add a language, copy the resource file and add the culture code before `.resx`, such as `WebStrings.fr.resx`. Keep the resource keys unchanged and translate only the values. Remove entries you haven't translated; missing entries fall back to English.

Resource files are XML and can be edited in a text editor or GitHub's file editor. Preserve XML escaping (`&amp;` for `&`) and named placeholders such as `{name}`; they can be reordered. Each entry has a comment describing where it appears. Some entries contain `[code]…[/code]` or `[strong]…[/strong]`: keep these tags around the matching words (they render as monospace or bold) and move them with the words when you reorder a sentence. Text such as `${NAME}` is passed in as a placeholder, so leave placeholders like `{reference}` untouched.

Count-dependent messages use [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/), for example `{count, plural, one {# chat} other {# chats}}`. Add the plural forms needed by the target language and keep `other`. Put exact-number cases such as `=0` before named cases: MessageFormat 8 processes them in order. Format placeholders must be retained, including ones nested in plural or select cases.

Add the culture to the supported UI culture list in `src/BlazeHarness.Web/Program.cs`. Run `dotnet build` and the Web integration tests before opening a pull request. The resource tests check for unused English entries, numbered keys, missing or boilerplate context, unknown translation keys, and mismatched format placeholders. They allow partial translations. Code must pass formatted resource keys as `nameof(WebStrings.Key)`. Approval reasons, MCP server errors and exception messages stay in English for now.
