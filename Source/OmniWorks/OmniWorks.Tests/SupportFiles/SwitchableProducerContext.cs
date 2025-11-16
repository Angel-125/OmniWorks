using System;
using OmniWorks.Core;

/// <summary>
/// A producer whose output can be toggled ON or OFF at runtime.
/// 
/// PURPOSE:
///     Lets integration tests simulate producers shutting off mid-simulation,
///     which triggers resource isolation and forces ledger rebuilds.
/// 
/// WHAT IT SIMULATES:
///     - When enabled: produces a fixed rate per second
///     - When disabled: produces zero and SHOULD be treated as absent
/// 
/// WHY IT EXISTS:
///     Many systems (solar panels, reactors, engines) dynamically activate
///     and deactivate. The broker needs to react correctly when a producer
///     disappears from the resource graph.
/// 
/// BEST USE CASES:
///     - Producer drop-out tests
///     - Broker culling behavior
///     - Switching between brokered ↔ unbrokered states
/// </summary>
public sealed class SwitchableProducerContext : IOmniResourceConverterContext
{
    public Guid ConverterId { get; set; }

    public int ResourceId { get; set; }
    public double AmountPerSecond { get; set; }
    public bool IsEnabled { get; set; } = true;

    public ConversionResults LastResults { get; private set; }

    public SwitchableProducerContext(int resourceId, double amountPerSecond)
    {
        ResourceId = resourceId;
        AmountPerSecond = amountPerSecond;
    }

    public void RegisterReports(ConverterReportRegistry registry)
    {
        if (!IsEnabled)
            return;

        registry.ProducerReports.Add(new ProducerReport
        {
            EndpointId = ConverterId,
            ResourceId = ResourceId,
            AmountOfferedPerSec = AmountPerSecond
        });
    }

    public void OnConversionResult(ConversionResults results)
    {
        LastResults = results;
    }
}
