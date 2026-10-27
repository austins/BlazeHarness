# BlazeHarness

A local web app for chatting and coding with any OpenAI-compatible LLM. It runs on Windows, macOS and Linux. The model can read, edit and search files and run shell commands, and you approve what it may do.

⚠ This app is still in early development. It is not yet suitable for production use.

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* An OpenAI-compatible endpoint and API key. You add providers in the app's settings.

## Running

```bash
dotnet run --project src/BlazeHarness.Web
```

The app binds a free port on `127.0.0.1`, prints a one-time launch URL, and opens your browser. Options:

| Option | Environment variable | Effect |
|---|---|---|
| `--port N` | `BLAZEHARNESS_PORT` | Pin the port instead of picking a free one. |
| `--no-browser` | `BLAZEHARNESS_NO_BROWSER=1` | Don't open the browser; use the printed URL. |

The app stores everything under `~/.blazeharness` (`%USERPROFILE%\.blazeharness` on Windows). Set `BLAZEHARNESS_HOME` to use another folder, for example a temp folder when experimenting. Providers and models are in `providers.json`, and defaults and permissions are in `config.json`.

API keys are kept in your OS secret store (Windows Credential Manager, macOS Keychain or Linux Secret Service). If none is available, they fall back to an owner-only file. They are never written to `config.json`, `providers.json`, session files or logs.

## Features

* Chat and coding in one interface, with streamed replies and multiple providers.
* Tools for shell commands, reading, writing and editing files, listing folders, and searching files. Read-only tools run in parallel.
* A `search_chat` tool lets the agent look back through the full chat (including tool output) for something compaction summarized away, and optionally through the other chats of the same folder.
* Attachments from the composer. Images go to the model as image input, and text files are added to the message, or read on demand with `read_attachment` when they are large.
* Approvals let you decide, per tool and per action, what runs without asking.
* Shell sandboxing where the platform supports it, so commands can stay inside the folder you opened. Otherwise commands run directly, behind approvals.
* Folders and sessions. Open a folder and run sessions inside it, grouped by folder. Sessions can also exist without a folder, for general chat.
* Persistent history in plain, human-readable files, with context compaction for long conversations. By default the model writes the summary; Settings > Context can switch to deterministic extraction (no model request), which lists the requests, changed and read files, and commands, and relies on `search_chat` for detail.
* Diffs, syntax highlighting, and find in the chat, with smooth scrolling in very long chats.
* Export and import of sessions.
* MCP servers, added from a standard `mcp.json` and enabled globally or per session.
* Skills, loaded on demand from `SKILL.md` folders in `~/.blazeharness/skills`, `~/.agents/skills`, and a folder's own `.agents/skills`.

## Planned and ideas

Not built yet, and not promised:

* A touch-friendly layout for use from a phone or tablet. The app is localhost-only by design, so that would need to be configurable.
* Localization of the web UI, starting with English.
* An in-app browser that the agent can use to open a running service, read the page, and take a screenshot.
* Web search and web fetch tools, so the agent can look things up and read pages, with the usual approval prompts.
* An "Ask user" tool that lets the agent pause and ask you a question, with free text or choices.
* Attachments as tool input, so a text-only model can pass an uploaded image to a vision tool.
* Subagents that work in their own context and return a summary.
* Plugin support, where the agent can write a plugin for itself through a skill, mode, or tool.
* Look into Blazor AI components, once they are stable on the target .NET version.
* Smaller additions: a command palette, a richer MCP editor with per-tool toggles, dynamic MCP tool loading, a sandbox network allow-list, message search across all folders, a duplicate-provider action, and a per-folder `.mcp.json`.

## Not goals

* Multi-user hosting or scaling.
* Offline or WebAssembly operation.
* A UI component framework or an npm toolchain.
