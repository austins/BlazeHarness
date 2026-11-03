using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Skills;
using BlazeHarness.Tests.Shared;
using BlazeHarness.Tests.Shared.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Chats;

/// <summary>
/// A chat store, scripted client and working folder in a temporary folder, with a service built on top of them.
/// </summary>
internal sealed class ChatServiceRig : IDisposable
{
    private readonly TempFolder _temp;
    private ChatService? _service;

    public ChatServiceRig(string prefix, string reply)
    {
        _temp = new TempFolder(prefix);
        Paths = new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root);
        Work = _temp.CreateDirectory("work");
        Store = new ChatStore(Paths, NullLoggerFactory.Instance, TimeProvider.System);
        Client = new ScriptedChatClient([ScriptedChatClient.Text(reply)]);
    }

    public AppPaths Paths { get; }

    public string Root => _temp.Root;

    public string Work { get; }

    public ChatStore Store { get; }

    public ScriptedChatClient Client { get; }

    public ChatService Service => _service ?? throw new InvalidOperationException("Call Build first.");

#pragma warning disable IDISP003 // Build refuses a second call, so nothing is overwritten.
    public ChatService Build(SkillCatalog? skills = null, McpManager? mcp = null, PermissionDefaults? defaults = null, Func<bool>? todosEnabled = null)
    {
        _service = _service is null ? new ChatService(Store, (_, _) => Task.FromResult<IChatClient>(Client), NullLoggerFactory.Instance, TimeProvider.System, defaults: defaults, mcp: mcp, skills: skills, todosEnabled: todosEnabled) : throw new InvalidOperationException("Build was already called.");
        return _service;
    }
#pragma warning restore IDISP003

    public void Dispose()
    {
        _service?.Dispose();
        Store.Dispose();
        Client.Dispose();
        _temp.Dispose();
    }
}
