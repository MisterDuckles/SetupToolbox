using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace WingetAppDeployer_WinUI.Services;

// Wraps schtasks.exe to create, delete, query, and run the auto-update task.
// Uses a WinUI-specific task name so it can coexist with the WPF app's task.
public sealed class TaskSchedulerService
{
    private const string TaskName = "WingetAppDeployer_WinUI_AutoUpdate";
    private const int ErrorCancelled = 1223;

    private static string LogPath => Path.Combine(Path.GetTempPath(), "WingetAppDeployer_schtasks.log");

    public async Task<CreateTaskOutcome> CreateUpdateTaskAsync(UpdateScheduleType scheduleType, string? customTime = null)
    {
        // We willen de exe-pad van DEZE app gebruiken voor de task action. Bij
        // `dotnet run` wijst Environment.ProcessPath naar de WinUI .exe (apphost),
        // bij self-contained publish ook. Als ProcessPath leeg is hebben we niks
        // om te schedulen.
        var exePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(exePath))
            return new CreateTaskOutcome(CreateTaskResult.Failed, "ProcessPath onbekend — kon de huidige exe niet bepalen.");

        var trigger = scheduleType switch
        {
            UpdateScheduleType.Daily => $"/sc daily /st {customTime ?? "09:00"}",
            UpdateScheduleType.Weekly => $"/sc weekly /d MON /st {customTime ?? "09:00"}",
            UpdateScheduleType.OnStartup => "/sc onlogon",
            _ => "/sc daily /st 09:00"
        };

        // Wrap in cmd.exe zodat we stdout+stderr kunnen redirecten naar een
        // tmp-logfile — dat kan niet wanneer UseShellExecute=true (vereist voor
        // Verb=runas → UAC). Format: cmd /c "schtasks ... > log 2>&1". De inner
        // quoting van /tr (escaped exe-pad) blijft gerespecteerd door cmd.
        var schtasksArgs = $"/create /tn \"{TaskName}\" {trigger} /tr \"\\\"{exePath}\\\" /autoupdate\" /f /rl highest";
        var logPath = LogPath;
        TryDelete(logPath);
        var cmdArgs = $"/c \"schtasks.exe {schtasksArgs} > \"{logPath}\" 2>&1\"";

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas"
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
                return new CreateTaskOutcome(CreateTaskResult.Success);

            var schtasksOutput = TryReadLog(logPath);
            return new CreateTaskOutcome(
                CreateTaskResult.Failed,
                BuildFailureMessage(process.ExitCode, schtasksOutput, schtasksArgs));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new CreateTaskOutcome(CreateTaskResult.UserCancelled);
        }
        catch (Exception ex)
        {
            return new CreateTaskOutcome(CreateTaskResult.Failed, $"Process kon niet starten: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static string TryReadLog(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    private static string BuildFailureMessage(int exitCode, string schtasksOutput, string schtasksArgs)
    {
        // Geef de schtasks output één-op-één terug, gevolgd door context (exit
        // code + de gebruikte arguments). Helpt bij debug van quoting/path
        // issues — user ziet de echte schtasks fout i.p.v. een generic message.
        var output = string.IsNullOrWhiteSpace(schtasksOutput) ? "(geen output)" : schtasksOutput;
        return $"schtasks exit {exitCode}\n\n{output}\n\nargs: {schtasksArgs}";
    }

    public async Task<bool> DeleteUpdateTaskAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{TaskName}\" /f",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas"
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            // Exit code 0 = success, 1 = task doesn't exist (also OK).
            return process.ExitCode == 0 || process.ExitCode == 1;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TaskExistsAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

public enum UpdateScheduleType
{
    Daily,
    Weekly,
    OnStartup
}

public enum CreateTaskResult
{
    Success,
    UserCancelled,
    Failed
}

// Combineert resultaat-status met de gevangen schtasks output zodat de UI
// bij Failed de echte fout kan tonen i.p.v. een generic "could not create" tekst.
// ErrorOutput is alleen gevuld bij Failed; bij Success / UserCancelled blijft 'm null.
public sealed record CreateTaskOutcome(CreateTaskResult Result, string? ErrorOutput = null);
