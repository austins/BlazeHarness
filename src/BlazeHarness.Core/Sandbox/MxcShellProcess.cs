using Microsoft.Mxc.Sdk.V1;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// A command running inside an MXC container.
/// </summary>
internal sealed class MxcShellProcess : IShellProcess
{
    private readonly MxcProcess _process;

    internal MxcShellProcess(MxcProcess process)
    {
        _process = process;
    }

    public Stream Output => _process.StandardOutput ?? Stream.Null;

    public Stream Error => _process.StandardError ?? Stream.Null;

    /// <summary>
    /// Gets the id of the process the container was started as, which is outside the sandbox's network.
    /// </summary>
    internal int HostProcessId => (int)_process.Id;

    public async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        var result = await _process.WaitAsync(cancellationToken);
        return result.ExitCode;
    }

    public void Kill()
    {
        try
        {
            _process.Kill();
        }
        catch (MxcException)
        {
            // The container had already ended.
        }
    }

#pragma warning disable IDISP007 // This object owns the process it is given.
    public void Dispose() => _process.Dispose();
#pragma warning restore IDISP007
}
