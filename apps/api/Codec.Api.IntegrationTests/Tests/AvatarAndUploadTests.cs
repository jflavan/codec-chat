using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Codec.Api.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Codec.Api.IntegrationTests.Tests;

public class AvatarAndUploadTests(CodecWebFactory factory) : IntegrationTestBase(factory)
{
    private static MultipartFormDataContent CreateImageForm(string fileName = "test.png", string contentType = "image/png", int sizeBytes = 100)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[sizeBytes]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task UploadAvatar_ReturnsOk()
    {
        var client = CreateClient("av-upload", "AvatarUploader");
        var form = CreateImageForm();

        var response = await client.PostAsync("/me/avatar", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("avatarUrl").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeleteAvatar_ReturnsOk()
    {
        var client = CreateClient("av-delete", "AvatarDeleter");

        var response = await client.DeleteAsync("/me/avatar");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadAvatar_InvalidType_ReturnsBadRequest()
    {
        var client = CreateClient("av-invalid", "InvalidUploader");
        var form = CreateImageForm("test.txt", "text/plain");

        var response = await client.PostAsync("/me/avatar", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadImage_ReturnsOk()
    {
        var client = CreateClient("img-upload", "ImageUploader");
        var form = CreateImageForm("photo.jpg", "image/jpeg");

        var response = await client.PostAsync("/uploads/images", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadImage_InvalidType_ReturnsBadRequest()
    {
        var client = CreateClient("img-invalid", "InvalidImgUploader");
        var form = CreateImageForm("doc.pdf", "application/pdf");

        var response = await client.PostAsync("/uploads/images", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadServerAvatar_ReturnsOk()
    {
        var client = CreateClient("av-server", "ServerAvatarUploader");
        var (serverId, _) = await CreateServerAsync(client);

        var form = CreateImageForm();
        var response = await client.PostAsync($"/servers/{serverId}/avatar", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteServerAvatar_ReturnsOk()
    {
        var client = CreateClient("av-server-del", "ServerAvatarDeleter");
        var (serverId, _) = await CreateServerAsync(client);

        var response = await client.DeleteAsync($"/servers/{serverId}/avatar");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadServerIcon_ReturnsOk()
    {
        var client = CreateClient("icon-upload", "IconUploader");
        var (serverId, _) = await CreateServerAsync(client);

        var form = CreateImageForm();
        var response = await client.PostAsync($"/servers/{serverId}/icon", form);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteServerIcon_ReturnsOk()
    {
        var client = CreateClient("icon-delete", "IconDeleter");
        var (serverId, _) = await CreateServerAsync(client);

        // Upload first then delete
        await client.PostAsync($"/servers/{serverId}/icon", CreateImageForm());
        var response = await client.DeleteAsync($"/servers/{serverId}/icon");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
