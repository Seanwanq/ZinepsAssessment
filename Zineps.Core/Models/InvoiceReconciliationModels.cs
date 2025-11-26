namespace Zineps.Core.Models;

/// Represents an invoice line from a carrier
public class CarrierInvoiceLine
{
    public string TrackingNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Weight { get; set; }
    public string Zone { get; set; } = string.Empty;
    public decimal? FuelSurcharge { get; set; } // Extensibility: future field
    public DateTime InvoiceDate { get; set; }
    public string CarrierName { get; set; } = string.Empty;
}

/// Represents a charge to a customer
public class CustomerCharge
{
    public string TrackingNumber { get; set; } = string.Empty;
    public decimal BilledAmount { get; set; }
    public double DeclaredWeight { get; set; }
    public string Zone { get; set; } = string.Empty;
    public decimal? AppliedFuelSurcharge { get; set; } // Extensibility: future field
    public DateTime ChargeDate { get; set; }
    public string CustomerId { get; set; } = string.Empty;
}

/// Represents a discrepancy between carrier invoice and customer charge
public class Discrepancy
{
    public string TrackingNumber { get; set; } = string.Empty;
    public string DiscrepancyType { get; set; } = string.Empty; // Price, Weight, Zone, FuelSurcharge
    public string Description { get; set; } = string.Empty;

    // Carrier values
    public decimal? CarrierAmount { get; set; }
    public double? CarrierWeight { get; set; }
    public string? CarrierZone { get; set; }
    public decimal? CarrierFuelSurcharge { get; set; }

    // Customer values
    public decimal? CustomerBilledAmount { get; set; }
    public double? CustomerDeclaredWeight { get; set; }
    public string? CustomerZone { get; set; }
    public decimal? CustomerFuelSurcharge { get; set; }

    // Financial impact
    public decimal FinancialImpact { get; set; } // Positive = undercharged, Negative = overcharged
    public string Severity { get; set; } = "Low"; // Low, Medium, High
}

/// Comprehensive report of discrepancies
public class DiscrepancyReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalRecordsProcessed { get; set; }
    public int TotalDiscrepanciesFound { get; set; }
    public List<Discrepancy> Discrepancies { get; set; } = new();

    // Summary statistics
    public DiscrepancySummary Summary { get; set; } = new();

    // Unmatched records
    public List<string> UnmatchedCarrierInvoices { get; set; } = new();
    public List<string> UnmatchedCustomerCharges { get; set; } = new();
}

/// Summary statistics for the discrepancy report
public class DiscrepancySummary
{
    public int PriceDiscrepancies { get; set; }
    public int WeightDiscrepancies { get; set; }
    public int ZoneDiscrepancies { get; set; }
    public int FuelSurchargeDiscrepancies { get; set; }

    public decimal TotalFinancialImpact { get; set; }
    public decimal TotalUndercharged { get; set; }
    public decimal TotalOvercharged { get; set; }

    public int HighSeverityCount { get; set; }
    public int MediumSeverityCount { get; set; }
    public int LowSeverityCount { get; set; }
}