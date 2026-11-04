using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace BlazeHarness.Core.Sandbox;

/// <summary>
/// Owns an unconfined shell's process group, including children left behind when the shell exits.
/// </summary>
internal sealed class PosixShellProcess : IShellProcess
{
    private readonly int _pid;
    private readonly Lock _gate = new();
    private readonly Task<int> _exit;
    private bool _exited;

    private PosixShellProcess(int pid, AnonymousPipeServerStream output, AnonymousPipeServerStream error)
    {
        _pid = pid;
        Output = output;
        Error = error;
        _exit = Task.Factory.StartNew(WaitForExit, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <inheritdoc />
    public Stream Output { get; }

    /// <inheritdoc />
    public Stream Error { get; }

    /// <inheritdoc />
    public Task<int> WaitAsync(CancellationToken cancellationToken) => _exit.WaitAsync(cancellationToken);

    /// <inheritdoc />
    public void Kill()
    {
        lock (_gate)
        {
            if (!_exited)
            {
                try
                {
                    using var leader = Process.GetProcessById(_pid);
                    leader.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or AggregateException)
                {
                    // The group cleanup also covers children orphaned by an exited shell.
                }
                finally
                {
                    _ = Signal(-_pid, 9);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Kill();
        Output.Dispose();
        Error.Dispose();
    }

    internal static PosixShellProcess Start(ShellLaunch launch)
    {
        var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        var actions = Marshal.AllocHGlobal(256);
        // Linux's opaque spawn attributes occupy 336 bytes; macOS stores a pointer here.
        var attributes = Marshal.AllocHGlobal(512);
        var actionsReady = false;
        var attributesReady = false;
        List<IntPtr> strings = [];
        try
        {
            Check(ActionsInit(actions));
            actionsReady = true;
            Check(AttributesInit(attributes));
            attributesReady = true;
            Check(SetFlags(attributes, 2)); // POSIX_SPAWN_SETPGROUP creates the group before any command can run.
            Check(SetGroup(attributes, 0));
            Check(ChangeDirectory(actions, Utf8(launch.WorkingDirectory, strings)));
            Check(Open(actions, 0, Utf8("/dev/null", strings), 0, 0));
            Check(Duplicate(actions, output.ClientSafePipeHandle, 1));
            Check(Duplicate(actions, error.ClientSafePipeHandle, 2));
            var arguments = Pointers([launch.Shell.Executable, .. launch.Shell.ArgumentsFor(launch.Command)], strings);
            var environment = Pointers(
                Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Select(entry => $"{entry.Key}={entry.Value}"),
                strings);
            Check(Spawn(out var pid, Utf8(launch.Shell.Executable, strings), actions, attributes, arguments, environment));
            output.DisposeLocalCopyOfClientHandle();
            error.DisposeLocalCopyOfClientHandle();
            return new PosixShellProcess(pid, output, error);
        }
        catch (Exception ex)
        {
            output.Dispose();
            error.Dispose();
            if (ex is Win32Exception or EntryPointNotFoundException)
            {
                throw new IOException($"Could not start {launch.Shell.Name}: {ex.Message}", ex);
            }

            throw;
        }
        finally
        {
            if (actionsReady)
            {
                _ = ActionsDestroy(actions);
            }

            if (attributesReady)
            {
                _ = AttributesDestroy(attributes);
            }

            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
            foreach (var pointer in strings)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    private int WaitForExit()
    {
        var info = Marshal.AllocHGlobal(256);
        try
        {
            // WNOWAIT pins the leader's pid until its group has been killed, so it cannot be reused by an unrelated process.
            while (WaitId(1, (uint)_pid, info, 4 | (OperatingSystem.IsMacOS() ? 0x20 : 0x01000000)) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                // ECHILD means another reaper consumed the exit status.
                if (error == 10)
                {
                    lock (_gate)
                    {
                        _exited = true;
                    }

                    return -1;
                }

                if (error != 4)
                {
                    throw new IOException("Could not wait for the shell process.", new Win32Exception(error));
                }
            }

            lock (_gate)
            {
                _ = Signal(-_pid, 9);
                int status;
                while (WaitPid(_pid, out status, 0) < 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 10)
                    {
                        _exited = true;
                        return -1;
                    }

                    if (error != 4)
                    {
                        throw new IOException("Could not reap the shell process.", new Win32Exception(error));
                    }
                }

                _exited = true;
                return (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(info);
        }
    }

    private static IntPtr Pointers(IEnumerable<string> values, List<IntPtr> owned)
    {
        var pointers = values.Select(value => Utf8(value, owned)).Append(IntPtr.Zero).ToArray();
        var array = Marshal.AllocCoTaskMem(pointers.Length * IntPtr.Size);
        owned.Add(array);
        Marshal.Copy(pointers, 0, array, pointers.Length);
        return array;
    }

    private static IntPtr Utf8(string value, List<IntPtr> owned)
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(value);
        owned.Add(pointer);
        return pointer;
    }

    private static void Check(int error)
    {
        if (error != 0)
        {
            throw new Win32Exception(error);
        }
    }

#pragma warning disable SYSLIB1054 // These small POSIX bindings avoid enabling unsafe code for the project.
    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int ActionsInit(IntPtr actions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int ActionsDestroy(IntPtr actions);

    [DllImport("libc", EntryPoint = "posix_spawnattr_init")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int AttributesInit(IntPtr attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int AttributesDestroy(IntPtr attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SetFlags(IntPtr attributes, short flags);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setpgroup")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int SetGroup(IntPtr attributes, int group);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int ChangeDirectory(IntPtr actions, IntPtr path);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Open(IntPtr actions, int fd, IntPtr path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Duplicate(IntPtr actions, SafePipeHandle fd, int target);

    [DllImport("libc", EntryPoint = "posix_spawn")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Spawn(out int pid, IntPtr path, IntPtr actions, IntPtr attributes, IntPtr arguments, IntPtr environment);

    [DllImport("libc", EntryPoint = "waitid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int WaitId(int type, uint pid, IntPtr info, int options);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int WaitPid(int pid, out int status, int options);

    [DllImport("libc", EntryPoint = "kill")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Signal(int pid, int signal);
#pragma warning restore SYSLIB1054
}
