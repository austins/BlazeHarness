# Contributing

Changes should follow the repository guidance in [AGENTS.md](AGENTS.md). Run `dotnet build` for code changes and the smallest relevant test command.

## Translating the web UI

The web UI text is in `src/BlazeHarness.Web/Resources/WebStrings.resx`. To add a language, copy the file and add the culture code before `.resx`, such as `WebStrings.fr.resx`. Keep the keys as they are and translate only the values. Delete entries you don't translate; missing entries fall back to English.

Resource files are XML, so a text editor or GitHub's file editor works. Keep XML escaping (`&amp;` for `&`) and named placeholders such as `{name}`. You can reorder placeholders. Each entry has a comment that says where the text appears and what its placeholders mean.

Some entries contain `[code]...[/code]` or `[strong]...[/strong]`. These tags render as monospace and bold. Keep them around the matching words, and move them with the words when you reorder a sentence. Placeholders such as `{reference}` hold literal text like `${NAME}`, so leave them alone. The `[seconds]` tag in the reconnect dialog marks where a live countdown number appears; keep it too.

Messages that depend on a count use [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/), for example `{count, plural, one {# chat} other {# chats}}`. Add the plural forms your language needs and keep `other`. Put exact-number cases such as `=0` before the named cases, because MessageFormat 8 checks them in order. Keep every placeholder, including those inside plural or select cases.

Add the culture to the supported UI culture list in `src/BlazeHarness.Web/Program.cs`. Before opening a pull request, run `dotnet build` and the Web integration tests. The tests fail on unused English entries, numbered keys, missing or boilerplate comments, translation keys that don't exist in English, and mismatched placeholders. Partial translations pass. Code must pass keys that take placeholders as `nameof(WebStrings.Key)`.

Approval reasons, MCP server errors and exception messages are still in English.
