using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WingetAppDeployer_WinUI.Services;

// Wraps schtasks.exe to create, delete, query, and run the auto-update task.
// Uses a WinUI-specific task name so it can coexist with the WPF app's task.
public sealed class TaskSchedulerService
{
    private const string TaskName = "WingetAppDeployer_WinUI_AutoUpdate";
    private const int ErrorCancelled = 1223;

    public async Task<CreateTaskResult> CreateUpdateTaskAsync(UpdateScheduleType scheduleType, string? customTime = null)
    {
        var exePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(exePath))
            return CreateTaskResult.Failed;

        var trigger = scheduleType switch
        {
            UpdateScheduleType.Daily => $"/sc daily /st {customTime ?? "09:00"}",
            UpdateScheduleType.Weekly => $"/sc weekly /d MON /st {customTime ?? "09:00"}",
            UpdateScheduleType.OnStartup => "/sc onlogon",
            _ => "/sc daily /st 09:00"
        };

        // /rl highest + UseShellExecute=true + Verb=runas triggers the UAC prompt
        // needed to create a task that can run without showing the user.
        var arguments = $"/create /tn \"{TaskName}\" {trigger} /tr \"\\\"{exePath}\\\" /autoupdate\" /f /rl highest";

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Verb = "runas"
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? CreateTaskResult.Success : CreateTaskResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return CreateTaskResult.UserCancelled;
        }
        catch
        {
            return CreateTaskResult.Failed;
        }
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
