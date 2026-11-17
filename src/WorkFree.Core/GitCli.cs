using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WorkFree.Core;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Thin wrapper around the git executable on PATH.</summary>
public static class GitCli
{
    public static GitResult Run(string? workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workDir is not null)
        {
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(workDir);
        }
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=off");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.longpaths=true");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        try
        {
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return new GitResult(p.ExitCode, stdout, err.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new GitResult(-1, "", "could not run git (is it installed and on PATH?): " + ex.Message);
        }
    }
}
