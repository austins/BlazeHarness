# Contributing

Follow the repository guidance in [AGENTS.md](AGENTS.md). It applies to people as well as agents.

## Before you open a pull request

1. Run `dotnet build`. Analyzers run in the build and warnings are errors.
2. Run the smallest test command that covers your change. If you touched web UI text, run the Web integration tests (see [Checks](#checks)).

## Packaging

The [release workflow](.github/workflows/deploy.yml) uses `src/BlazeHarness.Web/wwwroot/favicon.svg` for Linux and macOS icons. On macOS, it installs `librsvg` to render the icon sizes and uses `iconutil` to build the app's `.icns` file. The mounted DMG uses the same icon, with its custom-icon flag set before compression.

## Web UI text

### Adding text

Don't hard-code text a translator would change in Razor, C# or JavaScript. Add an entry to `WebStrings.resx` and read it with `nameof(WebStrings.Key)`.

* Write text that reads the same in every language inline instead: file names, format and protocol names, and sample values in placeholders. Use `AppConstants.AppName` for the product name, or `AppConstants.AppId` (no space) where a name can't hold a space, such as a user agent.
* Name keys `<Component><Purpose>`, such as `ChatListDeleteWarning`, and never by number.
* Give each new English entry a comment that says where the text appears, what each placeholder is, and what not to translate.
* Remove an entry when its last use is removed.
* Keep user, model and tool content, approval reasons, exception text and protocol identifiers in their original language. MCP server errors are also still in English.

### Translating

The web UI text is in `src/BlazeHarness.Web/Resources/Strings/WebStrings.resx`.

#### Adding a language

Copy the file and add the culture code before `.resx`, such as `WebStrings.fr.resx`. Keep the keys as they are and translate only the values. Delete entries you don't translate; missing entries fall back to English.

You don't need to register a new language in code. The Display language list in Settings is built from the translated resource files the app is built with.

When you add, reword or remove an English entry, make the same change in every existing `WebStrings.<culture>.resx`, so the translations stay in step.

#### Editing resource files

Resource files are XML, so a text editor or GitHub's file editor works. Keep XML escaping (`&amp;` for `&`). Each entry has a comment that says where the text appears and what its placeholders mean.

* Keep named placeholders such as `{name}`. You can reorder them.
* Some entries contain `[code]...[/code]` or `[strong]...[/strong]`. These tags render as monospace and bold. Keep them around the matching words, and move them with the words when you reorder a sentence.
* Placeholders such as `{reference}` hold literal text like `${NAME}`, so leave them alone.
* The `[seconds]` tag in the reconnect dialog marks where a live countdown number appears. Keep it too.

#### Plurals

Messages that depend on a count use [ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/), for example `{count, plural, one {# chat} other {# chats}}`.

* Add the plural forms your language needs and keep `other`.
* Put exact-number cases such as `=0` before the named cases, because MessageFormat 8 checks them in order.
* Keep every placeholder, including those inside plural or select cases.

### Checks

Run the Web integration tests after changing any `.resx` file:

```bash
dotnet test --project tests/BlazeHarness.Web.Tests.Integration
```

The tests fail on unused English entries, numbered keys, missing or boilerplate comments, translation keys that don't exist in English, and mismatched placeholders. Partial translations pass. Code must pass keys that take placeholders as `nameof(WebStrings.Key)`.
