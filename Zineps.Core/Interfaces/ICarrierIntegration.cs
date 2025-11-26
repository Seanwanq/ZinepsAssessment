namespace Zineps.Core.Interfaces;

/// Interface for carrier integration services
public interface ICarrierIntegration
{
    /// Authenticates with the carrier API
    Task<AuthenticationResult> AuthenticateAsync();

    /// Creates a shipment with the carrier
    Task<ShipmentResult> CreateShipmentAsync(ShipmentRequest request);

    /// Fetches a shipping label for a tracking number
    Task<LabelResult> GetLabelAsync(string trackingNumber);
}

public class AuthenticationResult
{
    public bool IsSuccess { get; set; }
    public string? Token { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ShipmentRequest
{
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public string RecipientCity { get; set; } = string.Empty;
    public string RecipientPostalCode { get; set; } = string.Empty;
    public string RecipientCountry { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string Zone { get; set; } = string.Empty;
}

public class ShipmentResult
{
    public bool IsSuccess { get; set; }
    public string? TrackingNumber { get; set; }
    public decimal Amount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LabelResult
{
    public bool IsSuccess { get; set; }
    public string? LabelData { get; set; } // Base64 encoded PDF
    public string? Format { get; set; } // PDF, PNG, etc.
    public string? ErrorMessage { get; set; }
}