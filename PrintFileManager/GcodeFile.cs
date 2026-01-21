using System.Security.Cryptography;
using System.Text;

namespace PrintFileManager;

public class GcodeFile : IDisposable
{
    public readonly string FilePath;

    private readonly List<GcodeFileSendJob> _sendFileJobs;

    private StreamReader _streamReader = null!;
    private FileStream _fileStream = null!;
    private bool _isDisposed = false;
    
    public long TotalSize { get; private set; }
    public string Md5 { get; private set; } = null!;
    public string Uuid { get; private set; } = null!;

    public GcodeFile(string filePath)
    {
        FilePath = filePath;
        Init();
        _sendFileJobs = BuildSendJobs(FilePath);
    }

    public GcodeFile(string filePath, List<Printer> printers)
    {
        FilePath = filePath;
        Init();
        _sendFileJobs = BuildSendJobs(FilePath, printers);
    }

    private void Init()
    {
        CreateStreamReader(FilePath);
        
        using(var md5 = MD5.Create())
        {
            Md5 = BitConverter.ToString(md5.ComputeHash(_fileStream)).Replace("-", "").ToLower();
        }
        Uuid = Guid.NewGuid().ToString();
        TotalSize = new FileInfo(FilePath).Length;
    }

    /// <summary>
    /// Builds the send jobs that are required to move a GCode file to a printer
    /// </summary>
    /// <param name="path">Path of the file to send</param>
    /// <param name="printers">IEnumerable of printers to send to</param>
    /// <returns>Send Jobs</returns>
    private List<GcodeFileSendJob> BuildSendJobs(string path, IEnumerable<Printer> printers)
    {
        List<GcodeFileSendJob> list = new List<GcodeFileSendJob>();
        foreach (var targetPrinter in printers)
        {
            //This task<task stuff lets us setup the job now, but start it later on when we want.
            var job = new GcodeFileSendJob(targetPrinter, new Task<Task<bool>>(() => targetPrinter.SendFile(this)));
            list.Add(job);
        }

        // Utils.Log($"BuildSendJobs created for {path}");

        return list;
    }

    /// <summary>
    /// Builds a jobs to send a GCode file to all printers that the file is for.
    /// </summary>
    /// <param name="path">File to send</param>
    /// <returns>Send Jobs</returns>
    private List<GcodeFileSendJob> BuildSendJobs(string path)
    {
        var machineType = GetMachineTypeFromFile(path);
        var targetPrinters = Program.Printers.Where(x => x.MachineType == machineType);
        return BuildSendJobs(path, targetPrinters);
    }

    private void CreateStreamReader(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File {filePath} not found!");
        }
        
        //Grab an exclusive lock on the file
        _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _streamReader = new StreamReader(_fileStream);
    }

    /// <summary>
    /// Gets the machine type this file is sliced for from a GCode file
    /// </summary>
    /// <param name="filePath">GCode file to read</param>
    /// <returns>Machine type string</returns>
    /// <exception cref="InvalidOperationException"></exception>
    private string GetMachineTypeFromFile(string filePath)
    {
        string target = "";
        var searchString = Program.Config.GetSection("App")["MachineType"]!;
        string line;
        
        //Reset the stream reader before we use it to read the entire file.
        _streamReader.BaseStream.Position = 0;
        while ((line = _streamReader.ReadLine() ?? throw new InvalidOperationException()) != null)
        {
            if (line.StartsWith(searchString))
            {
                target = line.Replace(searchString, string.Empty).Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(target))
        {
            Utils.Log($"File {FilePath} has no target machine! No action will be taken.");
        }
        return target;
    }

    /// <summary>
    /// Sends this file to the machines that have jobs for it.
    /// </summary>
    public async Task<List<GcodeFileSendJob>> SendFile()
    {
        Utils.Log("Sending file... " + FilePath);
        foreach (var job in _sendFileJobs)
        {
            job.SendFileTask.Start();
        }

        await Task.WhenAll(_sendFileJobs.Select(x => x.SendFileTask.Result));

        await PendingJobManager.CreatePendingFile(_sendFileJobs, FilePath);
        return _sendFileJobs;
    }

    /// <summary>Returns a string that represents the current object.</summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return $"{nameof(FilePath)}: {FilePath}, {nameof(_sendFileJobs)}: {_sendFileJobs}";
    }

    /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;
        
        _isDisposed = true;
        _streamReader.Dispose();
        _fileStream.Dispose();
    }
}