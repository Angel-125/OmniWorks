using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OmniWorks.Core;

/// <summary>
/// A producer that offers TWO different resources simultaneously.
/// 
/// PURPOSE:
///     Allows integration tests to verify broker behavior when a single
///     converter participates in multiple resource networks with different
///     isolation/broker states.
/// 
/// WHAT IT SIMULATES:
///     - Resource A: typically shared → should be BROKERED
///     - Resource B: typically isolated → should be UNBROKERED
/// 
/// WHY IT EXISTS:
///     Tests need a way to confirm that the ConversionResults glue layer
///     correctly splits producer reports into:
///         • BrokeredProducerReports
///         • UnbrokeredProducerReports
///     …depending on how the broker classifies each resource.
/// 
/// BEST USE CASES:
///     - Mixed brokered + unbrokered producer tests
///     - Ledger-refresh and culling verification
/// </summary>
public sealed class DualResourceProducerContext : IOmniResourceConverterContext
{
    public Guid ConverterId { get; set; }

    public int PrimaryResourceId { get; set; }
    public double PrimaryAmountPerSecond { get; set; }

    public int IsolatedResourceId { get; set; }
    public double IsolatedAmountPerSecond { get; set; }

    public ConversionResults LastResults { get; private set; }

    public DualResourceProducerContext(
        int primaryResourceId,
        double primaryAmountPerSecond,
        int isolatedResourceId,
        double isolatedAmountPerSecond)
    {
        PrimaryResourceId = primaryResourceId;
        PrimaryAmountPerSecond = primaryAmountPerSecond;
        IsolatedResourceId = isolatedResourceId;
        IsolatedAmountPerSecond = isolatedAmountPerSecond;
    }

    public void RegisterReports(ConverterReportRegistry registry)
    {
        // Primary, shared resource (will be brokered)
        registry.ProducerReports.Add(new ProducerReport
        {
            EndpointId = ConverterId,
            ResourceId = PrimaryResourceId,
            AmountOfferedPerSec = PrimaryAmountPerSecond
        });

        // Isolated resource (no consumer; should be culled and marked unbrokered)
        registry.ProducerReports.Add(new ProducerReport
        {
            EndpointId = ConverterId,
            ResourceId = IsolatedResourceId,
            AmountOfferedPerSec = IsolatedAmountPerSecond
        });
    }

    public void OnConversionResult(ConversionResults results)
    {
        LastResults = results;
    }
}
