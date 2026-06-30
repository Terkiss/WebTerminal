using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace WebPowerShell.Infrastructure.ConPTY;

public class WindowsConPtyProcess : ITerminalProcess
{
    private IntPtr _hPC = IntPtr.Zero;
    private ConPtyNative.PROCESS_INFORMATION _pi;
    private IntPtr _hJob = IntPtr.Zero;
    
    private SafeFileHandle? _hStdInWrite;
    private SafeFileHandle? _hStdOutRead;
    
    private FileStream? _stdinStream;
    private FileStream? _stdoutStream;

    private bool _disposed;

    public bool HasExited
    {
        get
        {
            if (_pi.hProcess == IntPtr.Zero) return true;
            // Native check for process exit could go here, or we can use a wrapper process object
            try {
                using var proc = System.Diagnostics.Process.GetProcessById(_pi.dwProcessId);
                return proc.HasExited;
            } catch { return true; }
        }
    }
    
    public int? ExitCode
    {
        get
        {
            if (_pi.hProcess == IntPtr.Zero) return null;
            try {
                using var proc = System.Diagnostics.Process.GetProcessById(_pi.dwProcessId);
                if (proc.HasExited) return proc.ExitCode;
            } catch { }
            return null;
        }
    }

    public Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken)
    {
        InitializeJobObject();

        var sa = new ConPtyNative.SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<ConPtyNative.SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1,
            lpSecurityDescriptor = IntPtr.Zero
        };

        if (!ConPtyNative.CreatePipe(out IntPtr hStdInRead, out IntPtr hStdInWriteRaw, ref sa, 0))
            throw new Exception("CreatePipe (stdin) failed");

        if (!ConPtyNative.CreatePipe(out IntPtr hStdOutReadRaw, out IntPtr hStdOutWrite, ref sa, 0))
            throw new Exception("CreatePipe (stdout) failed");

        var size = new ConPtyNative.COORD { X = (short)options.Columns, Y = (short)options.Rows };
        int result = ConPtyNative.CreatePseudoConsole(size, hStdInRead, hStdOutWrite, 0, out _hPC);
        if (result != 0)
        {
            throw new Exception($"CreatePseudoConsole failed with HRESULT {result:X}");
        }

        // Close handles we don't need on our side
        ConPtyNative.CloseHandle(hStdInRead);
        ConPtyNative.CloseHandle(hStdOutWrite);

        IntPtr lpSize = IntPtr.Zero;
        ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
        IntPtr attributeList = Marshal.AllocHGlobal(lpSize);

        try
        {
            if (!ConPtyNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref lpSize))
                throw new Exception("InitializeProcThreadAttributeList failed");

            if (!ConPtyNative.UpdateProcThreadAttribute(
                attributeList,
                0,
                (IntPtr)ConPtyNative.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Exception("UpdateProcThreadAttribute failed");
            }

            var siex = new ConPtyNative.STARTUPINFOEX();
            siex.StartupInfo.cb = Marshal.SizeOf<ConPtyNative.STARTUPINFOEX>();
            siex.lpAttributeList = attributeList;

            string cmdLine = string.IsNullOrEmpty(options.Arguments) 
                ? options.Executable 
                : $"{options.Executable} {options.Arguments}";

            bool success = ConPtyNative.CreateProcess(
                null,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                ConPtyNative.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero, // Environment not explicitly handled in this minimal implementation yet
                options.WorkingDirectory,
                ref siex,
                out _pi);

            if (!success)
            {
                throw new Exception($"CreateProcess failed with error code {Marshal.GetLastWin32Error()}");
            }

            // Assign to Job Object so the process tree dies when the job is closed
            if (_hJob != IntPtr.Zero)
            {
                ConPtyNative.AssignProcessToJobObject(_hJob, _pi.hProcess);
            }

            _hStdInWrite = new SafeFileHandle(hStdInWriteRaw, true);
            _hStdOutRead = new SafeFileHandle(hStdOutReadRaw, true);

            _stdinStream = new FileStream(_hStdInWrite, FileAccess.Write, 4096, false);
            _stdoutStream = new FileStream(_hStdOutRead, FileAccess.Read, 4096, false);
        }
        finally
        {
            ConPtyNative.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
        }
        
        return Task.CompletedTask;
    }

    private void InitializeJobObject()
    {
        _hJob = ConPtyNative.CreateJobObject(IntPtr.Zero, null);
        if (_hJob == IntPtr.Zero) return;

        var info = new ConPtyNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = ConPtyNative.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int length = Marshal.SizeOf(typeof(ConPtyNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr ptr = Marshal.AllocHGlobal(length);
        Marshal.StructureToPtr(info, ptr, false);

        if (!ConPtyNative.SetInformationJobObject(_hJob, ConPtyNative.JobObjectExtendedLimitInformation, ptr, (uint)length))
        {
            ConPtyNative.CloseHandle(_hJob);
            _hJob = IntPtr.Zero;
        }
        Marshal.FreeHGlobal(ptr);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    {
        if (_stdinStream != null)
        {
            await _stdinStream.WriteAsync(input, cancellationToken);
            await _stdinStream.FlushAsync(cancellationToken);
        }
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        if (_hPC != IntPtr.Zero)
        {
            ConPtyNative.ResizePseudoConsole(_hPC, new ConPtyNative.COORD { X = (short)columns, Y = (short)rows });
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_stdoutStream == null) yield break;

        var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = 0;
            try
            {
                bytesRead = await _stdoutStream.ReadAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { break; } // Stream closed or process died

            if (bytesRead == 0) break; // EOF
            
            yield return new ReadOnlyMemory<byte>(buffer, 0, bytesRead);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Actually closing handles and Job object will kill it
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_stdinStream != null) await _stdinStream.DisposeAsync();
        if (_stdoutStream != null) await _stdoutStream.DisposeAsync();
        
        _hStdInWrite?.Dispose();
        _hStdOutRead?.Dispose();

        if (_hJob != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(_hJob); // This kills all processes in the job
            _hJob = IntPtr.Zero;
        }

        if (_pi.hProcess != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(_pi.hProcess);
            ConPtyNative.CloseHandle(_pi.hThread);
            _pi.hProcess = IntPtr.Zero;
        }

        if (_hPC != IntPtr.Zero)
        {
            ConPtyNative.ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }
    }
}
