using PrintFileManager;

public class openCentauriCarbon : Printer
{
    public override async Task<bool> SendFile(string filePath)
    {
        if (!await TestPrinterNetworkAccess())
        {
            return false;
        }

        const int CHUNK_SIZE = 1024 * 1024; //1MB per documentation
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
                byte[] buffer = new byte[CHUNK_SIZE];
                int bytesRead;
                long currentOffset = 0;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, CHUNK_SIZE)) > 0)
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
            await Task.Delay(1000); //Force small delay between sends!
            Lock.Release();
        }

        return true;
    }
}