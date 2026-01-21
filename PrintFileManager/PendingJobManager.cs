using System.Reflection.Metadata.Ecma335;
using System.Text;
using static PrintFileManager.Program;


namespace PrintFileManager;

public static class PendingJobManager
{
    private static int _recheckDelay = 5000;
    private static bool _timerRunning = false;

    private static readonly AsyncSafeList<PendingJob> PendingJobs = new();
    private static CancellationTokenSource _cts = new CancellationTokenSource();

    /// <summary>
    /// Starts the forever timer that will check for an offline machine to come online.
    /// Also reads in the pending files that may exist on the file system.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public static void SetupPendingJobManaber()
    {
        if (_timerRunning)
            return;

        if (!Int32.TryParse(Program.Config.GetSection("App")["RecheckInterval"], out int delay))
        {
            throw new Exception("RecheckDelay in ini is not a valid int.");
        }

        Utils.Log($"Will attempt to contact offline machines every: {delay} seconds");
        
        Task.Run(ReadPendingFiles);

        //This is here to start the loop and restart it if exceptions occur.
        _ = Task.Factory.StartNew(async () => 
        {
            // OUTER LOOP: Handles restarts after a crash
            while (!_cts.Token.IsCancellationRequested)
            {
                try 
                {
                    await TimerLoop(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break; // App is shutting down, exit the outer loop
                }
                catch (Exception ex) 
                { 
                    Utils.Log($"Exception in Timer Loop: {ex}");
            
                    // Wait a moment before restarting to prevent "log spamming" 
                    // if the error is persistent (e.g., database down)
                    await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
                }
            }
        }, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

        _timerRunning = true;
    }

    private static async void ReadPendingFiles()
    {
        await PendingJobs.ResetListAsync();

        var fl = Directory.GetFiles(FullPathPendingGcode, "*.pend").ToList();
        var fileList = new List<PendingJob>();

        foreach (var file in fl)
        {
            //Check if the gcode file still exists in the watch directory, it not delete the .pend
            if (!Path.Exists(Path.Combine(Program.WatchPath, Path.GetFileNameWithoutExtension(file) + ".gcode")))
            {
                Utils.Log($"Deleting pending file: {file} because gcode for it is missing in the watch directory.");
                File.Delete(file);
                continue;
            }

            fileList.Add(new PendingJob(file));
        }

        await PendingJobs.AddRangeAsync(fileList);

        Utils.Log($"Found {(await PendingJobs.GetSnapshotAsync()).Count} pending files");
    }

    /// <summary>
    /// Attempts to delete the pending file, if it exists
    /// </summary>
    /// <param name="fileName">File to delete</param>
    public static async Task DeletePendingFile(string fileName)
    {
        fileName = Path.GetFileNameWithoutExtension(fileName);
        var snapshot = await PendingJobs.GetSnapshotAsync(); 
        var target = snapshot.FirstOrDefault(x => x.GcodeFilePath.Equals(fileName));

        //Null, file isn't in the list and likely does not exist on the filesystem.
        if (target == null)
            return;

        await DeletePendingFile(target);
    }

    /// <summary>
    /// Deletes a pending job, if it exists.
    /// </summary>
    /// <param name="pendingJob"></param>
    public static async Task DeletePendingFile(PendingJob pendingJob)
    {
        var snapshot = await PendingJobs.GetSnapshotAsync();
        if (!snapshot.Contains(pendingJob))
        {
            Utils.Log($"Was asked to delete a pending job, but it didn't exist in the list. {pendingJob.GcodeFilePath}");
            return;
        }

        pendingJob.DeleteJobFile();
        await PendingJobs.RemoveAsync(pendingJob);
    }

    /// <summary>
    /// Creates a pending file based on the results of a send job.
    /// </summary>
    /// <param name="sendFilejobs">Jobs to create teh file from.</param>
    /// <param name="filePath">GCode file</param>
    public static async Task CreatePendingFile(List<GcodeFileSendJob> sendFilejobs, string filePath)
    {
        var pendingPrinters = sendFilejobs.Where(x => !x.SendFileTask.Result.Result)
            .Select(x => x.TargetPrinter).ToList();

        //No fails
        if (pendingPrinters.Count == 0)
        {
            return;
        }

        var pj = new PendingJob(filePath, pendingPrinters);

        await pj.CreateJobFile();
        await PendingJobs.AddAsync(pj);
    }

    private static async Task TimerLoop(CancellationToken token)
    {
        while (true)
        {
            await Task.Delay(_recheckDelay, token);

            var pendingFile = await PendingJobs.GetSnapshotAsync();

            var printersPending = pendingFile.SelectMany(x => x.Printers).Distinct().ToList();

            var testTasks = new List<Task>();

            foreach (var printer in printersPending)
            {
                testTasks.Add(printer.TestPrinterNetworkAccess());
            }

            await Task.WhenAll(testTasks);

            //What printers that have jobs waiting are online?
            var reachablePrinters = printersPending.Where(x => x.lastConnectitvityResult).ToList();

            //Dip, no printers online.
            if (reachablePrinters.Count == 0)
            {
                continue;
            }

            //What jobs contain a candidate printer?
            var targetJobs = pendingFile.Where(x => x.Printers.Any(p => reachablePrinters.Contains(p)));

            foreach (var job in targetJobs)
            {
                //Removes the job from the real list. We are currently working with a snapshot copy, so this is fine. 
                await DeletePendingFile(job);
                var result = await ProcessPendingFile(job);
                
                //All sends complete, delete the file.
                if (result.All(x => x.SendFileTask.Result.Result))
                {
                    FileUtils.DeleteFile(job.GcodeFilePath);
                }
            }
        }
    }

    private static async Task<List<GcodeFileSendJob>> ProcessPendingFile(PendingJob job)
    {
        Utils.Log($"Processing pending file: {job.GcodeFilePath}");
        var gcodeFile = new GcodeFile(job.GcodeFilePath, job.Printers);

        return await gcodeFile.SendFile();
    }
}