using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MAM.Infrastructure.Processing;

internal static class ProcessingExternalTool
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string file,
        IEnumerable<string> args,
        string toolKind,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Unable to start {toolKind} processing tool.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Required {toolKind} processing tool '{file}' is unavailable.",
                ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may already have exited between the cancellation and kill.
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original cancellation as the authoritative failure.
            }

            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }
}
