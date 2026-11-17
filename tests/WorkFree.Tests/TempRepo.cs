using WorkFree.Core;

namespace WorkFree.Tests;

/// <summary>A throwaway directory holding a real git repository with one commit.</summary>
public sealed class TempRepo : IDisposable
{
    public string Root { get; }
    public string Main { get; }

    public TempRepo()
    {
        Root = Path.Combine(Path.GetTempPath(), "workfree-tests", Guid.NewGuid().ToString("N"));
        Main = Path.Combine(Root, "main");
        Directory.CreateDirectory(Main);
        Git(Main, "init", "-b", "main");
        File.WriteAllText(Path.Combine(Main, "readme.txt"), "hello");
        Git(Main, "add", ".");
        Git(Main, "-c", "user.name=test", "-c", "user.email=test@example.com", "commit", "-m", "init");
    }

    public string AddWorktree(string name)
    {
        var path = Path.Combine(Root, name);
        Git(Main, "worktree", "add", "-b", name, path);
        return path;
    }

    public static void Git(string dir, params string[] args)
    {
        var result = GitCli.Run(dir, args);
        if (!result.Ok) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {result.StdErr}");
    }

    public void Dispose()
    {
        try { PathUtil.DeleteDirectory(Root); }
        catch (IOException) { }
    }
}
