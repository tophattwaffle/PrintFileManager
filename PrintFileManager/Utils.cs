namespace PrintFileManager;

public class Utils
{
    private static readonly object _lock = new();
    public static void Log(string msg)
    {
        string line = $"{DateTime.Now} | {msg}\n";
        lock (_lock)
        {
            Console.Write(line);
            if(!string.IsNullOrEmpty(Program.LogPath) && Program.LogToFile)
                File.AppendAllText(Program.LogPath, line);
        }
    }
}