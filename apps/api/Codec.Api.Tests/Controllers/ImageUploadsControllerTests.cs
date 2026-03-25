using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class ImageUploadsControllerTests
{
    private readonly Mock<IImageUploadService> _imageUpload = new();
    private readonly Mock<IFileUploadService> _fileUpload = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly ImageUploadsController _controller;

    public ImageUploadsControllerTests()
    {
        _controller = new ImageUploadsController(_imageUpload.Object, _fileUpload.Object, _userService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "google-1"),
                    new Claim("name", "Test"),
                    new Claim("email", "test@test.com")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((new User { Id = Guid.NewGuid(), GoogleSubject = "google-1", DisplayName = "Test" }, false));
    }

    [Fact]
    public async Task UploadImage_InvalidFile_ReturnsBadRequest()
    {
        var file = new Mock<IFormFile>();
        _imageUpload.Setup(s => s.Validate(file.Object)).Returns("File is empty.");

        var result = await _controller.UploadImage(file.Object);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadImage_ValidFile_ReturnsOkWithUrl()
    {
        var file = new Mock<IFormFile>();
        _imageUpload.Setup(s => s.Validate(file.Object)).Returns((string?)null);
        _imageUpload.Setup(s => s.SaveImageAsync(It.IsAny<Guid>(), file.Object))
            .ReturnsAsync("https://storage.example.com/images/test.png");

        var result = await _controller.UploadImage(file.Object);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadImage_CallsSaveWithCorrectUserId()
    {
        var userId = Guid.NewGuid();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((new User { Id = userId, GoogleSubject = "google-1", DisplayName = "Test" }, false));

        var file = new Mock<IFormFile>();
        _imageUpload.Setup(s => s.Validate(file.Object)).Returns((string?)null);
        _imageUpload.Setup(s => s.SaveImageAsync(userId, file.Object))
            .ReturnsAsync("https://storage.example.com/images/test.png");

        await _controller.UploadImage(file.Object);

        _imageUpload.Verify(s => s.SaveImageAsync(userId, file.Object), Times.Once);
    }

    // --- UploadFile tests ---

    [Fact]
    public async Task UploadFile_InvalidFile_ReturnsBadRequest()
    {
        var file = new Mock<IFormFile>();
        _fileUpload.Setup(s => s.Validate(file.Object)).Returns("Unsupported file type.");

        var result = await _controller.UploadFile(file.Object);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadFile_ValidFile_ReturnsOkWithMetadata()
    {
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("document.pdf");
        file.Setup(f => f.Length).Returns(12345);
        file.Setup(f => f.ContentType).Returns("application/pdf");

        _fileUpload.Setup(s => s.Validate(file.Object)).Returns((string?)null);
        _fileUpload.Setup(s => s.SaveFileAsync(It.IsAny<Guid>(), file.Object))
            .ReturnsAsync("https://storage.example.com/files/document.pdf");

        var result = await _controller.UploadFile(file.Object);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadFile_CallsSaveWithCorrectUserId()
    {
        var userId = Guid.NewGuid();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((new User { Id = userId, GoogleSubject = "google-1", DisplayName = "Test" }, false));

        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("readme.txt");
        file.Setup(f => f.Length).Returns(100);
        file.Setup(f => f.ContentType).Returns("text/plain");
        _fileUpload.Setup(s => s.Validate(file.Object)).Returns((string?)null);
        _fileUpload.Setup(s => s.SaveFileAsync(userId, file.Object))
            .ReturnsAsync("https://storage.example.com/files/readme.txt");

        await _controller.UploadFile(file.Object);

        _fileUpload.Verify(s => s.SaveFileAsync(userId, file.Object), Times.Once);
    }

    [Fact]
    public async Task UploadFile_ValidationErrorMessage_PropagatedInResponse()
    {
        var file = new Mock<IFormFile>();
        _fileUpload.Setup(s => s.Validate(file.Object)).Returns("File exceeds maximum size.");

        var result = await _controller.UploadFile(file.Object);
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadImage_ValidationErrorMessage_PropagatedInResponse()
    {
        var file = new Mock<IFormFile>();
        _imageUpload.Setup(s => s.Validate(file.Object)).Returns("Only JPG, PNG, WebP, and GIF files are allowed.");

        var result = await _controller.UploadImage(file.Object);
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().NotBeNull();
    }
}
