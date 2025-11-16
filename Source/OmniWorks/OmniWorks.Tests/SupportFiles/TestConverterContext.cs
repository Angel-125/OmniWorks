using System;
using System.Collections.Generic;
using OmniWorks.Core; // your namespace
using Xunit;

/// <summary>
/// A minimal, single-resource test converter used by most integration tests.
/// 
/// PURPOSE:
///     Provides a simple producer OR consumer that touches exactly one resource,
///     making it ideal for "baseline" broker behavior tests.
/// 
/// WHAT IT SIMULATES:
///     - A basic producer (offers a fixed rate per second), OR
///     - A basic consumer (requests a fixed rate per second)
/// 
/// WHY IT EXISTS:
///     This context avoids any multi-resource or multi-role complexity so that
///     integration tests can verify fundamental broker behavior:
///         * proportional allocation
///         * isolation vs. participation
///         * correct ConversionResults mapping
/// 
/// NOTES:
///     - LastResults captures exactly one ConversionResults object per tick.
///     - Optional consumers can be simulated by toggling isOptional.
/// </summary>
public sealed class TestConverterContext : IOmniResourceConverterContext
{
    // These decide what sort of reports this context will register.
    public Guid ConverterId { get; set; }
    public int ResourceId { get; set; }
    public double AmountPerSecond { get; }
    public bool IsProducer { get; }
    public bool IsOptional { get; }

    // We'll stash the latest results here so tests can assert on them.
    public ConversionResults LastResults { get; private set; }

    public TestConverterContext(int resourceId, double amountPerSecond, bool isProducer, bool isOptional = false)
    {
        ResourceId = resourceId;
        AmountPerSecond = amountPerSecond;
        IsProducer = isProducer;
        IsOptional = isOptional;
    }

    public void RegisterReports(ConverterReportRegistry registry)
    {
        if (IsProducer)
        {
            registry.ProducerReports.Add(new ProducerReport
            {
                EndpointId = ConverterId,
                ResourceId = ResourceId,
                AmountOfferedPerSec = AmountPerSecond
            });
        }
        else
        {
            registry.ConsumerReports.Add(new ConsumerReport
            {
                EndpointId = ConverterId,
                ResourceId = ResourceId,
                AmountRequestedPerSec = AmountPerSecond,
                IsOptional = IsOptional
            });
        }
    }

    public void OnConversionResult(ConversionResults results)
    {
        LastResults = results;
    }
}

