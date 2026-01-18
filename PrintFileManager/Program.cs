using System.Globalization;
using System.Reflection;
using System.Text;
using CsvHelper;
using Microsoft.Extensions.Configuration;

namespace PrintFileManager;

class Program
{
    //TODO: All this "Program" shit should go to somewhere else that makes sense...
    private static readonly string SettingsFile = "config.ini";
    private static readonly string PrintersFile = "printers.csv";
    private static FileSystemWatcher _watcher;
    private static string archivePath = "";
    
    //The list of files that have been added while running
    private static readonly AsyncSafeList<string> fileQueue = new();

    public static bool deleteOnSend;
    public static bool LogToFile = false;
    public static string LogPath;
    public static string WatchPath;
    public static IConfigurationRoot Config;
    public static List<Printer> Printers = new();
    //This should probably be in a different place and read only
    public static string FullPathPendingGcode = string.Empty;
    
    private static CancellationTokenSource? _debounceCts;
    
    
    static async Task Main(string[] args)
    {
        HandleSettings();
        ReadPrinters();
        SetupWatcher(WatchPath);
        PendingJobManager.SetupPendingJobManaber();
        
        //Remember, you're here forever.
        await Task.Delay(Timeout.Infinite);
    }
    
    /// <summary>
    /// Handles when a file is changed. All files get a changed handler event after they are created, so we listen to that.
    /// This method will handle removing duplicates from the master list so that only the latest event is acted on. 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private static async void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Utils.Log($"File {e.FullPath} has been changed.");
        
        if(!(await fileQueue.GetSnapshotAsync()).Contains(e.FullPath))
        {
            // Utils.Log("Did not find path inside pending sends, adding...");
            await fileQueue.AddAsync(e.FullPath);
            await PendingJobManager.DeletePendingFile(e.FullPath);
        }
        else
        {
            // Utils.Log("Not adding because we were already in the list!");   
        }
        
        WaitForTimeout(TimeSpan.FromSeconds(5));
    }

    private static void WaitForTimeout(TimeSpan delay)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();

        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                await Task.Run(async () =>
                {
                   await StartProcessingGcodeFiles();
                });
            }
            catch (TaskCanceledException)
            {

            }
        }, token);
    }

    /// <summary>
    /// Starts processing GCode files that have shown up in the watch folder
    /// </summary>
    private static async Task StartProcessingGcodeFiles()
    {
        var workingList = await fileQueue.GetSnapshotAsync();
        string output = "";
        foreach (var file in workingList)
        {
            output += file + "\n";
        }
        Utils.Log($"Working on the following files:\n{output.TrimEnd()}");
        
        foreach(var file in workingList)
        {
            await ProcessGcodeFile(file);
        }
    }

    /// <summary>
    /// Processes a single GCode file
    /// </summary>
    /// <param name="filePath"></param>
    public static async Task ProcessGcodeFile(string filePath)
    {
        if (!string.IsNullOrEmpty(archivePath))
        {
            var filename = Path.GetFileName(filePath);
            File.Copy(filePath, Path.Combine(archivePath, filename), true);
        }
        
        Utils.Log($"Processing {filePath}...");
        var gcodeSender = new GcodeFile(filePath);

        var sendResult = await gcodeSender.SendFile();
        Utils.Log($"File {filePath} has been processed");
        await fileQueue.RemoveAsync(filePath);

        //All sends complete, delete the file.
        if (sendResult.All(x => x.SendFileTask.Result.Result))
        {
            FileUtils.DeleteFile(filePath);
        }
    }
    

    static void SetupWatcher(string path)
    {
        if (!Path.Exists(path))
        {
            throw new Exception("Path not found: " + path);
        }

        _watcher = new FileSystemWatcher(path);
        _watcher.NotifyFilter = NotifyFilters.CreationTime
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.CreationTime
                               | NotifyFilters.LastAccess
                               | NotifyFilters.Security
                               | NotifyFilters.Attributes
                               | NotifyFilters.Size;

        _watcher.Filter = "*.*";

        _watcher.Changed += OnChanged;
        
        _watcher.EnableRaisingEvents = true;
        
        Utils.Log($"Monitoring {path}");
    }
    
    /// <summary>
    /// Handles reading in the settings file. If the settings file does not exist, creates one.
    /// </summary>
    /// <exception cref="Exception"></exception>
    static void HandleSettings()
    {
        var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFile);
        
        if (!Path.Exists(settingsPath))
        {
            Utils.Log("Settings file not found at: " + settingsPath + " - A new file will be created.");

            var defaultIni = new StringBuilder()
                .AppendLine("; Auto-generated config file")
                .AppendLine("; Created: " + DateTime.Now)
                .AppendLine("; WatchPath may be an absolute or relative path from the running exe.")
                .AppendLine("; If LogPath is empty, log is created next to the exe.")
                .AppendLine("; If ArchivePath is set, files will be copied to that path when they are detected.")
                .AppendLine()
                .AppendLine("[App]")
                .AppendLine("WatchPath=slicedFiles")
                .AppendLine("ArchivePath=")
                .AppendLine("DeleteOnSend=true")
                .AppendLine("LogPath=")
                .AppendLine("LogFileEnabled=true")
                .AppendLine("RecheckInterval=30")
                .AppendLine("MachineString=;Sliced for");

            File.WriteAllText(settingsPath, defaultIni.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Utils.Log("Settings file created at: " + settingsPath);
            Utils.Log("Open the ini and set your desired gcode file path to monitor.");
            
            CreateMachineTypesFiles();
            CreatePrintersFiles();
            
            Environment.Exit(0);
        }
        
        CreateMachineTypesFiles();
        
        Config = new ConfigurationBuilder().AddIniFile("config.ini").Build();
        
        if (string.IsNullOrEmpty(Config.GetSection("App")["LogPath"]))
        {
            LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
            Utils.Log($"WatchPath {LogPath} ini is invalid.");
        }

        var settingsWatchPath = Config.GetSection("App")["WatchPath"];
        if (string.IsNullOrEmpty(settingsWatchPath))
        {
            throw new Exception("WatchPath in ini is invalid.");
        }
        
        if (!bool.TryParse(Config.GetSection("App")["LogFileEnabled"], out bool logFileEnabled))
        {
            throw new Exception("LogFileEnabled in ini is invalid. Must be true or false");
        }
        
        LogToFile = logFileEnabled;
        
        if (!bool.TryParse(Config.GetSection("App")["DeleteOnSend"], out bool deleteOnSendEnabled))
        {
            throw new Exception("DeleteOnSend in ini is invalid. Must be true or false");
        }

        deleteOnSend = deleteOnSendEnabled;
            
        if (string.IsNullOrEmpty(Config.GetSection("App")["MachineType"]))
        {
            throw new Exception("MachineType in ini is invalid.");
        }
        
        if (string.IsNullOrEmpty(Config.GetSection("App")["RecheckInterval"]))
        {
            throw new Exception("RecheckInterval in ini is invalid.");
        }

        archivePath = Config.GetSection("App")["ArchivePath"]!;
        
        if (!string.IsNullOrEmpty(archivePath) && !Path.IsPathFullyQualified(archivePath))
        {
            
            throw new Exception("Archive path is not fully qualified file path.");
        }
        
        if (!string.IsNullOrEmpty(archivePath) && !Path.Exists(archivePath))
        {
            Directory.CreateDirectory(archivePath);
        }
        
        string fullPathToWatchDir;

        if (Path.IsPathFullyQualified(settingsWatchPath))
        {
            fullPathToWatchDir = settingsWatchPath;
        }
        else
        {
            fullPathToWatchDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsWatchPath);
        }

        if (!Path.Exists(fullPathToWatchDir))
        {
            Directory.CreateDirectory(fullPathToWatchDir);
            Utils.Log("Created: " + fullPathToWatchDir);
        }
        
        FullPathPendingGcode = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PendingFiles");
        if (!Path.Exists(FullPathPendingGcode))
        {
            Directory.CreateDirectory(FullPathPendingGcode);
            Utils.Log("Created: " + FullPathPendingGcode);
        }
        
        Utils.Log("Setting watch path to: " + fullPathToWatchDir);
        WatchPath = fullPathToWatchDir;
    }

    /// <summary>
    /// Creates some example machine type files if none exist.
    /// </summary>
    private static void CreateMachineTypesFiles()
    {
        var fullPathToMachineDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetworkTypes");
        if (!Path.Exists(fullPathToMachineDir))
        {
            Directory.CreateDirectory(fullPathToMachineDir);
            Utils.Log("Created: " + fullPathToMachineDir);

            var octoprint = @"curl.exe -k -H ""X-Api-Key: [ApiKey]"" -F ""select=false"" -F ""print=false"" -F ""file=@[FilePath]"" ""http://[NetworkAddress]/api/files/local""";
            var octoprintFile = Path.Combine(fullPathToMachineDir, "octoprint.txt");
            File.WriteAllText(octoprintFile, octoprint, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Utils.Log("Octoprint CURL file created at: " + octoprintFile);
            
            var moonraker = @"curl.exe -F ""file=@[FilePath]"" ""http://[NetworkAddress]:7125/server/files/upload""";
            var moonrakerFile = Path.Combine(fullPathToMachineDir, "moonraker.txt");
            File.WriteAllText(moonrakerFile, moonraker, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Utils.Log("Moonraker CURL file created at: " + moonrakerFile);
        }
    }

    private static void CreatePrintersFiles()
    {
        var printersPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PrintersFile);
        
        if (!Path.Exists(printersPath))
        {
            Utils.Log("Printers file not found at: " + printersPath + " - A new file will be created.");

            var defaultIni = new StringBuilder()
                .AppendLine("MachineType,NetworkAddress,ApiKey,NetworkType")
                .AppendLine("Ender3,Printer.Domain.com,12345abc,octoprint")
                .AppendLine("ECC,ecc.Domain.com,,openCentauriCarbon")
                .AppendLine("SovolZero,192.168.0.1,,moonraker");

            File.WriteAllText(printersPath, defaultIni.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Utils.Log("Printers file created at: " + printersPath);
            Utils.Log("Open the csv and setup your printers.");
            Environment.Exit(0);
        }
    }
    
    /// <summary>
    /// Reads in all printers from the printers.csv file. Creates a blank one if one does not exist.
    /// </summary>
    private static void ReadPrinters()
    {
        CreatePrintersFiles();
        
        //Find all classes that inherit from Printer
        var baseType = typeof(Printer);
        Dictionary<string, Type> derivedTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(baseType))
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        
        using (var reader = new StreamReader("printers.csv"))
        using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
        {
            // Read rows manually to handle dynamic instantiation
            csv.Read();
            csv.ReadHeader();

            while (csv.Read())
            {
                string networkType = csv.GetField("NetworkType")!;

                // Logic: Use derived type if it exists, otherwise use base Printer
                Type typeToInstantiate = derivedTypes.ContainsKey(networkType) 
                    ? derivedTypes[networkType] 
                    : typeof(Printer);

                var printer = (Printer)csv.GetRecord(typeToInstantiate);
                Printers.Add(printer);
            }
        }
        
        if (Printers.Count == 0)
        {
            Utils.Log("No printers found. Please add printers to printers.csv.");
            Environment.Exit(0);
        }
        
        Utils.Log($"Found {Printers.Count} printers!");

        foreach (var printer in Printers)
        {
            printer.ReadUploadCommand();   
        }
    }
}