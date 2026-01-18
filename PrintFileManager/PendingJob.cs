using System.Text;

namespace PrintFileManager;

public class PendingJob
{
    public string GcodeFilePath { get; }
    public string PendingFilePath { get; private set; }
    public List<Printer> Printers { get; }

    /// <summary>Initializes a new instance of the <see cref="T:System.Object" /> class.</summary>
    /// This is used when we read a pending file in.
    public PendingJob(string pendingFilePath)
    {
        PendingFilePath = pendingFilePath;

        if (!Path.IsPathFullyQualified(pendingFilePath))
            pendingFilePath = Path.Combine(Program.FullPathPendingGcode, pendingFilePath + ".pend");

        if (!File.Exists(pendingFilePath))
        {
            throw new FileNotFoundException($"File {pendingFilePath} was not found");
        }

        List<Printer> pendingMachines = new List<Printer>();
        //For each line in each file
        foreach (var line in File.ReadLines(pendingFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            pendingMachines.Add(Program.Printers.First(x => x.NetworkAddress == line));
        }

        Printers = pendingMachines;
        
        GcodeFilePath = Path.Combine(Program.WatchPath, Path.GetFileNameWithoutExtension(pendingFilePath) + ".gcode");

        SetPendingFilePath();
    }

    public PendingJob(string gcodeFilePath, List<Printer> printers)
    {
        GcodeFilePath = gcodeFilePath;
        Printers = printers;
        SetPendingFilePath();
    }

    private void SetPendingFilePath()
    {
        var fileName = Path.GetFileNameWithoutExtension(GcodeFilePath) + ".pend";
        PendingFilePath = Path.Combine(Program.FullPathPendingGcode, fileName);
    }

    /// <summary>
    /// Creates a pending file for this job
    /// </summary>
    public async Task CreateJobFile()
    {
        var pendingMachinesString = new StringBuilder();
        foreach (var printer in Printers)
        {
            pendingMachinesString.AppendLine(printer.NetworkAddress);
        }

        await File.WriteAllTextAsync(PendingFilePath, pendingMachinesString.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Deletes the pending file for this job, if it exists.
    /// </summary>
    public void DeleteJobFile()
    {
        if (string.IsNullOrWhiteSpace(GcodeFilePath))
            return;
        
        if (File.Exists(PendingFilePath))
        {
            Utils.Log($"Deleting pending file: {PendingFilePath}");
            File.Delete(PendingFilePath);
        }
        else
        {
            Utils.Log($"Attempted to delete pending file: {PendingFilePath}, but it doesn't exist");
        }
    }

    /// <summary>Returns a string that represents the current object.</summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return $"{nameof(GcodeFilePath)}: {GcodeFilePath}, {nameof(PendingFilePath)}: {PendingFilePath}, {nameof(Printers)}: {Printers}";
    }
}