using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Zineps.Core.Services;
using Zineps.Core.Interfaces;

namespace Zineps.Tests.Services;

public class SpeedShipIntegrationServiceTests
{
    private readonly Mock<ILogger<SpeedShipIntegrationService>> _mockLogger;
    private readonly SpeedShipConfiguration _configuration;

    public SpeedShipIntegrationServiceTests()
    {
        _mockLogger = new Mock<ILogger<SpeedShipIntegrationService>>();
        _configuration = new SpeedShipConfiguration
        {
            ApiKey = "test_key",
            ApiSecret = "test_secret",
            TestMode = true // Use test mode for unit tests
        };
    }

    [Fact]
    public async Task AuthenticateAsync_InTestMode_ShouldReturnSuccessWithToken()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);

        // Act
        var result = await service.AuthenticateAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Token.Should().NotBeNullOrEmpty();
        result.Token.Should().StartWith("test_token_");
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task AuthenticateAsync_ShouldReuseValidToken()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);

        // Act
        var result1 = await service.AuthenticateAsync();
        var result2 = await service.AuthenticateAsync();

        // Assert
        result1.Token.Should().Be(result2.Token);
        result1.ExpiresAt.Should().Be(result2.ExpiresAt);
    }

    [Fact]
    public async Task CreateShipmentAsync_WithValidRequest_ShouldReturnSuccessWithTrackingNumber()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);
        var request = new ShipmentRequest
        {
            RecipientName = "John Doe",
            RecipientAddress = "123 Main St",
            RecipientCity = "Amsterdam",
            RecipientPostalCode = "1012AB",
            RecipientCountry = "NL",
            Weight = 2.5,
            Zone = "NL"
        };

        // Act
        var result = await service.CreateShipmentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.TrackingNumber.Should().NotBeNullOrEmpty();
        result.TrackingNumber.Should().StartWith("TEST");
        result.Amount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateShipmentAsync_WithInvalidRequest_ShouldReturnFailure()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);
        var request = new ShipmentRequest
        {
            RecipientName = "", // Invalid: empty name
            Weight = 0 // Invalid: zero weight
        };

        // Act
        var result = await service.CreateShipmentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid shipment request");
    }

    [Fact]
    public async Task CreateShipmentAsync_ShouldCalculateCostBasedOnWeightAndZone()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);
        var nlRequest = new ShipmentRequest
        {
            RecipientName = "Test",
            RecipientAddress = "Test",
            RecipientCity = "Amsterdam",
            RecipientPostalCode = "1012AB",
            RecipientCountry = "NL",
            Weight = 2.0,
            Zone = "NL"
        };
        var euRequest = new ShipmentRequest
        {
            RecipientName = "Test",
            RecipientAddress = "Test",
            RecipientCity = "Berlin",
            RecipientPostalCode = "10115",
            RecipientCountry = "DE",
            Weight = 2.0,
            Zone = "EU"
        };

        // Act
        var nlResult = await service.CreateShipmentAsync(nlRequest);
        var euResult = await service.CreateShipmentAsync(euRequest);

        // Assert
        nlResult.Amount.Should().BeLessThan(euResult.Amount); // NL zone should be cheaper than EU
    }

    [Fact]
    public async Task GetLabelAsync_WithValidTrackingNumber_ShouldReturnLabelData()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);
        var trackingNumber = "TEST20241126001";

        // Act
        var result = await service.GetLabelAsync(trackingNumber);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.LabelData.Should().NotBeNullOrEmpty();
        result.Format.Should().Be("PDF");
    }

    [Fact]
    public async Task GetLabelAsync_WithEmptyTrackingNumber_ShouldReturnFailure()
    {
        // Arrange
        var service = new SpeedShipIntegrationService(_mockLogger.Object, _configuration);

        // Act
        var result = await service.GetLabelAsync(string.Empty);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Tracking number is required");
    }

    [Fact]
    public async Task AuthenticateAsync_WithoutCredentials_ShouldRetryAndFail()
    {
        // Arrange
        var invalidConfig = new SpeedShipConfiguration
        {
            ApiKey = "",
            ApiSecret = "",
            TestMode = false // Disable test mode to test real authentication logic
        };
        var service = new SpeedShipIntegrationService(_mockLogger.Object, invalidConfig);

        // Act
        var result = await service.AuthenticateAsync();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Authentication failed");
    }
}