using PrintFileManager;

public class openCentauriCarbon : Printer
{
    private const int ChunkSize = 1024 * 1024; //1MB per documentation
    
    /// <summary>
    /// Specific override for the OCC printer to send files. Based on
    /// https://docs.opencentauri.cc/software/api/#http-file-transfer-interface
    /// </summary>
    /// <param name="filePath">File path to send</param>
    /// <returns>True is success, false otherwise.</returns>
    public override async Task<bool> SendFile(string filePath)
    {
        if (!await TestPrinterNetworkAccess())
        {
            return false;
        }
        
        string url = $"http://{NetworkAddress}:3030/uploadFile/upload";

        long totalSize = new FileInfo(filePath).Length;
        string md5 = FileUtils.GetMd5(filePath);
        string uuid = Guid.NewGuid().ToString();

        Utils.Log($"Sending to: {NetworkAddress} using openCentauriCarbon specific upload method.");
        
        await Lock.WaitAsync();
        try
        {
            using (var client = new HttpClient())
            using (var fileStream = File.OpenRead(filePath))
            {
                byte[] buffer = new byte[ChunkSize];
                int bytesRead;
                long currentOffset = 0;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, ChunkSize)) > 0)
                {
                    //Prepare the chunk
                    using (var content = new MultipartFormDataContent())
                    {
                        content.Add(new StringContent(md5), "S-File-MD5");
                        content.Add(new StringContent("1"), "Check");
                        content.Add(new StringContent(currentOffset.ToString()), "Offset");
                        content.Add(new StringContent(uuid), "Uuid");
                        content.Add(new StringContent(totalSize.ToString()), "TotalSize");

                        //Create the byte content for the current chunk
                        var fileContent = new ByteArrayContent(buffer, 0, bytesRead);
                        content.Add(fileContent, "File", Path.GetFileName(filePath));

                        // Send the chunk
                        var response = await client.PostAsync(url, content);
                        string result = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            Utils.Log($"{NetworkAddress} Upload failed at offset {currentOffset}: {result}");
                            return false;
                        }

                        currentOffset += bytesRead;
                    }
                }
            }
        }
        catch(Exception e)
        {
            Utils.Log($"Exception on {NetworkAddress}\n{e.Message}");
            return false;
        }
        finally
        {
            Lock.Release();
        }

        Utils.Log($"File sent to {NetworkAddress}");
        return true;
    }
}