namespace PrintFileManager;

public struct GcodeFileSendJob
{
    public Printer TargetPrinter { get; private set; }
    public Task<Task<bool>> SendFileTask { get; private set; }

    /// <summary>Initializes a new instance of the <see cref="T:System.Object" /> class.</summary>
    public GcodeFileSendJob(Printer targetPrinter, Task<Task<bool>> sendFileTask)
    {
        this.TargetPrinter = targetPrinter;
        this.SendFileTask = sendFileTask;
    }
}