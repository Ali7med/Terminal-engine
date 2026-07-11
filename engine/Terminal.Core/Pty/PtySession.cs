using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static Terminal.Core.Pty.NativeMethods;

namespace Terminal.Core.Pty;

/// <summary>
/// Pseudo console (ConPTY) session — the same mechanism modern terminals
/// (Windows Terminal / VS Code) use. Launches a shell with no window, pumps its
/// output over a pipe asynchronously, and forwards input back to it.
/// Ported and reshaped from <c>TerminalLauncher/Terminal/PseudoConsoleSession.cs</c>
/// (async byte-oriented read loop instead of a decoded-string event).
/// </summary>
public sealed class PtySession : IPtySession
{
    private IntPtr _hpc = IntPtr.Zero;
    private IntPtr _attrList = IntPtr.Zero;
    private PROCESS_INFORMATION _pi;
    private SafeFileHandle? _inputWrite;
    private SafeFileHandle? _outputRead;
    private FileStream? _writer;
    private FileStream? _reader;
    private Thread? _readThread;
    private RegisteredWaitHandle? _exitWait;
    private ManualResetEvent? _exitEvent;
    private volatile bool _disposed;
    private int _exitedRaised;

    public int ProcessId { get; private set; }

    public int ExitCode { get; private set; }

    public bool HasExited => Volatile.Read(ref _exitedRaised) == 1;

    public event Action<byte[]>? DataReceived;

    public event Action<int>? Exited;

    public void Start(string commandLine, string workingDirectory, short columns, short rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hpc != IntPtr.Zero)
            throw new InvalidOperationException("Session already started.");
        if (columns < 1) columns = 80;
        if (rows < 1) rows = 25;

        CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0);
        CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0);
        _inputWrite = inputWrite;
        _outputRead = outputRead;

        int hr = CreatePseudoConsole(new COORD { X = columns, Y = rows }, inputRead, outputWrite, 0, out _hpc);
        if (hr != 0)
        {
            inputRead.Dispose();
            outputWrite.Dispose();
            throw new Win32Exception(hr, "CreatePseudoConsole failed — requires Windows 10 1809+.");
        }

        // These two ends are now owned by the pseudo console.
        inputRead.Dispose();
        outputWrite.Dispose();

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        _attrList = Marshal.AllocHGlobal(size);
        si.lpAttributeList = _attrList;
        if (!InitializeProcThreadAttributeList(_attrList, 1, 0, ref size))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!UpdateProcThreadAttribute(_attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hpc, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var cmd = new StringBuilder(string.IsNullOrWhiteSpace(commandLine) ? "cmd.exe" : commandLine);
        string? cwd = Directory.Exists(workingDirectory) ? workingDirectory : null;

        // ONLY EXTENDED_STARTUPINFO_PRESENT here. Do NOT add CREATE_NO_WINDOW / DETACHED_PROCESS:
        // empirically (real console) they yield bytes=0 — the child never attaches to the pseudo
        // console and no output reaches the pipe. Baseline (this flag alone) gives full output.
        if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, cwd, ref si, out _pi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to launch the shell process.");

        ProcessId = _pi.dwProcessId;

        _writer = new FileStream(_inputWrite, FileAccess.Write);
        _reader = new FileStream(_outputRead, FileAccess.Read);

        // A dedicated thread doing blocking reads — not Task/ReadAsync. Anonymous pipes
        // (CreatePipe) are NOT opened for overlapped I/O, so FileStream.ReadAsync over them
        // delivers nothing reliably; a blocking read loop is the pattern Windows Terminal and
        // the reference PseudoConsoleSession use. Closing the pipe/PTY in Dispose unblocks it.
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ConPTY-Read" };
        _readThread.Start();

        // Fire SessionClosed via a thread-pool wait rather than a dedicated blocked thread,
        // so opening/closing many sessions does not accumulate parked threads.
        _exitEvent = new ManualResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle(_pi.hProcess, ownsHandle: false),
        };
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, OnProcessExit, null, Timeout.Infinite, executeOnlyOnce: true);
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        try
        {
            while (!_disposed)
            {
                int read = _reader!.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                DataReceived?.Invoke(chunk);
            }
        }
        catch (IOException)
        {
            // Output pipe closed because the shell exited — expected.
        }
        catch (ObjectDisposedException)
        {
            // Reader disposed during shutdown — expected.
        }
    }

    private void OnProcessExit(object? state, bool timedOut)
    {
        try
        {
            _exitWait?.Unregister(null);
            ExitCode = GetExitCodeProcess(_pi.hProcess, out uint code) ? (int)code : -1;
        }
        catch (Exception)
        {
            ExitCode = -1;
        }

        if (Interlocked.Exchange(ref _exitedRaised, 1) == 0 && !_disposed)
            Exited?.Invoke(ExitCode);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var writer = _writer;
        if (writer is null)
            throw new InvalidOperationException("Session not started.");

        try
        {
            await writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Input pipe closed — the shell has gone away.
        }
        catch (ObjectDisposedException)
        {
            // Writer disposed during shutdown.
        }
    }

    public void Write(string text) =>
        WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask().GetAwaiter().GetResult();

    public void Resize(short columns, short rows)
    {
        if (_hpc != IntPtr.Zero && columns > 0 && rows > 0)
            ResizePseudoConsole(_hpc, new COORD { X = columns, Y = rows });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Closing the pseudo console signals EOF to the shell and unblocks the read loop.
        try { if (_hpc != IntPtr.Zero) ClosePseudoConsole(_hpc); } catch (Exception) { }
        _hpc = IntPtr.Zero;

        try { _writer?.Dispose(); } catch (Exception) { }
        try { _reader?.Dispose(); } catch (Exception) { }

        // Give the blocking read loop a moment to unwind after the pipe/PTY close.
        try { _readThread?.Join(TimeSpan.FromMilliseconds(500)); } catch (Exception) { }

        try { _exitWait?.Unregister(null); } catch (Exception) { }
        try { _exitEvent?.Dispose(); } catch (Exception) { }

        if (_attrList != IntPtr.Zero)
        {
            try { DeleteProcThreadAttributeList(_attrList); } catch (Exception) { }
            Marshal.FreeHGlobal(_attrList);
            _attrList = IntPtr.Zero;
        }

        try
        {
            if (_pi.hProcess != IntPtr.Zero)
            {
                TerminateProcess(_pi.hProcess, 0);
                CloseHandle(_pi.hProcess);
                CloseHandle(_pi.hThread);
                _pi.hProcess = IntPtr.Zero;
                _pi.hThread = IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            // Best-effort teardown.
        }

        GC.SuppressFinalize(this);
    }
}
