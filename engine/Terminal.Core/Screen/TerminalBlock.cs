namespace Terminal.Core.Screen;

/// <summary>
/// State of a command block (Warp-style grouping of one command with its output).
/// A block with no exit code (heuristic, or still running) defaults to Success on close.
/// </summary>
public enum BlockState
{
    /// <summary>The command is still running (not yet closed by OSC 133;D).</summary>
    Running,

    /// <summary>Finished successfully (exit code 0, or heuristic with no code).</summary>
    Success,

    /// <summary>Finished with a non-zero exit code.</summary>
    Failed,
}

/// <summary>
/// A command block tracked live while feeding output. Its range is expressed in absolute
/// line numbers (the same scale as <see cref="ScreenSnapshot.BaseLine"/>), so the renderer
/// can locate it across scrollback. Mutable and engine-internal; the render thread sees the
/// immutable <see cref="BlockSnapshot"/> instead.
/// </summary>
internal sealed class Block
{
    /// <summary>Absolute line of the first line of the block (the prompt/command line).</summary>
    public long StartLine;

    /// <summary>Absolute line of the first output line.</summary>
    public long OutputStartLine;

    /// <summary>Absolute line just past the block (exclusive); <see cref="long.MaxValue"/> while open.</summary>
    public long EndLine = long.MaxValue;

    /// <summary>The command text, when known (heuristic path).</summary>
    public string CommandText = "";

    /// <summary>Exit code when reported via OSC 133;D;code.</summary>
    public int? ExitCode;

    /// <summary>Running / Success / Failed.</summary>
    public BlockState State = BlockState.Running;
}

/// <summary>Immutable view of a <see cref="Block"/> for the render thread.</summary>
public sealed class BlockSnapshot
{
    public long StartLine { get; }
    public long OutputStartLine { get; }
    public long EndLine { get; }
    public string CommandText { get; }
    public int? ExitCode { get; }
    public BlockState State { get; }

    internal BlockSnapshot(Block b)
    {
        StartLine = b.StartLine;
        OutputStartLine = b.OutputStartLine;
        EndLine = b.EndLine;
        CommandText = b.CommandText;
        ExitCode = b.ExitCode;
        State = b.State;
    }
}
