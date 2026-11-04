using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// Kills an unconfined command and all its children when its owning job is closed.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    internal WindowsProcessJob()
    {
        _handle = CreateJob(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var size = IntPtr.Size == 8 ? 144 : 112;
        var limits = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, limits, size);
            Marshal.WriteInt32(limits, 16, 0x2000); // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
            if (!SetLimits(_handle, 9, limits, (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(limits);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    internal void Assign(Process process)
    {
        if (!AssignProcess(_handle, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    internal void Kill() => _ = Terminate(_handle, 1);

#pragma warning disable SYSLIB1054 // These small Windows bindings avoid enabling unsafe code for the project.
    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateJob(IntPtr security, string? name);

    [DllImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLimits(SafeFileHandle job, int informationClass, IntPtr information, uint length);

    [DllImport("kernel32.dll", EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcess(SafeFileHandle job, SafeProcessHandle process);

    [DllImport("kernel32.dll", EntryPoint = "TerminateJobObject", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Terminate(SafeFileHandle job, uint exitCode);
#pragma warning restore SYSLIB1054
}
