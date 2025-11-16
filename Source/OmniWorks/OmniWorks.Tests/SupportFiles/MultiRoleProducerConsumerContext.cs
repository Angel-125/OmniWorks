using System;
using OmniWorks.Core;

/// <summary>
/// A converter that BOTH produces AND consumes resources, potentially the same one.
/// 
/// PURPOSE:
///     Exercises the trickiest glue-layer edge case: one converter performing
///     dual roles in the same tick.
/// 
/// WHAT IT SIMULATES:
///     - Produces Resource X
///     - Consumes Resource Y (or X)
/// 
/// WHY IT EXISTS:
///     This context allows tests to explicitly validate:
///         • A single converter does NOT count as two endpoints for a resource.
///           (If it is the only touchpoint → isolated → unbrokered.)
///
///         • When a second converter participates, BOTH the producer and consumer
///           reports must become BROKERED in ConversionResults.
/// 
/// BEST USE CASES:
///     - Isolated multi-role converter tests
///     - Shared-network multi-role tests
///     - Verifying ConversionResults correctness for dual-role converters
/// </summary>
public sealed class MultiRoleProducerConsumerContext : IOmniResourceConverterContext
{
    public Guid ConverterId { get; set; }

    public int ProducedResourceId { get; set; }
    public double ProducedPerSecond { get; set; }

    public int ConsumedResourceId { get; set; }
    public double ConsumedPerSecond { get; set; }
    public bool ConsumedIsOptional { get; set; }

    public ConversionResults LastResults { get; private set; }

    public MultiRoleProducerConsumerContext(
        int producedResourceId,
        double producedPerSecond,
        int consumedResourceId,
        double consumedPerSecond,
        bool consumedIsOptional = false)
    {
        ProducedResourceId = producedResourceId;
        ProducedPerSecond = producedPerSecond;
        ConsumedResourceId = consumedResourceId;
        ConsumedPerSecond = consumedPerSecond;
        ConsumedIsOptional = consumedIsOptional;
    }

    public void RegisterReports(ConverterReportRegistry registry)
    {
        registry.ProducerReports.Add(new ProducerReport
        {
            EndpointId = ConverterId,
            ResourceId = ProducedResourceId,
            AmountOfferedPerSec = ProducedPerSecond
        });

        registry.ConsumerReports.Add(new ConsumerReport
        {
            EndpointId = ConverterId,
            ResourceId = ConsumedResourceId,
            AmountRequestedPerSec = ConsumedPerSecond,
            IsOptional = ConsumedIsOptional
        });
    }

    public void OnConversionResult(ConversionResults results)
    {
        LastResults = results;
    }
}
