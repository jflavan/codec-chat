using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Codec.Api.Tests.Services;

public class FileUploadServiceTests
{
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly FileUploadService _svc;

    public FileUploadServiceTests()
    {
        _svc = new FileUploadService(_fileStorage.Object);
    }

    [Fact]
    public void Validate_EmptyFile_ReturnsError()
    {
        var file = CreateFormFile(0, "application/pdf", "doc.pdf");
        _svc.Validate(file).Should().Be("File is empty.");
    }

    [Fact]
    public void Validate_OversizedFile_ReturnsError()
    {
        var file = CreateFormFile(26 * 1024 * 1024, "application/pdf", "doc.pdf");
        _svc.Validate(file).Should().Contain("MB limit");
    }

    [Fact]
    public void Validate_ExactlyAtSizeLimit_ReturnsNull()
    {
        var file = CreateFormFile(25 * 1024 * 1024, "application/pdf", "doc.pdf");
        _svc.Validate(file).Should().BeNull();
    }

    [Fact]
    public void Validate_InvalidContentType_ReturnsError()
    {
        var file = CreateFormFile(1024, "application/octet-stream", "file.bin");
        _svc.Validate(file).Should().Contain("Unsupported file type");
    }

    [Fact]
    public void Validate_InvalidExtension_ReturnsError()
    {
        var file = CreateFormFile(1024, "application/pdf", "doc.exe");
        _svc.Validate(file).Should().Contain("Unsupported file extension");
    }

    [Fact]
    public void Validate_NoExtension_ReturnsError()
    {
        var file = CreateFormFile(1024, "application/pdf", "document");
        _svc.Validate(file).Should().Contain("Unsupported file extension");
    }

    [Theory]
    [InlineData("application/pdf", "report.pdf")]
    [InlineData("application/msword", "letter.doc")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "letter.docx")]
    [InlineData("application/vnd.ms-excel", "data.xls")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "data.xlsx")]
    [InlineData("application/vnd.ms-powerpoint", "slides.ppt")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", "slides.pptx")]
    [InlineData("text/plain", "notes.txt")]
    [InlineData("text/csv", "data.csv")]
    [InlineData("text/markdown", "readme.md")]
    [InlineData("application/rtf", "doc.rtf")]
    public void Validate_ValidDocument_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    [Theory]
    [InlineData("application/zip", "archive.zip")]
    [InlineData("application/x-tar", "archive.tar")]
    [InlineData("application/gzip", "archive.gz")]
    [InlineData("application/x-7z-compressed", "archive.7z")]
    [InlineData("application/x-rar-compressed", "archive.rar")]
    public void Validate_ValidArchive_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    [Theory]
    [InlineData("application/json", "config.json")]
    [InlineData("application/xml", "data.xml")]
    [InlineData("text/xml", "feed.xml")]
    [InlineData("text/html", "page.html")]
    [InlineData("text/css", "styles.css")]
    [InlineData("text/javascript", "app.js")]
    [InlineData("application/javascript", "lib.js")]
    public void Validate_ValidCodeFile_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    [Theory]
    [InlineData("audio/mpeg", "song.mp3")]
    [InlineData("audio/ogg", "clip.ogg")]
    [InlineData("audio/wav", "sound.wav")]
    [InlineData("audio/webm", "audio.webm")]
    public void Validate_ValidAudioFile_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    [Theory]
    [InlineData("video/mp4", "video.mp4")]
    [InlineData("video/webm", "clip.webm")]
    [InlineData("video/ogg", "movie.ogg")]
    public void Validate_ValidVideoFile_ReturnsNull(string contentType, string fileName)
    {
        var file = CreateFormFile(1024, contentType, fileName);
        _svc.Validate(file).Should().BeNull();
    }

    [Fact]
    public void Validate_ContentTypeCaseInsensitive_ReturnsNull()
    {
        var file = CreateFormFile(1024, "APPLICATION/PDF", "doc.pdf");
        _svc.Validate(file).Should().BeNull();
    }

    [Fact]
    public void Validate_ExtensionCaseInsensitive_ReturnsNull()
    {
        var file = CreateFormFile(1024, "application/pdf", "doc.PDF");
        _svc.Validate(file).Should().BeNull();
    }

    [Fact]
    public async Task SaveFileAsync_UploadsWithHashFilename()
    {
        var userId = Guid.NewGuid();
        var file = CreateFormFile(100, "application/pdf", "report.pdf");

        _fileStorage.Setup(x => x.UploadAsync("files", It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/files/test.pdf");

        var result = await _svc.SaveFileAsync(userId, file);

        result.Should().Be("https://storage.example.com/files/test.pdf");
        _fileStorage.Verify(x => x.UploadAsync("files",
            It.Is<string>(p => p.StartsWith($"{userId}/") && p.EndsWith(".pdf")),
            It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveFileAsync_PreservesExtensionLowercased()
    {
        var userId = Guid.NewGuid();
        var file = CreateFormFile(100, "application/zip", "Archive.ZIP");

        _fileStorage.Setup(x => x.UploadAsync("files", It.IsAny<string>(), It.IsAny<Stream>(), "application/zip", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/files/test.zip");

        await _svc.SaveFileAsync(userId, file);

        _fileStorage.Verify(x => x.UploadAsync("files",
            It.Is<string>(p => p.EndsWith(".zip")),
            It.IsAny<Stream>(), "application/zip", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveFileAsync_UsesFilesContainer()
    {
        var userId = Guid.NewGuid();
        var file = CreateFormFile(100, "text/plain", "notes.txt");

        _fileStorage.Setup(x => x.UploadAsync("files", It.IsAny<string>(), It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/files/test.txt");

        await _svc.SaveFileAsync(userId, file);

        _fileStorage.Verify(x => x.UploadAsync(
            "files",
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            "text/plain",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveFileAsync_DeterministicHash_SameContentSamePath()
    {
        var userId = Guid.NewGuid();
        var capturedPaths = new List<string>();

        _fileStorage.Setup(x => x.UploadAsync("files", Capture.In(capturedPaths), It.IsAny<Stream>(), "application/pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/files/test.pdf");

        var file1 = CreateFormFile(100, "application/pdf", "report.pdf");
        var file2 = CreateFormFile(100, "application/pdf", "report.pdf");

        await _svc.SaveFileAsync(userId, file1);
        await _svc.SaveFileAsync(userId, file2);

        capturedPaths.Should().HaveCount(2);
        capturedPaths[0].Should().Be(capturedPaths[1]);
    }

    private static IFormFile CreateFormFile(long length, string contentType, string fileName)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.Length).Returns(length);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[Math.Max(length, 0)]));
        return mock.Object;
    }
}
