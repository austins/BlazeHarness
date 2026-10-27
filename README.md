# BlazeHarness

A local web app for chatting and coding with any OpenAI-compatible LLM. It runs on your machine, on Windows, macOS and Linux, and gives the model tools to read, edit and search files and run shell commands, with you approving what it may do.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An OpenAI-compatible endpoint and API key (you add providers in the app's settings)

## Running

```bash
dotnet run --project src/BlazeHarness.Web
```

The app binds a free port on `127.0.0.1`, prints a one-time launch URL, and opens your browser. Options:

| Option | Environment variable | Effect |
|---|---|---|
| `--port N` | `BLAZEHARNESS_PORT` | Pin the port instead of picking a free one. |
| `--no-browser` | `BLAZEHARNESS_NO_BROWSER=1` | Don't open the browser; use the printed URL. |

The app stores everything under `~/.blazeharness` (`%USERPROFILE%\.blazeharness` on Windows). Set `BLAZEHARNESS_HOME` to use another folder, for example a temp folder when experimenting.

API keys are kept in your OS secret store (Windows Credential Manager, macOS Keychain or Linux Secret Service). If none is available, they fall back to an owner-only file. They are never written to `config.json`, session files or logs.

## Features

- **Chat and coding in one interface** with any OpenAI-compatible endpoint, with streamed replies and multiple providers.
- **Tools** for shell commands, reading, writing and editing files, listing folders, and searching files. Read-only tools run in parallel.
- **Approvals**: you decide, per tool and per action, what runs without asking.
- **Shell sandboxing** where the platform supports it, so commands can be confined to the folder you opened. Otherwise commands run directly, behind approvals.
- **Folders and sessions**: open a folder and run sessions inside it, grouped by folder. Sessions can also exist without a folder for general chat.
- **Persistent history** in plain, human-readable files, with context compaction for long conversations.
- **Diffs, syntax highlighting and find** in the chat, with smooth scrolling in very long chats.
- **Export and import** of sessions.
- **MCP servers**: add and configure servers from a standard `mcp.json`, enabled globally or per session.
- **Skills**: `SKILL.md` folders from `~/.blazeharness/skills`, `~/.agents/skills`, and a folder's own `.agents/skills`, loaded on demand.

## Planned and ideas

Not built yet, and not promised:

- **Mobile browser support**: a touch-friendly layout for use from a phone or tablet. This needs a security review first, since the app is localhost-only by design.
- **Localization** of the web UI, starting with English.
- **In-app browser** that the agent can use to open a running service, read the page and take a screenshot.
- **"Ask user" tool** that lets the agent pause and ask you a question, with free text or choices.
- **Attachments as tool input**, so a text-only model can pass an uploaded image to a vision tool.
- **Session history tool** that lets the agent read earlier messages, to recover detail that compaction dropped.
- **Subagents** that work in their own context and return a summary.
- **Blazor AI components**, once they are stable on the target .NET version.
- **Smaller additions**: a command palette, a richer MCP editor with per-tool toggles, dynamic MCP tool loading, a sandbox network allow-list, message search across sessions, a duplicate-provider action, and a per-folder `.mcp.json`.

## Not goals

- Multi-user hosting or scaling.
- Offline or WebAssembly operation.
- A UI component framework or an npm toolchain.
