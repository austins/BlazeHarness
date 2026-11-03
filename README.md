# BlazeHarness

A local web app for chatting and coding with any OpenAI-compatible LLM. It runs on Windows, macOS and Linux. The model can read, edit and search files and run shell commands, and you approve what it may do.

⚠️ This app is in early development and not yet suitable for production use.

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* An OpenAI-compatible endpoint and API key. You add providers in the app's settings.

## Running

```bash
dotnet run --project src/BlazeHarness.Web
```

The app listens on `127.0.0.1:5380`, prints its URL, and opens your browser. Options:

| Option | Environment variable | Effect |
|---|---|---|
| `--host ADDRESS` | `BLAZEHARNESS_HOST` | `localhost` or `127.0.0.1` (default) listens on this machine only. `0.0.0.0` listens on all interfaces so other devices can connect by IP address. |
| `--port N` | `BLAZEHARNESS_PORT` | Listen on this port instead of 5380. The app exits with a message if the port is taken. |
| `--no-browser` | `BLAZEHARNESS_NO_BROWSER=1` | Don't open the browser on startup. |

There is no sign-in. With the default, only programs on this machine can reach the app. With `0.0.0.0`, anyone who can reach the port can use it, and it can run commands and edit files, so only do that on a trusted network and limit access with a firewall. The connection is plain HTTP, and the URL must use an IP address, `localhost`, or this machine's host name (also with a `.lan`, `.local`, `.localdomain`, `.home`, `.internal` or `.home.arpa` suffix). Other names are refused to block DNS rebinding.

The app stores everything under `~/.blazeharness` (`%USERPROFILE%\.blazeharness` on Windows). Set `BLAZEHARNESS_HOME` to use another folder, for example a temp folder when experimenting. Providers and models are in `providers.json`, and defaults and permissions are in `config.json`. Each chat is kept in `chats/<id>/`.

API keys are kept in your OS secret store (Windows Credential Manager, macOS Keychain or Linux Secret Service). If none is available, they fall back to an owner-only file. They are never written to `config.json`, `providers.json`, chat files or logs.

## Features

* Chat and coding in one interface, with streamed replies and multiple providers. Settings > General offers full-width chats and text-size controls, saved in the browser.
* Tools for shell commands, reading, writing and editing files, listing folders, and searching files. Read-only tools run in parallel.
* A to-do list the agent keeps up to date with `update_todos` during long tasks. It is shown above the message box (collapsed to "n of m done" and the current step) and survives compaction. It needs no approval, because it only changes what the chat shows.
* An `ask_user` tool for questions the agent can't settle itself: a question with up to 8 options (single or multiple choice) or free text. The run pauses until you answer, choose "You decide" or skip, and the chat shows "Needs you" in the list. Text typed in the message box answers the question. It works at every access level, needs no approval, and is limited to 3 questions per run. A question still open when the app restarts is marked as not answered.
* Messages sent while a reply is running (text only). They wait above the message box as "Queued" and are added as one message after the next tool results, or when the reply would end, so the reply continues with them without changing the start of the request. Stop does not send them; it puts the text back in the message box.
* A `search_chat` tool lets the agent look back through the full chat (including tool output) for something compaction summarized away, and optionally through the other chats of the same folder.
* Attachments from the composer. Images go to the model as image input, and text files are added to the message, or read on demand with `read_attachment` when they are large. An image's path is in the message, and tools and sandboxed commands can read the chat's attachments without asking, so a text-only model can still inspect an image.
* Approvals let you decide, per tool and per action, what runs without asking.
* Shell sandboxing where the platform supports it, so commands can stay inside the folder you opened. Files that a file path rule refuses are also kept out of a sandboxed command's reach: an exact path wherever it is, and a wildcard pattern inside the working folder and approved folders. This is best effort: only files that exist when the command starts, not what git history holds, and on macOS renaming a folder above one may get around it. Where the sandbox can't keep a single file out, those commands don't run. Otherwise commands run directly, behind approvals.
* Folders and chats. Open a folder and run chats inside it, grouped by folder. A chat can also exist without a folder, as a general chat.
* Persistent history in plain, human-readable files, with context compaction for long conversations. It works in steps: first, old large tool results (file reads, command logs) are replaced in the request by a short stub, keeping every call and its result paired, and a marker shows where; only if that is not enough are the older messages summarized. The full output stays in the chat and its log, and `search_chat` still finds it. To-do, skill and question results are never replaced. By default the model writes the summary; Settings > General can switch to deterministic extraction (no model request), which lists the requests, changed and read files, and commands, and relies on `search_chat` for detail.
* Diffs, syntax highlighting, and find in the chat, with smooth scrolling in very long chats.
* Export and import of chats.
* MCP servers, added and edited in a form (secrets go in the OS keychain) or in a standard `mcp.json`, and enabled globally or per chat. A server's tools are found by search when needed (`find_tools`, then `use_tool`) so a pile of servers doesn't fill the context window. Tick "Always load all of its tools" for a small server, or set `"alwaysLoad"` in `mcp.json` to `true` or a list of tool name patterns. List tool name patterns in `"excludeTools"` to keep a server's tools from the model entirely; the tool list in Settings > MCP has a checkbox per tool that edits this list (a tool kept out by a pattern can only be changed in the file). The tool lists are remembered in `cache/mcp-tools.json`, which is safe to delete.
* Skills, loaded on demand from `SKILL.md` folders in `~/.blazeharness/skills`, `~/.agents/skills`, and a folder's own `.agents/skills`.
* Web search and web fetch tools (Settings > Web). Search uses public providers that need no API key (DuckDuckGo, Exa, Parallel, Keenable), either in parallel or one at a time, with retries and a pause after repeated failures. Fetch reads HTML, PDF and text from the public internet only and caches pages in memory per chat. Each use asks first, unless the chat is Unrestricted or set to never ask, and rules can match a host. JavaScript is not run, and system proxy settings are not used.

## Planned and ideas

Not built yet, and not promised:

* An in-app browser that the agent can use to open a running service, read the page, and take a screenshot, so it can check its own UI changes (for example a click-through or a download) instead of only running tests.
* Reading Office documents (docx, xlsx, pptx) in the web fetch tool, which reads HTML, PDF and text.
* A limit on repeated failed attempts, after which the run pauses and uses `ask_user` to ask for help instead of retrying; today a run has no round limit and the user stops it.
* Subagents that work in their own context and return a summary, and can run in parallel (for example independent checks) while the main agent waits. A subagent would run the same loop at the same or a lower access level, and its approval requests would go to the parent chat's approver, never be granted on their own.
* Plugin support, where the agent can write a plugin for itself through a skill, mode, or tool.
* Look into Blazor AI components, once they are stable on the target .NET version.
* Smaller additions: a command palette and a sandbox network allow-list (so commands can only reach hosts you list).

## Not goals

* Multi-user hosting or scaling.
* Offline or WebAssembly operation.
* A UI component framework or an npm toolchain.

## Why not Microsoft Agent Framework?

BlazeHarness builds on `Microsoft.Extensions.AI` (`IChatClient`, `AIFunction`) and keeps its own agent loop rather than using Microsoft Agent Framework (AF).

* The loop, approvals, access levels and chat persistence are the security-critical parts, and they depend on exact behaviour: every tool call gets a result in the saved chat, approvals are checked per call, and the request prefix stays stable and append-only. AF's `ToolApprovalAgent`, context providers and history handling don't guarantee these.
* The pieces we would need from AF are not settled. Its shell tool is preview, its MCP package is alpha, and it has no `AGENTS.md` support.
* An app that runs shell commands and fetches web pages needs a command sandbox, a web client that only reaches public addresses, and Host and Origin checks on its local server. AF offers an optional Docker shell executor and neither of the other two, so we would write most of this ourselves and still take on a large dependency tree.

Worth another look once AF stabilises (for example per-tool approvals, no history loss when a call is terminated, and MCP GA).
