using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Core.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazeHarness.Core.Tests.Integration.Sessions;

/// <summary>
/// A session store, scripted client and working folder in a temporary folder, with a service built on top of them.
/// </summary>
internal sealed class SessionServiceRig : IDisposable
{
    private readonly TempFolder _temp;
    private SessionService? _service;

    public SessionServiceRig(string prefix, string reply)
    {
        _temp = new TempFolder(prefix);
        Paths = new AppPaths(Path.Combine(_temp.Root, "app"), _temp.Root);
        Work = _temp.CreateDirectory("work");
        Store = new SessionStore(Paths, NullLoggerFactory.Instance);
        Client = new ScriptedChatClient([ScriptedChatClient.Text(reply)]);
    }

    public AppPaths Paths { get; }

    public string Root => _temp.Root;

    public string Work { get; }

    public SessionStore Store { get; }

    public ScriptedChatClient Client { get; }

    public SessionService Service => _service ?? throw new InvalidOperationException("Call Build first.");

#pragma warning disable IDISP003 // Build refuses a second call, so nothing is overwritten.
    public SessionService Build(SkillCatalog? skills = null, McpManager? mcp = null, PermissionDefaults? defaults = null)
    {
        _service = _service is null ? new SessionService(Store, (_, _) => Task.FromResult<IChatClient>(Client), NullLoggerFactory.Instance, skills: skills, defaults: defaults, mcp: mcp) : throw new InvalidOperationException("Build was already called.");
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
