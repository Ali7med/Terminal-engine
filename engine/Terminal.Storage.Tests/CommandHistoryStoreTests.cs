using System.Linq;
using Terminal.Storage;

namespace Terminal.Storage.Tests;

public class CommandHistoryStoreTests
{
    private static CommandHistoryStore NewStore(TestDatabase t) => new(t.Db);

    [Fact]
    public void Add_then_Recent_returns_the_command()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git status");

        Assert.Equal(new[] { "git status" }, store.Recent());
    }

    [Fact]
    public void Recent_dedups_repeated_commands_and_orders_newest_first()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("ls");
        store.Add("git status");
        store.Add("ls"); // repeated — should collapse and float to the top as newest

        Assert.Equal(new[] { "ls", "git status" }, store.Recent());
    }

    [Fact]
    public void Add_ignores_null_and_whitespace_commands()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add(null!);
        store.Add("");
        store.Add("   ");
        store.Add("\t\n");

        Assert.Empty(store.Recent());
    }

    [Fact]
    public void Search_matches_substring_case_insensitively()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("Git Status");
        store.Add("git commit");

        var results = store.Search("git");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Command == "Git Status");
        Assert.Contains(results, r => r.Command == "git commit");
    }

    [Fact]
    public void Search_excludes_non_matching_commands()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git status");
        store.Add("npm install");

        var results = store.Search("npm");

        Assert.Single(results);
        Assert.Equal("npm install", results[0].Command);
    }

    [Fact]
    public void Search_orders_newest_first_and_round_trips_metadata()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git log", shell: "cmd", cwd: @"C:\a");
        store.Add("git diff", shell: "pwsh", cwd: @"C:\b");

        var results = store.Search("git");

        Assert.Equal(new[] { "git diff", "git log" }, results.Select(r => r.Command).ToArray());
        Assert.Equal("pwsh", results[0].Shell);
        Assert.Equal(@"C:\b", results[0].WorkingDirectory);
    }

    [Fact]
    public void Search_treats_wildcards_as_literals()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("echo 100%");
        store.Add("echo done");

        var percent = store.Search("100%");
        Assert.Single(percent);
        Assert.Equal("echo 100%", percent[0].Command);

        // A bare "%" must not match everything.
        var underscore = store.Search("a_b");
        Assert.Empty(underscore);
    }

    [Fact]
    public void Clear_empties_the_history()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git status");
        store.Add("ls");

        store.Clear();

        Assert.Empty(store.Recent());
        Assert.Empty(store.Search("git"));
    }

    [Fact]
    public void Empty_store_returns_empty_lists()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        Assert.Empty(store.Recent());
        Assert.Empty(store.Search("anything"));
    }

    [Fact]
    public void Recent_respects_the_limit()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("one");
        store.Add("two");
        store.Add("three");

        var recent = store.Recent(limit: 2);

        Assert.Equal(new[] { "three", "two" }, recent);
    }

    [Fact]
    public void Search_respects_the_limit()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("cmd one");
        store.Add("cmd two");
        store.Add("cmd three");

        var results = store.Search("cmd", limit: 1);

        Assert.Single(results);
        Assert.Equal("cmd three", results[0].Command);
    }

    [Fact]
    public void Suggest_returns_most_recent_command_with_prefix()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git status");
        store.Add("git commit -m x");
        store.Add("git checkout main"); // newest git* command

        Assert.Equal("git checkout main", store.Suggest("git c"));
    }

    [Fact]
    public void Suggest_prefers_newest_matching_distinct_command()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git push");
        store.Add("git pull");
        store.Add("git push"); // repeated — floats back to newest

        Assert.Equal("git push", store.Suggest("git p"));
    }

    [Fact]
    public void Suggest_excludes_exact_equal_command()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("ls"); // exactly equal to the prefix — not a completion

        Assert.Null(store.Suggest("ls"));
    }

    [Fact]
    public void Suggest_returns_null_for_null_or_whitespace_prefix()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git status");

        Assert.Null(store.Suggest(null!));
        Assert.Null(store.Suggest(""));
        Assert.Null(store.Suggest("   "));
    }

    [Fact]
    public void Suggest_returns_null_when_no_command_matches()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("git status");

        Assert.Null(store.Suggest("npm "));
    }

    [Fact]
    public void Suggest_is_case_sensitive_ordinal()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("Git status");

        // Lowercase prefix must NOT match a capitalized command.
        Assert.Null(store.Suggest("git"));
        Assert.Equal("Git status", store.Suggest("Git"));
    }

    [Fact]
    public void Suggest_treats_wildcards_in_prefix_as_literals()
    {
        using var t = new TestDatabase();
        var store = NewStore(t);

        store.Add("echo 100% done");
        store.Add("echo other");

        // "%" must be literal, not a wildcard matching everything.
        Assert.Equal("echo 100% done", store.Suggest("echo 100%"));
        Assert.Null(store.Suggest("echo 1_0")); // "_" literal — no such command
    }
}
