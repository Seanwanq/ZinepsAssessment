using Microsoft.Extensions.Logging;
using Zineps.Core.Interfaces;

namespace Zineps.Core.Services;

/// <summary>
/// SpeedShip carrier integration service
/// </summary>
public class SpeedShipIntegrationService : ICarrierIntegration
{
    private readonly ILogger<SpeedShipIntegrationService> _logger;
    private readonly SpeedShipConfiguration _configuration;

    // API Endpoints (per requirements)
    private const string AuthEndpoint = "/auth/token";
    private const string ShipmentsEndpoint = "/shipments";
    private const string LabelsEndpoint = "/labels/{0}"; // {0} = trackingNumber

    private string? _authToken;
    private DateTime? _tokenExpiry;
    private int _authRetryCount = 0;
    private const int MaxAuthRetries = 3;

    public SpeedShipIntegrationService(
        ILogger<SpeedShipIntegrationService> logger,
        SpeedShipConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// Authenticates with SpeedShip API with retry logic
    public async Task<AuthenticationResult> AuthenticateAsync()
    {
        try
        {
            _logger.LogInformation("Authenticatin with SpeedShip API (Attempt {Attempt})", _authRetryCount + 1);

            // Check if we already have a valid token
            if (!string.IsNullOrEmpty(_authToken) && _tokenExpiry.HasValue && _tokenExpiry > DateTime.UtcNow)
            {
                _logger.LogInformation("Using existing valid auto token");
                return new AuthenticationResult
                {
                    IsSuccess = true,
                    Token = _authToken,
                    ExpiresAt = _tokenExpiry.Value
                };
            }

            // Test mode: simulating authentication
            if (_configuration.TestMode)
            {
                return SimulateAuthentication();
            }

            // Real authentication logic would go here
            await Task.Delay(100); // Simulate network delay
            // Example: var response = await _httpClient.PostAsync(AuthEndpoint, content);

            // Simulate authentication logic
            if (string.IsNullOrEmpty(_configuration.ApiKey) || string.IsNullOrEmpty(_configuration.ApiSecret))
            {
                throw new InvalidOperationException("Api credentials are not configured.");
            }

            _authToken = $"speedship_token_{Guid.NewGuid():N}";
            _tokenExpiry = DateTime.UtcNow.AddHours(2);

            _logger.LogInformation("Successfully authenticated with SpeedShip API");
            _authRetryCount = 0;

            return new AuthenticationResult
            {
                IsSuccess = true,
                Token = _authToken,
                ExpiresAt = _tokenExpiry.Value
            };

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed");

            if (_authRetryCount < MaxAuthRetries)
            {
                _authRetryCount += 1;
                _logger.LogWarning("Retrying authentication ({Retry}/{Max})", _authRetryCount, MaxAuthRetries);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, _authRetryCount)));
                return await AuthenticateAsync();
            }

            _logger.LogError("Authentication failed after {MaxRetries} attempts", MaxAuthRetries);
            _authRetryCount = 0;

            return new AuthenticationResult
            {
                IsSuccess = false,
                ErrorMessage = $"Authentication failed after {MaxAuthRetries} attempts: {ex.Message}"
            };
        }
    }

    /// Creates a shipment with SpeedShip
    public async Task<ShipmentResult> CreateShipmentAsync(ShipmentRequest request)
    {
        try
        {
            _logger.LogInformation("Creating shipment for {Recipient}", request.RecipientName);

            // Ensure we're authenticated
            var authResult = await AuthenticateAsync();
            if (!authResult.IsSuccess)
            {
                return new ShipmentResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Authentication required before creating shipment"
                };
            }

            // Validate request
            if (string.IsNullOrEmpty(request.RecipientName) || request.Weight <= 0)
            {
                return new ShipmentResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Invalid shipment request: missing required fields"
                };
            }

            // Test mode: simulate shipment creation
            if (_configuration.TestMode)
            {
                return SimulateShipmentCreation(request);
            }

            // Real mode: would call actual API
            await Task.Delay(150); // Simulate network delay
            // Example: var response = await _httpClient.PostAsync(ShipmentsEndpoint, content);

            // Calculate amount based on weight and zone
            var amount = CalculateShipmentCost(request.Weight, request.Zone);

            var trackingNumber = $"SS{DateTime.UtcNow:yyyyMMdd}{Random.Shared.Next(1000, 9999)}";

            _logger.LogInformation("Shipment created successfully with tracking number {TrackingNumber}", trackingNumber);

            return new ShipmentResult
            {
                IsSuccess = true,
                TrackingNumber = trackingNumber,
                Amount = amount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create shipment");
            return new ShipmentResult
            {
                IsSuccess = false,
                ErrorMessage = $"Shipment creation failed: {ex.Message}"
            };
        }
    }

    /// Fetches a shipping label for a given tracking number
    public async Task<LabelResult> GetLabelAsync(string trackingNumber)
    {
        try
        {
            _logger.LogInformation("Fetching label for tracking number {TrackingNumber}", trackingNumber);

            // Ensure we're authenticated
            var authResult = await AuthenticateAsync();
            if (!authResult.IsSuccess)
            {
                return new LabelResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Authentication required before fetching label"
                };
            }

            if (string.IsNullOrEmpty(trackingNumber))
            {
                return new LabelResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Tracking number is required"
                };
            }

            // Test mode: simulate label generation
            if (_configuration.TestMode)
            {
                return SimulateLabelGeneration(trackingNumber);
            }

            // Real mode: would call actual API
            await Task.Delay(200); // Simulate network delay
            // Example: var response = await _httpClient.GetAsync(string.Format(LabelsEndpoint, trackingNumber));

            // Generate mock base64 PDF (in real scenario, this would be from the API)
            var mockPdfContent = GenerateMockLabelPdf(trackingNumber);

            _logger.LogInformation("Label fetched successfully for {TrackingNumber}", trackingNumber);

            return new LabelResult
            {
                IsSuccess = true,
                LabelData = mockPdfContent,
                Format = "PDF"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch label for {TrackingNumber}", trackingNumber);
            return new LabelResult
            {
                IsSuccess = false,
                ErrorMessage = $"Label fetch failed: {ex.Message}"
            };
        }
    }

    #region Test Mode Simulations

    private AuthenticationResult SimulateAuthentication()
    {
        _logger.LogInformation("TEST MODE: Simulating authentication");
        _authToken = $"test_token_{Guid.NewGuid():N}";

        _tokenExpiry = DateTime.UtcNow.AddHours(2);

        return new AuthenticationResult
        {
            IsSuccess = true,
            Token = _authToken,
            ExpiresAt = _tokenExpiry.Value
        };
    }

    private ShipmentResult SimulateShipmentCreation(ShipmentRequest request)
    {
        _logger.LogInformation("TEST MODE: Simulating shipment creation");
        var amount = CalculateShipmentCost(request.Weight, request.Zone);
        var trackingNumber = $"TEST{DateTime.UtcNow:yyyyMMdd}{Random.Shared.Next(1000, 9999)}";

        return new ShipmentResult
        {
            IsSuccess = true,
            TrackingNumber = trackingNumber,
            Amount = amount
        };
    }

    private LabelResult SimulateLabelGeneration(string trackingNumber)
    {
        _logger.LogInformation("TEST MODE: Simulating label generation");
        var mockPdfContent = GenerateMockLabelPdf(trackingNumber);

        return new LabelResult
        {
            IsSuccess = true,
            LabelData = mockPdfContent,
            Format = "PDF"
        };
    }
    #endregion

    #region Helper Methods

    private decimal CalculateShipmentCost(double weight, string zone)
    {
        // Simple pricing logic
        decimal baseCost = 3.50m;
        decimal weightCost = (decimal)weight * 0.75m;
        decimal zoneCost = zone switch
        {
            "NL" => 0.0m,
            "EU" => 2.50m,
            _ => 5.00m
        };

        return baseCost + weightCost + zoneCost;
    }

    private string GenerateMockLabelPdf(string trackingNumber)
    {
        // Generate a simple mock PDF content as base64
        // In real scenario, this would be the actual PDF from the carrier API
        var mockContent = $"SPEEDSHIP LABEL\nTracking: {trackingNumber}\nDate: {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(mockContent);
        return Convert.ToBase64String(bytes);
    }

    #endregion
}

/// Configuration for SpeedShip integration
public class SpeedShipConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.speedship.com";
    public bool TestMode { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 30;
}