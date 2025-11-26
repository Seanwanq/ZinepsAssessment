using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Zineps.Core.Services;
using Zineps.Core.Models;

namespace Zineps.Tests.Services;

public class InvoiceReconciliationServiceTests
{
    private readonly Mock<ILogger<InvoiceReconciliationService>> _mockLogger;
    private readonly ReconciliationConfiguration _configuration;

    public InvoiceReconciliationServiceTests()
    {
        _mockLogger = new Mock<ILogger<InvoiceReconciliationService>>();
        _configuration = new ReconciliationConfiguration
        {
            PriceToleranceAmount = 0.01m,
            WeightTolerancePercent = 0.05,
            HighSeverityThreshold = 10.00m,
            MediumSeverityThreshold = 2.00m
        };
    }

    [Fact]
    public void ReconcileInvoices_WithMatchingRecords_ShouldReturnNoDiscrepancies()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine
            {
                TrackingNumber = "123ABC",
                Amount = 5.99m,
                Weight = 2.0,
                Zone = "NL",
                CarrierName = "SpeedShip",
                InvoiceDate = DateTime.UtcNow
            }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge
            {
                TrackingNumber = "123ABC",
                BilledAmount = 5.99m,
                DeclaredWeight = 2.0,
                Zone = "NL",
                CustomerId = "CUST001",
                ChargeDate = DateTime.UtcNow
            }
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.TotalRecordsProcessed.Should().Be(1);
        report.TotalDiscrepanciesFound.Should().Be(0);
        report.UnmatchedCarrierInvoices.Should().BeEmpty();
        report.UnmatchedCustomerCharges.Should().BeEmpty();
    }

    [Fact]
    public void ReconcileInvoices_WithPriceDiscrepancy_ShouldDetectIt()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine
            {
                TrackingNumber = "123ABC",
                Amount = 5.99m,
                Weight = 2.0,
                Zone = "NL",
                CarrierName = "SpeedShip",
                InvoiceDate = DateTime.UtcNow
            }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge
            {
                TrackingNumber = "123ABC",
                BilledAmount = 6.99m, // Different price
                DeclaredWeight = 2.0,
                Zone = "NL",
                CustomerId = "CUST001",
                ChargeDate = DateTime.UtcNow
            }
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.TotalDiscrepanciesFound.Should().Be(1);
        report.Discrepancies[0].DiscrepancyType.Should().Be("Price");
        report.Discrepancies[0].CarrierAmount.Should().Be(5.99m);
        report.Discrepancies[0].CustomerBilledAmount.Should().Be(6.99m);
        report.Discrepancies[0].FinancialImpact.Should().Be(1.00m); // Overcharged by €1
        report.Summary.PriceDiscrepancies.Should().Be(1);
    }

    [Fact]
    public void ReconcileInvoices_WithWeightDiscrepancy_ShouldDetectIt()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new() {
                TrackingNumber = "123ABC",
                Amount = 5.99m,
                Weight = 2.0,
                Zone = "NL",
                CarrierName = "SpeedShip",
                InvoiceDate = DateTime.UtcNow
            }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new() {
                TrackingNumber = "123ABC",
                BilledAmount = 5.99m,
                DeclaredWeight = 1.5, // Different weight (> 5% difference)
                Zone = "NL",
                CustomerId = "CUST001",
                ChargeDate = DateTime.UtcNow
            }
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.TotalDiscrepanciesFound.Should().Be(1);
        report.Discrepancies[0].DiscrepancyType.Should().Be("Weight");
        report.Discrepancies[0].CarrierWeight.Should().Be(2.0);
        report.Discrepancies[0].CustomerDeclaredWeight.Should().Be(1.5);
        report.Summary.WeightDiscrepancies.Should().Be(1);
    }

    [Fact]
    public void ReconcileInvoices_WithZoneDiscrepancy_ShouldDetectIt()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine
            {
                TrackingNumber = "123ABC",
                Amount = 5.99m,
                Weight = 2.0,
                Zone = "NL",
                CarrierName = "SpeedShip",
                InvoiceDate = DateTime.UtcNow
            }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge
            {
                TrackingNumber = "123ABC",
                BilledAmount = 5.99m,
                DeclaredWeight = 2.0,
                Zone = "EU", // Different zone
                CustomerId = "CUST001",
                ChargeDate = DateTime.UtcNow
            }
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.TotalDiscrepanciesFound.Should().Be(1);
        report.Discrepancies[0].DiscrepancyType.Should().Be("Zone");
        report.Discrepancies[0].CarrierZone.Should().Be("NL");
        report.Discrepancies[0].CustomerZone.Should().Be("EU");
        report.Summary.ZoneDiscrepancies.Should().Be(1);
    }

    [Fact]
    public void ReconcileInvoices_WithMultipleDiscrepancies_ShouldDetectAll()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine
            {
                TrackingNumber = "123ABC",
                Amount = 5.99m,
                Weight = 2.0,
                Zone = "NL",
                CarrierName = "SpeedShip",
                InvoiceDate = DateTime.UtcNow
            }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge
            {
                TrackingNumber = "123ABC",
                BilledAmount = 6.99m, // Price difference
                DeclaredWeight = 1.5, // Weight difference
                Zone = "EU", // Zone difference
                CustomerId = "CUST001",
                ChargeDate = DateTime.UtcNow
            }
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.TotalDiscrepanciesFound.Should().Be(3); // Price, Weight, and Zone
        report.Summary.PriceDiscrepancies.Should().Be(1);
        report.Summary.WeightDiscrepancies.Should().Be(1);
        report.Summary.ZoneDiscrepancies.Should().Be(1);
    }

    [Fact]
    public void ReconcileInvoices_WithUnmatchedCarrierInvoice_ShouldReportIt()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine
            {
                TrackingNumber = "123ABC",
                Amount = 5.99m,
                Weight = 2.0,
                Zone = "NL",
                CarrierName = "SpeedShip",
                InvoiceDate = DateTime.UtcNow
            }
        };
        var customerCharges = new List<CustomerCharge>(); // No matching charge

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.UnmatchedCarrierInvoices.Should().Contain("123ABC");
        report.TotalDiscrepanciesFound.Should().Be(0);
    }

    [Fact]
    public void ReconcileInvoices_WithUnmatchedCustomerCharge_ShouldReportIt()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>(); // No matching invoice
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge
            {
                TrackingNumber = "456DEF",
                BilledAmount = 7.99m,
                DeclaredWeight = 3.0,
                Zone = "EU",
                CustomerId = "CUST002",
                ChargeDate = DateTime.UtcNow
            }
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Should().NotBeNull();
        report.UnmatchedCustomerCharges.Should().Contain("456DEF");
    }

    [Fact]
    public void ReconcileInvoices_ShouldCalculateFinancialImpactCorrectly()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine { TrackingNumber = "001", Amount = 5.00m, Weight = 1.0, Zone = "NL", InvoiceDate = DateTime.UtcNow },
            new CarrierInvoiceLine { TrackingNumber = "002", Amount = 10.00m, Weight = 2.0, Zone = "EU", InvoiceDate = DateTime.UtcNow }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge { TrackingNumber = "001", BilledAmount = 6.00m, DeclaredWeight = 1.0, Zone = "NL", ChargeDate = DateTime.UtcNow }, // Overcharged €1
            new CustomerCharge { TrackingNumber = "002", BilledAmount = 9.00m, DeclaredWeight = 2.0, Zone = "EU", ChargeDate = DateTime.UtcNow } // Undercharged €1
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Summary.TotalOvercharged.Should().Be(1.00m);
        report.Summary.TotalUndercharged.Should().Be(1.00m);
        report.Summary.TotalFinancialImpact.Should().Be(0.00m); // Net zero
    }

    [Fact]
    public void ReconcileInvoices_ShouldClassifySeverityCorrectly()
    {
        // Arrange
        var service = new InvoiceReconciliationService(_mockLogger.Object, _configuration);
        var carrierInvoices = new List<CarrierInvoiceLine>
        {
            new CarrierInvoiceLine { TrackingNumber = "001", Amount = 5.00m, Weight = 1.0, Zone = "NL", InvoiceDate = DateTime.UtcNow },
            new CarrierInvoiceLine { TrackingNumber = "002", Amount = 10.00m, Weight = 2.0, Zone = "EU", InvoiceDate = DateTime.UtcNow },
            new CarrierInvoiceLine { TrackingNumber = "003", Amount = 20.00m, Weight = 3.0, Zone = "INT", InvoiceDate = DateTime.UtcNow }
        };
        var customerCharges = new List<CustomerCharge>
        {
            new CustomerCharge { TrackingNumber = "001", BilledAmount = 5.50m, DeclaredWeight = 1.0, Zone = "NL", ChargeDate = DateTime.UtcNow }, // €0.50 - Low
            new CustomerCharge { TrackingNumber = "002", BilledAmount = 13.00m, DeclaredWeight = 2.0, Zone = "EU", ChargeDate = DateTime.UtcNow }, // €3.00 - Medium
            new CustomerCharge { TrackingNumber = "003", BilledAmount = 35.00m, DeclaredWeight = 3.0, Zone = "INT", ChargeDate = DateTime.UtcNow } // €15.00 - High
        };

        // Act
        var report = service.ReconcileInvoices(carrierInvoices, customerCharges);

        // Assert
        report.Summary.LowSeverityCount.Should().Be(1);
        report.Summary.MediumSeverityCount.Should().Be(1);
        report.Summary.HighSeverityCount.Should().Be(1);
    }
}