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
    private readonly Mock<IUserService> _userService = new();
    private readonly ImageUploadsController _controller;

    public ImageUploadsControllerTests()
    {
        _controller = new ImageUploadsController(_imageUpload.Object, _userService.Object);
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
            .ReturnsAsync(new User { Id = Guid.NewGuid(), GoogleSubject = "google-1", DisplayName = "Test" });
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
}
