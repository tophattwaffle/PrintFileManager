using System.Diagnostics;
using System.Security.Cryptography;

namespace PrintFileManager;

public class FileUtils
{
    public static string GetMd5(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file was not found.", filePath);
        
        using (var md5 = MD5.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-","").ToLower();
            }
        }
    }
    
    public static long GetFileLength(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The specified file was not found.", filePath);

        FileInfo fi = new FileInfo(filePath);
        return fi.Length;
    }

    public static void DeleteFile(string filePath)
    {
        if (!File.Exists(filePath))
            return;
        
        File.Delete(filePath);
    }
    
    public static async Task<bool> WaitForFileToBeUnlocked(string fullPath, TimeSpan timeout)
    {
        if(timeout < TimeSpan.FromSeconds(1))
            timeout = TimeSpan.FromSeconds(1);

        var stopWatch = new Stopwatch();
        stopWatch.Start();

        while (IsFileLocked(fullPath))
        {
            if (stopWatch.Elapsed > timeout)
            {
                Utils.Log($"File {fullPath} did not unlock in time, aborting.");
                return false;
            }
            
            Utils.Log("Waiting for file lock on " + fullPath);
            await Task.Delay(200);
        }
        
        Utils.Log("No lock on " + fullPath);
        return true;
    }
    
    public static bool IsFileLocked(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentException("Path cannot be null or empty.", nameof(fullPath));

        if (!File.Exists(fullPath))
            return false;

        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}