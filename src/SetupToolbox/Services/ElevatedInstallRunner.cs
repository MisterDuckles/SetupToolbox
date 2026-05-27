using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AppModel = SetupToolbox.Models.App;

namespace SetupToolbox.Services;

public enum ElevatedRunResult { Completed, UacDeclined, Failed }

/// <summary>
/// Draait een winget-install-batch in een ge-eleveerd kind-proces zodat de héle
/// batch achter ÉÉN UAC-prompt valt i.p.v. een prompt per machine-scope app.
///
/// Waarom een apart proces: UAC triggeren vereist Process.Start met Verb="runas"
/// + UseShellExecute=true, en dat verbiedt stdout-redirect. We kunnen de output
/// van een elevated winget dus niet lezen zoals WingetService dat in-process doet.
/// Oplossing: we relaunchen onze EIGEN exe ge-eleveerd in "--install-runner"
/// headless modus (zie App.OnLaunched), die de installs draait en de voortgang
/// als JSONL naar een temp-bestand schrijft. Het onge-eleveerde UI-proces "tailt"
/// dat bestand en voedt exact dezelfde InstallProgress-stroom als de in-process
/// flow — de UI (InstallDialog) merkt geen verschil.
/// </summary>
public static class ElevatedInstallRunner
{
    public const string RunnerArg = "--install-runner";

    public static bool IsRunnerArg(string arg) =>
        arg.Equals(RunnerArg, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------
    // Parent-kant: schrijf job → start elevated kind → tail de progress-file.
    // ---------------------------------------------------------------------

    public static async Task<ElevatedRunResult> InstallAppsElevatedAsync(
        IReadOnlyList<AppModel> apps,
        IProgress<InstallProgress>? overall,
        int maxParallelism)
    {
        if (apps.Count == 0) return ElevatedRunResult.Completed;

        var workDir = Path.Combine(Path.GetTempPath(), "SetupToolbox", "runner-" + Guid.NewGuid().ToString("N"));
        var jobPath = Path.Combine(workDir, "job.json");
        var progressPath = Path.Combine(workDir, "progress.jsonl");

        try
        {
            Directory.CreateDirectory(workDir);
            var job = new InstallJob
            {
                MaxParallelism = maxParallelism,
                Apps = apps.Select(a => new InstallJobItem { Id = a.WingetId, Name = a.Name, Source = a.Source }).ToList()
            };
            File.WriteAllText(jobPath, JsonSerializer.Serialize(job));
            File.WriteAllText(progressPath, string.Empty); // zodat de tail-reader 'm meteen kan openen
        }
        catch
        {
            TryCleanup(workDir);
            return ElevatedRunResult.Failed;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) { TryCleanup(workDir); return ElevatedRunResult.Failed; }

        Helpers.Diagnostics.Log("install.log",
            $"RUNNER START exe={exePath} apps={apps.Count} parallel={maxParallelism} workDir={workDir}");

        Process? proc;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"{RunnerArg} \"{jobPath}\"",
                UseShellExecute = true,   // verplicht voor Verb=runas
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // UAC geweigerd (1223) of elevatie niet mogelijk → caller valt terug
            // op de in-process per-app flow zodat de user niet vastzit.
            Helpers.Diagnostics.Log("install.log",
                $"RUNNER FAIL UAC declined / elevation impossible (Win32 {ex.NativeErrorCode}: {ex.Message})");
            TryCleanup(workDir);
            return ElevatedRunResult.UacDeclined;
        }
        catch (Exception ex)
        {
            Helpers.Diagnostics.Log("install.log", $"RUNNER FAIL start exception: {ex.GetType().Name}: {ex.Message}");
            TryCleanup(workDir);
            return ElevatedRunResult.Failed;
        }

        if (proc == null)
        {
            Helpers.Diagnostics.Log("install.log", "RUNNER FAIL Process.Start returned null");
            TryCleanup(workDir);
            return ElevatedRunResult.Failed;
        }

        var pid = TryGetPid(proc);

        var byId = new Dictionary<string, AppModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in apps) byId[a.WingetId] = a;

        var processed = 0;
        try
        {
            while (true)
            {
                var exited = proc.HasExited;
                processed = DrainProgress(progressPath, processed, byId, overall);
                if (exited) break;
                await Task.Delay(250);
            }
            // Belt-and-suspenders: regels die ná de exit-check nog landden.
            processed = DrainProgress(progressPath, processed, byId, overall);
        }
        finally
        {
            sw.Stop();
            int exitCode = -1;
            try { exitCode = proc.ExitCode; } catch { /* mag falen als proc al disposed/uitgesloten */ }
            Helpers.Diagnostics.Log("install.log",
                $"RUNNER EXIT pid={pid} exitCode=0x{unchecked((uint)exitCode):X8} elapsed={sw.Elapsed.TotalSeconds:F1}s progressLines={processed}");
            proc.Dispose();
            TryCleanup(workDir);
        }

        return ElevatedRunResult.Completed;
    }

    private static int TryGetPid(Process p)
    {
        try { return p.Id; } catch { return -1; }
    }

    /// <summary>
    /// Leest nieuwe, volledige JSONL-regels (vanaf index <paramref name="processed"/>)
    /// en rapporteert ze als InstallProgress. Returnt het nieuwe aantal verwerkte
    /// regels. Het bestand groeit alleen aan, dus de laatste split-token is altijd
    /// een (mogelijk nog onvolledige) trailing regel die we overslaan tot 'ie af is.
    /// </summary>
    private static int DrainProgress(
        string path, int processed,
        Dictionary<string, AppModel> byId,
        IProgress<InstallProgress>? overall)
    {
        string content;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            content = sr.ReadToEnd();
        }
        catch { return processed; }

        var parts = content.Split('\n');
        var complete = parts.Length - 1; // laatste element = onvolledige/lege regel
        for (var i = processed; i < complete; i++)
        {
            var raw = parts[i].Trim();
            if (raw.Length == 0) continue;
            InstallProgressLine? line;
            try { line = JsonSerializer.Deserialize<InstallProgressLine>(raw); }
            catch { continue; }
            if (line == null || !byId.TryGetValue(line.WingetId, out var app)) continue;
            overall?.Report(new InstallProgress(line.Index, line.Total, app, (InstallPhase)line.Phase, line.Message));
        }
        return complete < processed ? processed : complete;
    }

    private static void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------------
    // Kind-kant: lees de job, draai de installs, schrijf voortgang naar JSONL.
    // ---------------------------------------------------------------------

    public static async Task RunChildAsync(string jobPath)
    {
        Helpers.Diagnostics.Log("install.log", $"CHILD START job={jobPath} pid={Environment.ProcessId}");

        InstallJob? job;
        try { job = JsonSerializer.Deserialize<InstallJob>(File.ReadAllText(jobPath)); }
        catch (Exception ex)
        {
            Helpers.Diagnostics.Log("install.log", $"CHILD EXIT could not read job: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        if (job?.Apps == null || job.Apps.Count == 0)
        {
            Helpers.Diagnostics.Log("install.log", "CHILD EXIT empty/invalid job");
            return;
        }

        var workDir = Path.GetDirectoryName(jobPath);
        if (string.IsNullOrEmpty(workDir))
        {
            Helpers.Diagnostics.Log("install.log", "CHILD EXIT no workDir");
            return;
        }
        var progressPath = Path.Combine(workDir, "progress.jsonl");

        // Alleen Name/WingetId/Source zetten — NIET App.IconImage aanraken (lazy
        // BitmapImage = UI-object, bestaat niet in dit headless proces).
        var apps = job.Apps
            .Select(j => new AppModel { Name = j.Name, WingetId = j.Id, Source = j.Source })
            .ToList();

        try
        {
            using var writer = new ProgressFileWriter(progressPath);
            await App.Winget.InstallAppsAsync(apps, writer, job.MaxParallelism);
            Helpers.Diagnostics.Log("install.log", $"CHILD EXIT ok apps={apps.Count} parallel={job.MaxParallelism}");
        }
        catch (Exception ex)
        {
            Helpers.Diagnostics.Log("install.log", $"CHILD EXIT exception: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    // Synchronous, thread-safe IProgress: parallelle installs rapporteren vanaf
    // meerdere threads tegelijk, dus serialiseren we de file-append met een lock.
    // (Progress<T> zou async naar een SyncContext posten — die is er in dit
    // headless proces niet, en de volgorde/atomiciteit zou onbetrouwbaar worden.)
    private sealed class ProgressFileWriter : IProgress<InstallProgress>, IDisposable
    {
        private readonly StreamWriter _sw;
        private readonly object _lock = new();

        public ProgressFileWriter(string path)
        {
            _sw = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }

        public void Report(InstallProgress p)
        {
            var line = JsonSerializer.Serialize(new InstallProgressLine
            {
                Index = p.CurrentIndex,
                Total = p.Total,
                WingetId = p.App.WingetId,
                Phase = (int)p.Phase,
                Message = p.Message
            });
            lock (_lock) { _sw.WriteLine(line); }
        }

        public void Dispose() => _sw.Dispose();
    }

    // ---------------------------------------------------------------------
    // IPC-DTO's (System.Text.Json) — bewust simpele classes met get/set.
    // ---------------------------------------------------------------------

    private sealed class InstallJob
    {
        public int MaxParallelism { get; set; } = 1;
        public List<InstallJobItem> Apps { get; set; } = new();
    }

    private sealed class InstallJobItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = "winget";
    }

    private sealed class InstallProgressLine
    {
        public int Index { get; set; }
        public int Total { get; set; }
        public string WingetId { get; set; } = string.Empty;
        public int Phase { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
