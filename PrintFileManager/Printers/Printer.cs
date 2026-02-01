using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection.Metadata.Ecma335;

namespace PrintFileManager;

public class Printer
{
    public string MachineType {get; set;}
    public string NetworkAddress {get; set;}
    public string ApiKey {get; set;}
    public string NetworkType {get; set;}
    private string uploadCommand;
    public bool lastConnectitvityResult { get; private set; }

    //Single lock for all instances, this will be to prevent multiple uploads running at once.
    protected static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// Reads the upload command from the MachineTypes file for that printer.
    /// </summary>
    public void ReadUploadCommand()
    {
        //For derived classes, like the OCC, bail here because we have bespoke code for them to send files.
        if (GetType() != typeof(Printer))
            return;
        
        var fullPathToMachineDir = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "NetworkTypes");
        var files = Directory.GetFiles(fullPathToMachineDir);
        
        var targetFile = files.FirstOrDefault(x => x.Contains(NetworkType));
        if (targetFile == null)
        {
            Utils.Log($"Unable to find uploadCommand file for MachineType {NetworkType} inside {fullPathToMachineDir}");
            return;
        }
        
        uploadCommand = File.ReadLines(targetFile).ToArray()[0];

        uploadCommand = uploadCommand.Replace("[ApiKey]", ApiKey)
            .Replace("[NetworkType]", NetworkType)
            .Replace("[NetworkAddress]", NetworkAddress);

        //
        if (!uploadCommand.Contains("-sS") && uploadCommand.ToLower().Contains("curl"))
        {
            uploadCommand += " -sS";
        }
    }

    /// <summary>
    /// Tests network access to a printer.
    /// </summary>
    /// <returns>True if it can communicate on the network</returns>
    public virtual async Task<bool> TestPrinterNetworkAccess()
    {
        if (Program.DebugAlwaysPassNetworkCheck)
        {
            Utils.Log($"DEBUG NETWORK CHECK PASSED {NetworkAddress}");
            return true;
        }
    
        try 
        {
            using (Ping pinger = new Ping())
            {
                var reply = await pinger.SendPingAsync(NetworkAddress, 4000);
            
                if (reply.Status != IPStatus.Success)
                {
                    lastConnectitvityResult = false;
                    return false;
                }
            }
        }
        catch (PingException ex) when (ex.InnerException is System.Net.Sockets.SocketException sx && sx.NativeErrorCode == 11001)
        {
            //Don't care if the exception is an unknown host. This only occurs when DNS/Basic network isn't working.
            //In which case, nothing will work anyway.
            //Utils.Log($"Ping failed for {NetworkAddress}: {ex.Message}");
            lastConnectitvityResult = false;
            return false;
        }

        lastConnectitvityResult = true;
        return true;
    }
    /// <summary>
    /// Attempts to send a file to a printer
    /// Inherit from Printer and override this function to create printer specific uploads in code
    /// Otherwise use Curl.
    /// </summary>
    /// <param name="filePath">File to send</param>
    /// <returns>True if success, false otherwise</returns>
    public virtual async Task<bool> SendFile(GcodeFile gcodeFile)
    {
        if (!await TestPrinterNetworkAccess())
        {
            return false;
        }

        await Lock.WaitAsync();
        try
        {
            var sendCmd = uploadCommand.Replace("[FilePath]", gcodeFile.FilePath);

            var processStart = new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                Arguments = $"/c {sendCmd}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Utils.Log($"Sending to: {NetworkAddress} Using:\n{sendCmd}");

            if (Program.DebugDontSend)
            {
                Utils.Log($"DEBUG SEND OFF FOR {NetworkAddress}");
                return true;
            }

            using var process = Process.Start(processStart)!;
            
            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            
            Utils.Log($"{NetworkAddress} STDOUT: {stdOut}");
            if (!string.IsNullOrEmpty(stdErr))
            {
                Utils.Log($"{NetworkAddress} STDERR: {stdErr}");
                return false;
            }

            return true;
        }
        catch(Exception e)
        {
            Utils.Log($"Exception on {NetworkAddress}\n{e.Message}");
            return false;
        }
        finally
        {
            //Creating too many instances for Curl too fast was unstable for me, so lets just delay between those.
            await Task.Delay(500);
            Lock.Release();
        }
    }

    /// <summary>Returns a string that represents the current object.</summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString()
    {
        return
            $"{nameof(uploadCommand)}: {uploadCommand}, {nameof(MachineType)}: {MachineType}, {nameof(NetworkAddress)}: {NetworkAddress}, {nameof(ApiKey)}: {ApiKey}, {nameof(NetworkType)}: {NetworkType}, {nameof(lastConnectitvityResult)}: {lastConnectitvityResult}";
    }
}