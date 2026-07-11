namespace Terminal.Core.Pty;

/// <summary>
/// A live pseudo-console (ConPTY) session. It launches a shell process with no window,
/// streams its raw output bytes, and forwards input to it. Decoding those bytes into
/// screen operations is the job of the VT parser (T-003) — this layer stays byte-oriented.
/// </summary>
public interface IPtySession : IDisposable
{
    /// <summary>OS process id of the shell; 0 until <see cref="Start"/> is called.</summary>
    int ProcessId { get; }

    /// <summary>Exit code of the shell process; valid only after <see cref="Exited"/> fires.</summary>
    int ExitCode { get; }

    /// <summary>True once the shell process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Raised (off the UI thread) with a chunk of raw output bytes, not yet decoded.</summary>
    event Action<byte[]>? DataReceived;

    /// <summary>Raised exactly once when the shell process exits, carrying its exit code.</summary>
    event Action<int>? Exited;

    /// <summary>Launches the shell attached to a fresh pseudo console of the given size.</summary>
    void Start(string commandLine, string workingDirectory, short columns, short rows);

    /// <summary>Writes raw bytes to the shell's input asynchronously.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Convenience: writes UTF-8 encoded text to the shell's input.</summary>
    void Write(string text);

    /// <summary>Resizes the pseudo console (in character cells). Safe to call before/after exit.</summary>
    void Resize(short columns, short rows);
}
