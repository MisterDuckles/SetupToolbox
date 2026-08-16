using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace SetupToolbox.Services;

// Wraps schtasks.exe to create, delete, query, and run the auto-update task.
// Uses a WinUI-specific task name so it can coexist with the WPF app's task.
public sealed class TaskSchedulerService
{
    private const string TaskName = "SetupToolbox_AutoUpdate";
    private const int ErrorCancelled = 1223;

    private static string LogPath => Path.Combine(Path.GetTempPath(), "SetupToolbox_schtasks.log");

    public async Task<CreateTaskOutcome> CreateUpdateTaskAsync(UpdateScheduleType scheduleType, string? customTime = null)
    {
        // We willen de exe-pad van DEZE app gebruiken voor de task action. Bij
        // `dotnet run` wijst Environment.ProcessPath naar de WinUI .exe (apphost),
        // bij self-contained publish ook. Als ProcessPath leeg is hebben we niks
        // om te schedulen.
        var exePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(exePath))
            return new CreateTaskOutcome(CreateTaskResult.Failed, "ProcessPath onbekend — kon de huidige exe niet bepalen.");

        // v1.0.14: task aanmaken via een eigen XML-definitie i.p.v. losse schtasks-
        // vlaggen. Reden: `schtasks /create` zet **DisallowStartIfOnBatteries** en
        // **StopIfGoingOnBatteries** standaard AAN. Op accustroom start Windows de
        // task dan domweg niet — mét `LastTaskResult = 0` en een bijgewerkte
        // LastRunTime, dus volledig stil en niet te onderscheiden van een geslaagde
        // run. Op een laptop draaiden de auto-updates daardoor feitelijk nooit.
        // Gemeten en bevestigd op 2026-08-16; alleen via /xml zijn die vlaggen te zetten.
        string xmlPath;
        try
        {
            xmlPath = WriteTaskXml(exePath, scheduleType, customTime);
        }
        catch (Exception ex)
        {
            return new CreateTaskOutcome(CreateTaskResult.Failed,
                $"Kon de task-definitie niet wegschrijven: {ex.Message}");
        }

        // Wrap in cmd.exe zodat we stdout+stderr kunnen redirecten naar een
        // tmp-logfile — dat kan niet wanneer UseShellExecute=true (vereist voor
        // Verb=runas → UAC). Format: cmd /c "schtasks ... > log 2>&1".
        var schtasksArgs = $"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f";
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
        finally
        {
            // De XML is alleen nodig tijdens /create; schtasks heeft 'm daarna
            // ingelezen in z'n eigen store.
            TryDelete(xmlPath);
        }
    }

    /// <summary>
    /// Schrijft een Task Scheduler XML-definitie naar %TEMP% en geeft het pad terug.
    /// Bewust expliciet over de settings die `schtasks` anders ongevraagd invult:
    /// de accu-vlaggen uit (anders draait de task nooit op een laptop) en
    /// StartWhenAvailable aan (haalt een gemiste run in als de machine sliep).
    /// </summary>
    private static string WriteTaskXml(string exePath, UpdateScheduleType scheduleType, string? customTime)
    {
        var time = customTime ?? "09:00";

        // StartBoundary vereist een volledige datum+tijd. Voor een terugkerende
        // trigger doet de datum zelf er niet toe, zolang 'ie in het verleden ligt.
        var start = $"2020-01-01T{time}:00";

        var trigger = scheduleType switch
        {
            UpdateScheduleType.Weekly =>
                $"<CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled>"
                + "<ScheduleByWeek><DaysOfWeek><Monday /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>"
                + "</CalendarTrigger>",
            UpdateScheduleType.OnStartup =>
                "<LogonTrigger><Enabled>true</Enabled></LogonTrigger>",
            _ =>
                $"<CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled>"
                + "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>"
                + "</CalendarTrigger>"
        };

        var user = Escape($@"{Environment.UserDomainName}\{Environment.UserName}");
        var command = Escape(exePath);

        // RunOnlyIfNetworkAvailable bewust op false: een run zonder netwerk faalt
        // netjes en meldt dat via een notificatie, terwijl een niet-gestarte task
        // volledig stil is — en precies dat stille falen is de bug die we hier fixen.
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
            + "  <RegistrationInfo>\n"
            + "    <Description>Setup Toolbox — werkt geinstalleerde apps bij via winget.</Description>\n"
            + "  </RegistrationInfo>\n"
            + $"  <Triggers>{trigger}</Triggers>\n"
            + "  <Principals>\n"
            + "    <Principal id=\"Author\">\n"
            + $"      <UserId>{user}</UserId>\n"
            + "      <LogonType>InteractiveToken</LogonType>\n"
            + "      <RunLevel>HighestAvailable</RunLevel>\n"
            + "    </Principal>\n"
            + "  </Principals>\n"
            + "  <Settings>\n"
            + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n"
            + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n"
            + "    <StartWhenAvailable>true</StartWhenAvailable>\n"
            + "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n"
            + "    <AllowStartOnDemand>true</AllowStartOnDemand>\n"
            + "    <Enabled>true</Enabled>\n"
            + "    <Hidden>false</Hidden>\n"
            + "    <WakeToRun>false</WakeToRun>\n"
            + "    <AllowHardTerminate>true</AllowHardTerminate>\n"
            + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n"
            + "    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>\n"
            + "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\n"
            + "  </Settings>\n"
            + "  <Actions Context=\"Author\">\n"
            + "    <Exec>\n"
            + $"      <Command>{command}</Command>\n"
            + "      <Arguments>/autoupdate</Arguments>\n"
            + "    </Exec>\n"
            + "  </Actions>\n"
            + "</Task>\n";

        var path = Path.Combine(Path.GetTempPath(), $"SetupToolbox_task_{Guid.NewGuid():N}.xml");

        // schtasks verwacht UTF-16 wanneer de declaratie dat zegt; Encoding.Unicode
        // schrijft UTF-16 LE met BOM.
        File.WriteAllText(path, xml, Encoding.Unicode);
        return path;
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? value;

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
