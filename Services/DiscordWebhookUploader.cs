using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public static class DiscordWebhookUploader
{
    private static readonly HttpClient _http = new HttpClient();

    public static async Task SendFileAsync(string webhookUrl, string filePath, string fileName, string message)
    {
        using var form = new MultipartFormDataContent();

        // Message
        form.Add(new StringContent(message), "content");

        // File
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(fileContent, "files[0]", fileName);

        var resp = await _http.PostAsync(webhookUrl, form);
        resp.EnsureSuccessStatusCode();
    }
}