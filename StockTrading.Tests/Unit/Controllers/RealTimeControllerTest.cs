using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using StockTrading.API.Controllers;
using StockTrading.API.Controllers.Market;
using StockTrading.API.Services;
using StockTrading.Application.Features.Market.Services;
using StockTrading.Application.Features.Users.DTOs;

namespace StockTrading.Tests.Unit.Controllers;

[TestSubject(typeof(RealTimeController))]
public class RealTimeControllerTest
{
    private readonly Mock<IRealTimeService> _mockRealTimeService;
    private readonly Mock<IUserContextService> _mockUserContextService;
    private readonly Mock<ILogger<RealTimeController>> _mockLogger;
    private readonly RealTimeController _controller;
    private readonly UserInfo _testUser;

    public RealTimeControllerTest()
    {
        _mockRealTimeService = new Mock<IRealTimeService>();
        _mockLogger = new Mock<ILogger<RealTimeController>>();
        _mockUserContextService = new Mock<IUserContextService>();

        _testUser = new UserInfo
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test User",
            WebSocketToken = "test_token"
        };

        _controller = new RealTimeController(
            _mockRealTimeService.Object,
            _mockUserContextService.Object,
            _mockLogger.Object
        );

        _mockUserContextService
            .Setup(x => x.GetCurrentUserAsync())
            .ReturnsAsync(_testUser);
    }

    [Fact]
    public async Task StartRealTimeService_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        _mockRealTimeService
            .Setup(s => s.StartAsync(_testUser))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.StartRealTimeService();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockRealTimeService.Verify(s => s.StartAsync(_testUser), Times.Once);
    }

    [Fact]
    public async Task StartRealTimeService_ThrowsException_WhenUserHasNoToken()
    {
        // Arrange
        var userWithoutToken = new UserInfo
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test User",
            WebSocketToken = null
        };

        _mockUserContextService
            .Setup(x => x.GetCurrentUserAsync())
            .ReturnsAsync(userWithoutToken);

        // Act
        var result = await _controller.StartRealTimeService();

        // Act & Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task SubscribeSymbol_ReturnsOk_WhenSuccessful()
    {
        // Arrange
        string symbol = "005930";

        _mockRealTimeService
            .Setup(s => s.SubscribeSymbolAsync(symbol))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.SubscribeSymbol(symbol);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockRealTimeService.Verify(s => s.SubscribeSymbolAsync(symbol), Times.Once);
    }

    [Fact]
    public async Task SubscribeSymbol_ThrowsException_WhenSymbolIsInvalid()
    {
        // Arrange
        string invalidSymbol = "12345"; // 5자리

        // Act
        var result = await _controller.SubscribeSymbol(invalidSymbol);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public void GetSubscriptions_ReturnsOk_WithSymbolsList()
    {
        // Arrange
        var subscribedSymbols = new List<string> { "005930", "000660" };

        _mockRealTimeService
            .Setup(s => s.GetSubscribedSymbols())
            .Returns(subscribedSymbols);

        // Act
        var result = _controller.GetSubscriptions();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockRealTimeService.Verify(s => s.GetSubscribedSymbols(), Times.Once);
    }
}