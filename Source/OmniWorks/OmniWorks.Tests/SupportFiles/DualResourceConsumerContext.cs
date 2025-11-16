using System;
using OmniWorks.Core;

/// <summary>
/// A consumer that REQUESTS TWO different resources at once.
/// 
/// PURPOSE:
///     Mirrors DualResourceProducerContext, but for consumers.
///     Lets tests verify correct handling of mixed brokered/unbrokered
///     CONSUMER reports.
/// 
/// WHAT IT SIMULATES:
///     - Primary resource: requested from a matching producer → BROKERED
///     - Isolated resource: requested with no matching producer → UNBROKERED
/// 
/// WHY IT EXISTS:
///     The ConversionResults class maintains separate lists for
///     BrokeredConsumerReports and UnbrokeredConsumerReports. This context
///     provides the cleanest way to validate that the broker and glue layer
///     route the right consumer reports into the right lists.
/// 
/// BEST USE CASES:
///     - Brokered + unbrokered consumer scenarios
///     - Tests validating resource culling behavior
/// </summary>
public sealed class DualResourceConsumerContext : IOmniResourceConverterContext
{
    public Guid ConverterId { get; set; }

    public int PrimaryResourceId { get; set; }
    public double PrimaryAmountPerSecond { get; set; }

    public int IsolatedResourceId { get; set; }
    public double IsolatedAmountPerSecond { get; set; }

    public bool PrimaryIsOptional { get; set; }
    public bool IsolatedIsOptional { get; set; }

    public ConversionResults LastResults { get; private set; }

    public DualResourceConsumerContext(
        int primaryResourceId,
        double primaryAmountPerSecond,
        int isolatedResourceId,
        double isolatedAmountPerSecond,
        bool primaryIsOptional = false,
        bool isolatedIsOptional = false)
    {
        PrimaryResourceId = primaryResourceId;
        PrimaryAmountPerSecond = primaryAmountPerSecond;
        IsolatedResourceId = isolatedResourceId;
        IsolatedAmountPerSecond = isolatedAmountPerSecond;
        PrimaryIsOptional = primaryIsOptional;
        IsolatedIsOptional = isolatedIsOptional;
    }

    public void RegisterReports(ConverterReportRegistry registry)
    {
        // Primary, shared resource (will be brokered)
        registry.ConsumerReports.Add(new ConsumerReport
        {
            EndpointId = ConverterId,
            ResourceId = PrimaryResourceId,
            AmountRequestedPerSec = PrimaryAmountPerSecond,
            IsOptional = PrimaryIsOptional
        });

        // Isolated resource (no producer; should be culled and marked unbrokered)
        registry.ConsumerReports.Add(new ConsumerReport
        {
            EndpointId = ConverterId,
            ResourceId = IsolatedResourceId,
            AmountRequestedPerSec = IsolatedAmountPerSecond,
            IsOptional = IsolatedIsOptional
        });
    }

    public void OnConversionResult(ConversionResults results)
    {
        LastResults = results;
    }
}
