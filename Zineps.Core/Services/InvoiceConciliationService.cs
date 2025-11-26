using Microsoft.Extensions.Logging;
using Zineps.Core.Models;

namespace Zineps.Core.Services;


/// Service for reconciling carrier invoices with customer charges
public class InvoiceReconciliationService
{
    private readonly ILogger<InvoiceReconciliationService> _logger;
    private readonly ReconciliationConfiguration _configuration;

    public InvoiceReconciliationService(
        ILogger<InvoiceReconciliationService> logger,
        ReconciliationConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// Reconciles carrier invoices with customer charges
    public DiscrepancyReport ReconcileInvoices(
        List<CarrierInvoiceLine> carrierInvoices,
        List<CustomerCharge> customerCharges)
    {
        _logger.LogInformation(
            "Starting invoice reconciliation: {CarrierCount} carrier invoices, {CustomerCount} customer charges",
            carrierInvoices.Count,
            customerCharges.Count);

        var report = new DiscrepancyReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalRecordsProcessed = carrierInvoices.Count
        };

        // Create dictionaries for efficient lookup - critical for scaling to 1M+ records
        var customerChargeDict = customerCharges
            .GroupBy(c => c.TrackingNumber)
            .ToDictionary(g => g.Key, g => g.First());

        var processedTrackingNumbers = new HashSet<string>();

        // Process each carrier invoice
        foreach (var carrierInvoice in carrierInvoices)
        {
            processedTrackingNumbers.Add(carrierInvoice.TrackingNumber);

            // Check if we have a matching customer charge
            if (!customerChargeDict.TryGetValue(carrierInvoice.TrackingNumber, out var customerCharge))
            {
                report.UnmatchedCarrierInvoices.Add(carrierInvoice.TrackingNumber);
                _logger.LogWarning("No matching customer charge found for tracking number {TrackingNumber}",
                    carrierInvoice.TrackingNumber);
                continue;
            }

            // Check for discrepancies
            var discrepancies = CheckDiscrepancies(carrierInvoice, customerCharge);
            report.Discrepancies.AddRange(discrepancies);
        }

        // Find unmatched customer charges
        report.UnmatchedCustomerCharges = customerCharges
            .Where(c => !processedTrackingNumbers.Contains(c.TrackingNumber))
            .Select(c => c.TrackingNumber)
            .ToList();

        // Calculate summary statistics
        report.Summary = CalculateSummary(report.Discrepancies);
        report.TotalDiscrepanciesFound = report.Discrepancies.Count;

        _logger.LogInformation(
            "Reconciliation complete: {DiscrepancyCount} discrepancies found, Financial impact: {Impact:C}",
            report.TotalDiscrepanciesFound,
            report.Summary.TotalFinancialImpact);

        return report;
    }

    /// Asynchronous version for processing large datasets
    public async Task<DiscrepancyReport> ReconcileInvoicesAsync(
        IEnumerable<CarrierInvoiceLine> carrierInvoices,
        IEnumerable<CustomerCharge> customerCharges,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var carrierList = carrierInvoices.ToList();
            var customerList = customerCharges.ToList();
            return ReconcileInvoices(carrierList, customerList);
        }, cancellationToken);
    }

    /// Process invoices in batches for better memory management with large datasets
    public async Task<DiscrepancyReport> ReconcileInvoicesInBatchesAsync(
        IAsyncEnumerable<CarrierInvoiceLine> carrierInvoices,
        IAsyncEnumerable<CustomerCharge> customerCharges,
        int batchSize = 10000,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting batch reconciliation with batch size {BatchSize}", batchSize);

        var report = new DiscrepancyReport
        {
            GeneratedAt = DateTime.UtcNow
        };

        // Build customer charge index
        var customerChargeDict = new Dictionary<string, CustomerCharge>();
        await foreach (var charge in customerCharges.WithCancellation(cancellationToken))
        {
            customerChargeDict[charge.TrackingNumber] = charge;
        }

        var processedTrackingNumbers = new HashSet<string>();
        var batchNumber = 0;

        // Process carrier invoices in batches
        var batch = new List<CarrierInvoiceLine>();
        await foreach (var invoice in carrierInvoices.WithCancellation(cancellationToken))
        {
            batch.Add(invoice);
            report.TotalRecordsProcessed++;

            if (batch.Count >= batchSize)
            {
                batchNumber++;
                _logger.LogInformation("Processing batch {BatchNumber} with {Count} records", batchNumber, batch.Count);

                ProcessBatch(batch, customerChargeDict, processedTrackingNumbers, report);
                batch.Clear();
            }
        }

        // Process remaining records
        if (batch.Count > 0)
        {
            batchNumber++;
            _logger.LogInformation("Processing final batch {BatchNumber} with {Count} records", batchNumber, batch.Count);
            ProcessBatch(batch, customerChargeDict, processedTrackingNumbers, report);
        }

        // Find unmatched customer charges
        report.UnmatchedCustomerCharges = customerChargeDict.Keys
            .Where(k => !processedTrackingNumbers.Contains(k))
            .ToList();

        report.Summary = CalculateSummary(report.Discrepancies);
        report.TotalDiscrepanciesFound = report.Discrepancies.Count;

        _logger.LogInformation("Batch reconciliation complete: Processed {BatchCount} batches", batchNumber);

        return report;
    }

    private void ProcessBatch(
        List<CarrierInvoiceLine> batch,
        Dictionary<string, CustomerCharge> customerChargeDict,
        HashSet<string> processedTrackingNumbers,
        DiscrepancyReport report)
    {
        foreach (var invoice in batch)
        {
            processedTrackingNumbers.Add(invoice.TrackingNumber);

            if (!customerChargeDict.TryGetValue(invoice.TrackingNumber, out var customerCharge))
            {
                report.UnmatchedCarrierInvoices.Add(invoice.TrackingNumber);
                continue;
            }

            var discrepancies = CheckDiscrepancies(invoice, customerCharge);
            report.Discrepancies.AddRange(discrepancies);
        }
    }

    private List<Discrepancy> CheckDiscrepancies(
        CarrierInvoiceLine carrierInvoice,
        CustomerCharge customerCharge)
    {
        var discrepancies = new List<Discrepancy>();

        // Check price discrepancy
        var priceDifference = Math.Abs(carrierInvoice.Amount - customerCharge.BilledAmount);
        if (priceDifference > _configuration.PriceToleranceAmount)
        {
            var financialImpact = customerCharge.BilledAmount - carrierInvoice.Amount;
            discrepancies.Add(new Discrepancy
            {
                TrackingNumber = carrierInvoice.TrackingNumber,
                DiscrepancyType = "Price",
                Description = $"Price mismatch: Carrier charged {carrierInvoice.Amount:C}, Customer billed {customerCharge.BilledAmount:C}",
                CarrierAmount = carrierInvoice.Amount,
                CustomerBilledAmount = customerCharge.BilledAmount,
                FinancialImpact = financialImpact,
                Severity = DetermineSeverity(Math.Abs(financialImpact))
            });
        }

        // Check weight discrepancy
        var weightDifference = Math.Abs(carrierInvoice.Weight - customerCharge.DeclaredWeight);
        var weightTolerancePercent = carrierInvoice.Weight * _configuration.WeightTolerancePercent;
        if (weightDifference > weightTolerancePercent)
        {
            discrepancies.Add(new Discrepancy
            {
                TrackingNumber = carrierInvoice.TrackingNumber,
                DiscrepancyType = "Weight",
                Description = $"Weight mismatch: Carrier recorded {carrierInvoice.Weight}kg, Customer declared {customerCharge.DeclaredWeight}kg",
                CarrierWeight = carrierInvoice.Weight,
                CustomerDeclaredWeight = customerCharge.DeclaredWeight,
                FinancialImpact = 0, // Weight alone doesn't determine impact without repricing
                Severity = "Medium"
            });
        }

        // Check zone discrepancy
        if (!string.Equals(carrierInvoice.Zone, customerCharge.Zone, StringComparison.OrdinalIgnoreCase))
        {
            discrepancies.Add(new Discrepancy
            {
                TrackingNumber = carrierInvoice.TrackingNumber,
                DiscrepancyType = "Zone",
                Description = $"Zone mismatch: Carrier zone '{carrierInvoice.Zone}', Customer zone '{customerCharge.Zone}'",
                CarrierZone = carrierInvoice.Zone,
                CustomerZone = customerCharge.Zone,
                FinancialImpact = 0, // Zone alone doesn't determine impact without repricing
                Severity = "Medium"
            });
        }

        // Check fuel surcharge discrepancy (extensibility feature)
        if (carrierInvoice.FuelSurcharge.HasValue && customerCharge.AppliedFuelSurcharge.HasValue)
        {
            var fuelDifference = Math.Abs(carrierInvoice.FuelSurcharge.Value - customerCharge.AppliedFuelSurcharge.Value);
            if (fuelDifference > _configuration.PriceToleranceAmount)
            {
                var fuelImpact = customerCharge.AppliedFuelSurcharge.Value - carrierInvoice.FuelSurcharge.Value;
                discrepancies.Add(new Discrepancy
                {
                    TrackingNumber = carrierInvoice.TrackingNumber,
                    DiscrepancyType = "FuelSurcharge",
                    Description = $"Fuel surcharge mismatch: Carrier {carrierInvoice.FuelSurcharge:C}, Customer {customerCharge.AppliedFuelSurcharge:C}",
                    CarrierFuelSurcharge = carrierInvoice.FuelSurcharge,
                    CustomerFuelSurcharge = customerCharge.AppliedFuelSurcharge,
                    FinancialImpact = fuelImpact,
                    Severity = DetermineSeverity(Math.Abs(fuelImpact))
                });
            }
        }

        return discrepancies;
    }

    private DiscrepancySummary CalculateSummary(List<Discrepancy> discrepancies)
    {
        var summary = new DiscrepancySummary
        {
            PriceDiscrepancies = discrepancies.Count(d => d.DiscrepancyType == "Price"),
            WeightDiscrepancies = discrepancies.Count(d => d.DiscrepancyType == "Weight"),
            ZoneDiscrepancies = discrepancies.Count(d => d.DiscrepancyType == "Zone"),
            FuelSurchargeDiscrepancies = discrepancies.Count(d => d.DiscrepancyType == "FuelSurcharge"),

            TotalFinancialImpact = discrepancies.Sum(d => d.FinancialImpact),
            TotalUndercharged = discrepancies.Where(d => d.FinancialImpact < 0).Sum(d => Math.Abs(d.FinancialImpact)),
            TotalOvercharged = discrepancies.Where(d => d.FinancialImpact > 0).Sum(d => d.FinancialImpact),

            HighSeverityCount = discrepancies.Count(d => d.Severity == "High"),
            MediumSeverityCount = discrepancies.Count(d => d.Severity == "Medium"),
            LowSeverityCount = discrepancies.Count(d => d.Severity == "Low")
        };

        return summary;
    }

    private string DetermineSeverity(decimal amount)
    {
        if (amount >= _configuration.HighSeverityThreshold)
            return "High";
        if (amount >= _configuration.MediumSeverityThreshold)
            return "Medium";
        return "Low";
    }
}

/// Configuration for invoice reconciliation
public class ReconciliationConfiguration
{
    public decimal PriceToleranceAmount { get; set; } = 0.01m; // €0.01 tolerance
    public double WeightTolerancePercent { get; set; } = 0.05; // 5% tolerance
    public decimal HighSeverityThreshold { get; set; } = 10.00m; // €10+
    public decimal MediumSeverityThreshold { get; set; } = 2.00m; // €2+
}