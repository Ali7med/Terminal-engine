using Terminal.Core.Screen;

namespace Terminal.Core.Tests.Screen;

public class CommandBlockTests
{
    // OSC 133 shell-integration marker: ESC ] 133 ; <body> BEL
    private static string Osc133(string body) => $"]133;{body}";

    [Fact]
    public void No_blocks_by_default()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString("hello world\r\n");
        Assert.Empty(sb.Snapshot().Blocks);
    }

    [Fact]
    public void Osc133_full_cycle_creates_one_successful_block()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc133("A") + "$ ls\r\n" + Osc133("C") + "file1\r\n" + Osc133("D;0"));

        var blocks = sb.Snapshot().Blocks;
        Assert.Single(blocks);
        Assert.Equal(0, blocks[0].ExitCode);
        Assert.Equal(BlockState.Success, blocks[0].State);
    }

    [Fact]
    public void Osc133_nonzero_exit_marks_failed()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc133("A") + "$ bad\r\n" + Osc133("C") + "boom\r\n" + Osc133("D;1"));

        var blocks = sb.Snapshot().Blocks;
        Assert.Single(blocks);
        Assert.Equal(1, blocks[0].ExitCode);
        Assert.Equal(BlockState.Failed, blocks[0].State);
    }

    [Fact]
    public void New_prompt_closes_previous_open_block()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc133("A") + "$ a\r\n");   // first prompt, left open
        sb.FeedString(Osc133("A") + "$ b\r\n");   // second prompt closes the first

        var blocks = sb.Snapshot().Blocks;
        Assert.Equal(2, blocks.Count);
        Assert.NotEqual(long.MaxValue, blocks[0].EndLine);   // first now closed
    }

    [Fact]
    public void Heuristic_command_opens_block_carrying_command_text()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.BeginHeuristicCommand("ls -la");
        sb.FeedString("total 0\r\n");

        var blocks = sb.Snapshot().Blocks;
        Assert.Single(blocks);
        Assert.Equal("ls -la", blocks[0].CommandText);
        Assert.Equal(BlockState.Running, blocks[0].State);
    }

    [Fact]
    public void Heuristic_is_ignored_while_osc133_block_is_open()
    {
        var sb = new ScreenBuffer(80, 24);
        sb.FeedString(Osc133("A"));        // OSC 133 block open
        sb.BeginHeuristicCommand("ignored");

        Assert.Single(sb.Snapshot().Blocks);
    }
}
