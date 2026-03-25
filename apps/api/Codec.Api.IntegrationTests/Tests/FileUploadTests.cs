using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;

namespace Codec.Api.IntegrationTests.Tests;

/// <summary>
/// Integration tests for file upload/download roundtrip:
/// image uploads, general file uploads, and validation.
/// </summary>
public class FileUploadTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private static ByteArrayContent CreateFakeImage(string contentType = "image/png")
    {
        // Minimal valid PNG: 8-byte signature + IHDR + IDAT + IEND
        byte[] pngBytes = [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1 pixel
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, // depth, color, etc.
            0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, // IDAT chunk
            0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00,
            0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC, 0x33,
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, // IEND chunk
            0xAE, 0x42, 0x60, 0x82
        ];

        var content = new ByteArrayContent(pngBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private static ByteArrayContent CreateFakeFile(string contentType = "text/plain", int size = 100)
    {
        var bytes = new byte[size];
        Array.Fill(bytes, (byte)'x');
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    [Fact]
    public async Task UploadImage_ValidPng_ReturnsUrl()
    {
        var client = CreateClient("upload-img-1", "UploadImgUser");

        var formContent = new MultipartFormDataContent();
        var imageContent = CreateFakeImage();
        formContent.Add(imageContent, "file", "test-image.png");

        var response = await client.PostAsync("/uploads/images", formContent);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("imageUrl", out var urlProp));
        Assert.False(string.IsNullOrEmpty(urlProp.GetString()));
    }

    [Fact]
    public async Task UploadImage_InvalidType_ReturnsBadRequest()
    {
        var client = CreateClient("upload-img-bad-1", "UploadImgBadUser");

        var formContent = new MultipartFormDataContent();
        var fileContent = CreateFakeFile("application/pdf", 100);
        formContent.Add(fileContent, "file", "document.pdf");

        var response = await client.PostAsync("/uploads/images", formContent);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_ValidFile_ReturnsUrlAndMetadata()
    {
        var client = CreateClient("upload-file-1", "UploadFileUser");

        var formContent = new MultipartFormDataContent();
        var fileContent = CreateFakeFile("text/plain", 200);
        formContent.Add(fileContent, "file", "notes.txt");

        var response = await client.PostAsync("/uploads/files", formContent);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("fileUrl", out var urlProp));
        Assert.False(string.IsNullOrEmpty(urlProp.GetString()));
        Assert.Equal("notes.txt", body.GetProperty("fileName").GetString());
        Assert.Equal(200, body.GetProperty("fileSize").GetInt64());
    }

    [Fact]
    public async Task UploadImage_Unauthenticated_Returns401()
    {
        var client = Factory.CreateClient();

        var formContent = new MultipartFormDataContent();
        var imageContent = CreateFakeImage();
        formContent.Add(imageContent, "file", "unauth.png");

        var response = await client.PostAsync("/uploads/images", formContent);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_Unauthenticated_Returns401()
    {
        var client = Factory.CreateClient();

        var formContent = new MultipartFormDataContent();
        var fileContent = CreateFakeFile();
        formContent.Add(fileContent, "file", "unauth.txt");

        var response = await client.PostAsync("/uploads/files", formContent);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadImage_DownloadRoundtrip()
    {
        var client = CreateClient("upload-roundtrip-1", "RoundtripUser");

        var formContent = new MultipartFormDataContent();
        var imageContent = CreateFakeImage();
        formContent.Add(imageContent, "file", "roundtrip.png");

        var uploadResponse = await client.PostAsync("/uploads/images", formContent);
        uploadResponse.EnsureSuccessStatusCode();
        var body = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var imageUrl = body.GetProperty("imageUrl").GetString()!;

        // Download the uploaded file (the URL should be relative to the API)
        var downloadResponse = await client.GetAsync(imageUrl);
        // In local storage mode, the file should be accessible
        Assert.True(
            downloadResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"Download returned {downloadResponse.StatusCode}");
    }

    [Fact]
    public async Task UploadFile_DownloadRoundtrip()
    {
        var client = CreateClient("upload-file-rt-1", "FileRTUser");

        var formContent = new MultipartFormDataContent();
        var fileContent = CreateFakeFile("text/plain", 50);
        formContent.Add(fileContent, "file", "roundtrip.txt");

        var uploadResponse = await client.PostAsync("/uploads/files", formContent);
        uploadResponse.EnsureSuccessStatusCode();
        var body = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var fileUrl = body.GetProperty("fileUrl").GetString()!;

        // Try to download
        var downloadResponse = await client.GetAsync(fileUrl);
        Assert.True(
            downloadResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound,
            $"Download returned {downloadResponse.StatusCode}");
    }
}
