using Codec.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class HealthControllerTests
{
    [Fact]
    public void GetInfo_InDevelopment_ReturnsOk()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        var controller = new HealthController(env.Object);

        var result = controller.GetInfo();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetInfo_InProduction_ReturnsNotFound()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var controller = new HealthController(env.Object);

        var result = controller.GetInfo();
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetHealth_ReturnsOk()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        var controller = new HealthController(env.Object);

        var result = controller.GetHealth();
        result.Should().BeOfType<OkObjectResult>();
    }
}
