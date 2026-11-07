# <img src="src/BlazeHarness.Web/wwwroot/favicon.svg" alt="" width="32" height="32"> Blaze Harness

A local web app for chatting and coding with any OpenAI-compatible LLM. It runs on Windows, macOS and Linux. The model can read, edit and search files and run shell commands, and you approve what it may do.

> [!WARNING]
> This app is in early development and not yet suitable for production use.

## Requirements

* An OpenAI-compatible endpoint and API key. You add providers in the app's settings.
* Optional, on Linux:
  * `bubblewrap` (`bwrap`), for the command sandbox. Without it, commands run directly, behind approvals. The sandbox also needs unprivileged user namespaces, which some distributions restrict.
  * `socat` and `nsenter` (util-linux), so servers that sandboxed commands start can be reached (see [Background commands](#background-commands)).

## Installing

Download the file for your system from the [latest release](https://github.com/austins/BlazeHarness/releases/latest). Each one includes the .NET runtime.

* On Windows (x64), extract the `.zip` and run `BlazeHarness.Web.exe`. The app isn't signed, so SmartScreen may stop the first run: choose More info, then Run anyway.
* On macOS (Apple silicon), open the `.dmg` and drag Blaze Harness to Applications. The app isn't notarized, so macOS blocks the first launch: open System Settings > Privacy & Security and choose Open Anyway.
* On Linux (x64), make the `.AppImage` executable (`chmod +x`) and run it. It needs FUSE and libicu, which most desktop distributions include.

The app runs in a terminal window that shows its address. Press Ctrl+C or close the window to stop it. Log entries go to a file, not the terminal (see [Data and secrets](#data-and-secrets)).

To check that a download was built from this repository, run `gh attestation verify <file> --repo austins/BlazeHarness`.

## Running

To run from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then:

```bash
dotnet run --project src/BlazeHarness.Web
```

The app listens on `127.0.0.1:5380`, prints its URL, and opens your browser.

| Option | Environment variable | Effect |
|---|---|---|
| `--host ADDRESS` | `BLAZEHARNESS_HOST` | `localhost` or `127.0.0.1` (default) listens on this machine only. `0.0.0.0` listens on all interfaces so other devices can connect by IP address. |
| `--port N` | `BLAZEHARNESS_PORT` | Listen on this port instead of 5380. The app exits with a message if the port is taken. |
| `--no-browser` | `BLAZEHARNESS_NO_BROWSER=1` | Don't open the browser on startup. |

### Access

There is no sign-in. With the default, only programs on this machine can reach the app. With `0.0.0.0`, anyone who can reach the port can use it, and it can run commands and edit files, so only do that on a trusted network and limit access with a firewall.

The connection is plain HTTP. By default the URL must use `localhost` or `127.0.0.1`. With `0.0.0.0`, any IP address and this machine's host name are also accepted (the host name may carry a `.lan`, `.local`, `.localdomain`, `.home`, `.internal` or `.home.arpa` suffix). Other names are refused to block DNS rebinding.

### Data and secrets

The app stores everything under `~/.blazeharness` (`%USERPROFILE%\.blazeharness` on Windows). Set `BLAZEHARNESS_HOME` to use another folder, for example a temp folder when experimenting.

| Path | Contents |
|---|---|
| `providers.json` | Providers and models |
| `config.json` | Settings and permissions |
| `instructions.md` | Your custom instructions |
| `mcp.json` | MCP servers |
| `folders.json` | Working folders you registered |
| `skills/` | Your own skills |
| `chats/<id>/` | One folder per chat |
| `logs/` | One log file per day, capped at 5 MB; files older than 7 days are deleted. Holds warnings and errors, and the app's own information messages. Exception messages are left out because they can echo prompts or replies |

API keys are kept in your OS secret store (Windows Credential Manager, macOS Keychain or Linux Secret Service). If none is available, they fall back to an owner-only file. They are never written to `config.json`, `providers.json`, chat files or logs.

## Features

### Chat and tools

* Chat and coding in one interface, with streamed replies and multiple providers. Settings > General offers full-width chats and text-size controls, saved in the browser. Message timestamps use the selected display language and the browser's local time zone.
* Tools for shell commands, reading, writing and editing files, listing folders, and searching files. Read-only tools run in parallel.
  * File reads stop after the requested page and one lookahead line. Continuation results include the next offset; the total line count is included only when the read reaches the end.
* Folders and chats. Open a folder and run chats inside it, grouped by folder. A chat can also exist without a folder, as a general chat.
* Diffs, syntax highlighting, and find in the chat, with smooth scrolling in very long chats.
* Plain-CSS motion for new messages, opening panels, tool results and hover or press feedback. It is off when your system asks for reduced motion.

### Approvals and sandboxing

* Approvals let you decide, per tool and per action, what runs without asking.
  * Permissions and resolved file paths are checked again after approval. If they change while a question is open, the call is refused and must be requested again.
  * Risky commands (recursive deletes, disk tools, `sudo`, commands that print secrets) need approval every time below the Unrestricted access level. With Never ask they are refused.
  * Never ask never pauses a run: anything that would still ask (a risky command, an ask rule, some `send_to_chat` targets) is refused with a reason the agent reads, and `ask_user` questions aren't asked. For autonomous work, use Write in chat folder with Never ask, so commands stay in the sandbox.
  * In a Read only chat, commands whose only risk is changing files run without asking, because the sandbox makes them fail.
* Shell sandboxing where the platform supports it, so commands can stay inside the folder you opened. Otherwise commands run directly, behind approvals.
  * Files that a file path rule refuses are also kept out of a sandboxed command's reach: an exact path wherever it is, and a wildcard pattern inside the working folder and approved folders.
  * This is best effort. It covers only files that exist when the command starts, not what git history holds, and on macOS renaming a folder above one may get around it.
  * Where the sandbox can't keep a single file, or a protected folder inside the working folder, out of reach, those commands don't run. On Windows this is currently always the case, so a command whose folder holds a refused file or a protected folder needs the Unrestricted access level.
* Git status and diffs disable repository-configured clean and process filters, so inspecting changes does not run those helpers.
* An optional network host filter for sandboxed commands (Settings > Permissions > Network, off by default).
  * When it is on, a sandboxed command's HTTP and HTTPS traffic goes through a local proxy and everything else is blocked.
  * The first time a command reaches a host you have no rule for, the chat asks: allow once, for this chat, or always (a Network host rule). Deny rules win, allow rules skip the question, and with Never ask an unlisted host is refused.
  * Hosts that resolve to private or loopback addresses are always refused. On Linux, servers the sandbox runs on its isolated localhost still work. macOS shares the host network, so the filter also blocks connections to servers started by the sandbox.
  * It does not apply at the Unrestricted access level, where commands run outside the sandbox.
  * Tested on Linux only. On Windows, sandboxed commands don't run while it is on.

### Long-running work

#### Background commands

The `shell` tool's `background` option keeps a command running after the call returns, for servers and watchers. Processes started with `&` or `nohup` are stopped when their owning command ends.

Without the sandbox, leftover background processes are stopped when the command ends, so build and compiler servers start fresh each time. On POSIX, automatic exit cleanup does not cover separate process groups or sessions, such as `timeout` or `setsid`.

* Each background command gets an id such as `bg1` and a time limit (`timeout_seconds`, default 1 hour, at most 8 hours). At most 8 run at once, and they stop when the chat closes.
* The `shell_jobs` tool lists them, reads their output (optionally waiting for them to end) and stops them. Running ones are listed above the message box with a Stop button for you.
* A sandboxed command has its own network. On Linux with `socat` and `nsenter` installed, the ports a sandboxed background command listens on are forwarded at the same number to localhost and into the chat's other sandboxed commands, so the browser tools and later commands reach its server (`shell_jobs` lists them).
* Elsewhere, a server it starts can only be reached from inside that command. For more, set the chat's access level to Unrestricted.

#### Keeping the agent on track

* A to-do list the agent keeps up to date with `update_todos` during long tasks. It is shown above the message box (collapsed to "n of m done" and the current step) and survives compaction. If the list still has open items after 10 tool calls without an update, the agent is reminded to update it. It needs no approval, because it only changes what the chat shows.
* An `ask_user` tool for questions the agent can't settle itself: a question with up to 8 options (single or multiple choice) or free text.
  * The run pauses until you answer, choose "You decide" or skip, and the chat shows "Needs you" in the list. Text typed in the message box answers the question.
  * It works at every access level, needs no approval, and is limited to 3 questions per run. With Never ask the question isn't asked, and the agent is told to decide itself and list open questions in its reply. A question still open when the app restarts is marked as not answered.
* A limit on repeated failures: after 5 rounds in a row in which every tool call failed, the agent is told to stop retrying and to use `ask_user` (or reply) to say what went wrong. A message you send during the run replaces that note. A run has no other round limit; you can stop it.
* Messages sent while a reply is running (text only). They wait above the message box as "Queued", where an X removes one. They are added as one message after the next tool results, or when the reply would end, so the reply continues with them without changing the start of the request. Stop does not send them; it puts the text back in the message box.

### Context and history

* Persistent history in plain, human-readable files, with export and import of chats.
* Context compaction for long conversations, in steps:
  1. Old large tool results (file reads, command logs) are replaced in the request by a short stub. Every call and its result stay paired, and a marker shows where.
  2. Only if that is not enough, the older messages are summarized.

  The full output stays in the chat and its log, and `search_chat` still finds it. To-do, skill and question results are never replaced. By default the model writes the summary. Automatic chat titles and model-written summaries use the chat's selected model. Settings > General can switch to deterministic extraction (no model request), which lists the requests, changed and read files, and commands, and relies on `search_chat` for detail.
* A `search_chat` tool lets the agent look back through the full chat (including tool output) for something compaction summarized away, and optionally through the other chats of the same folder.
* A `send_to_chat` tool lets the agent pass context to another chat and start a turn there, for example from a finished story to one that depends on it. To point it at a chat, right-click the chat, choose "Copy chat reference" and paste the `chat:<id>` into your message. The agent can also name a chat by its exact title, or start a new one. The receiving chat runs with its own permissions, shows which chat sent the message, and doesn't reply back. If it is busy, the message waits until its reply ends. A send to a chat in another folder, or from a Read only chat, asks you every time; a chat set to Never ask refuses it. A message that has passed through 3 chats without you is refused.
* Attachments from the composer. Images go to the model as image input, and text files are added to the message, or read on demand with `read_attachment` when they are large. An image's path is in the message, and tools and sandboxed commands can read the chat's attachments without asking, so a text-only model can still inspect an image.

### Extensions

* MCP servers, added and edited in a form (secrets go in the OS keychain) or in a standard `mcp.json`, and enabled globally or per chat.
  * A server's tools are found by search when needed (`find_tools`, then `use_tool`) so a pile of servers doesn't fill the context window. Tick "Always load all of its tools" for a small server, or set `"alwaysLoad"` in `mcp.json` to `true` or a list of tool name patterns.
  * List tool name patterns in `"excludeTools"` to keep a server's tools from the model entirely. The tool list in Settings > MCP has a checkbox per tool that edits this list (a tool kept out by a pattern can only be changed in the file).
  * The tool lists are remembered in `cache/mcp-tools.json`, which is safe to delete.
* Skills, loaded on demand from `SKILL.md` folders in `~/.blazeharness/skills`, `~/.agents/skills`, and a folder's own `.agents/skills`.
* Custom instructions (Settings > Instructions): your own text, stored in `instructions.md` and added to every chat's system prompt after the built-in prompt. It does not replace the built-in prompt. A folder's `AGENTS.md` comes after it and takes priority if the two conflict. There is no length limit, but the text is sent with every message, so keep it short. Edits apply from the next message in each chat.

### Web access

#### Search and fetch

Web search and web fetch tools (Settings > Web).

* Search uses public providers that need no API key (DuckDuckGo, Exa, Parallel, Keenable), either in parallel or one at a time, with retries and a pause after repeated failures.
* Fetch reads HTML, PDF, Word, Excel and PowerPoint (docx, xlsx, pptx) and text from the public internet only, and caches pages in memory per chat.
* Each use asks first, unless the chat is Unrestricted or set to never ask, and rules can match a host.
* JavaScript is not run, and system proxy settings are not used.

#### In-app browser

A browser the agent can use without any extra MCP server (Settings > Web, on by default). It is a remote-controlled test browser, not a replacement for your own: it has no tabs, extensions or developer tools, and the agent cannot drag or upload files.

* Tools:
  * `browser_navigate` opens a page of a service running on this machine (loopback addresses only, not this app).
  * `browser_snapshot` reads the page back as an accessibility outline (controls carry a `ref`), with console and network errors.
  * `browser_click` and `browser_type` act on those refs (a select takes an option's text).
  * `browser_scroll` pages through long or lazy-loading pages, and `browser_wait` gives a slow page time (optionally until some text shows up).
  * `browser_screenshot` saves a PNG into the folder and shows it in the chat, and `browser_download` saves a file a link downloads (up to 50 MB). These two need write access, like other file tools.
* Models that accept images also see the screenshot (up to 5 MB). Others read pages only through the outline. Pages in iframes are not reachable.
* It drives headless Chrome (the Chrome for Testing headless shell), which is downloaded once (about 125 MB) when you choose Install browser, so the app itself stays small.
* Each chat has its own cookies, separate from your own browser, and each `host:port` needs approval.
* Where sandboxed commands' ports can't be forwarded (see [Background commands](#background-commands)), the browser tools are offered only in chats with the Unrestricted access level.

#### Side panel

* A live view of the page next to the chat. Show or hide it with the panel button at the right end of the top bar. It also opens by itself when the agent opens another address.
  * You can click, hover, type and scroll in it, or save a full-page screenshot.
  * It is a stream of screenshots (up to about 30 frames a second as high-quality JPEG over its own WebSocket, so animations stay smooth; only the visible viewport, and nothing is sent while the page is still). The page is not embedded, so you see the viewport as the agent's Chrome renders it, and native controls such as file pickers or right-click menus don't appear.
* A read-only Changes tab for folder chats in a git repository (git must be installed).
  * It lists the files changed since the last commit, with a scope menu (all, staged or unstaged) and a scrollable, syntax-highlighted diff for the selected file.
  * Nothing is cached: it checks again when you open the chat, when a reply finishes, when the panel opens and when you press Refresh, so a repository created mid-chat shows up. The tab is hidden when the folder is not in a repository or has no changes.
* Both sidebars are resizable by dragging the divider or with the arrow keys (double-click resets), and the width is remembered.

## Planned and ideas

Not built yet, and not promised:

* Subagents that work in their own context and return a summary, and can run in parallel (for example independent checks) while the main agent waits. A subagent would run the same loop at the same or a lower access level, and its approval requests would go to the parent chat's approver, never be granted on their own. A subagent starts without the parent's history, so it would need the to-do list passed in or a `get_todos` tool; a chat today needs neither, because the list is in its own history and in compaction summaries.
* Plugin support, where the agent can write a plugin for itself through a skill, mode, or tool.
* Look into Blazor AI components, once they are stable on the target .NET version.

## Not goals

* Multi-user hosting or scaling.
* Offline or WebAssembly operation.
* A UI component framework or an npm toolchain.

## Why not Microsoft Agent Framework?

Blaze Harness builds on `Microsoft.Extensions.AI` (`IChatClient`, `AIFunction`) and keeps its own agent loop rather than using Microsoft Agent Framework (AF).

* The loop, approvals, access levels and chat persistence are the security-critical parts, and they depend on exact behavior: every tool call gets a result in the saved chat, approvals are checked per call, and the request prefix stays stable and append-only. AF's `ToolApprovalAgent`, context providers and history handling don't guarantee these.
* The pieces we would need from AF are not settled. Its shell tool is preview, its MCP package is alpha, and it has no `AGENTS.md` support.
* An app that runs shell commands and fetches web pages needs a command sandbox, a web client that only reaches public addresses, and Host and Origin checks on its local server. AF offers an optional Docker shell executor and neither of the other two, so we would write most of this ourselves and still take on a large dependency tree.

Worth another look once AF stabilizes (for example per-tool approvals, no history loss when a call is terminated, and MCP GA).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
