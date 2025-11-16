using System;
using OmniWorks.Core;

/// <summary>
/// A consumer with a finite internal capacity (e.g., a tank full of aardvarks).
/// 
/// PURPOSE:
///     Used to test dynamic behavior across multiple ticks, specifically:
///         • optional consumption
///         • finite storage limits
///         • dropping out of the graph when full
/// 
/// WHAT IT SIMULATES:
///     - Requests some amount per second
///     - Tracks internal fill amount
///     - Stops generating consumer reports when capacity is reached
/// 
/// WHY IT EXISTS:
///     The broker must gracefully handle a converter that:
///         1. participates in the resource network at first
///         2. then becomes isolated (because it no longer consumes)
///     This context makes those transitions testable and deterministic.
/// 
/// BEST USE CASES:
///     - Multi-tick resource fill tests
///     - Optional-only networks
///     - Broker culling & graph refresh validation
/// </summary>
public sealed class FiniteCapacityOptionalConsumerContext : IOmniResourceConverterContext
{
    public Guid ConverterId { get; set; }

    public int ResourceId { get; set; }
    public double MaxFillRatePerSecond { get; set; }
    public double Capacity { get; set; }

    public double CurrentAmount { get; private set; }

    public ConversionResults LastResults { get; private set; }

    public bool IsFull => CurrentAmount >= Capacity;

    public FiniteCapacityOptionalConsumerContext(
        int resourceId,
        double maxFillRatePerSecond,
        double capacity)
    {
        ResourceId = resourceId;
        MaxFillRatePerSecond = maxFillRatePerSecond;
        Capacity = capacity;
    }

    public void RegisterReports(ConverterReportRegistry registry)
    {
        if (IsFull)
            return;

        double remainingCapacity = Capacity - CurrentAmount;
        if (remainingCapacity <= 0)
            return;

        // In this test we use deltaTime = 1.0, so "per second" == "per tick".
        double requestedPerSecond = Math.Min(MaxFillRatePerSecond, remainingCapacity);

        if (requestedPerSecond <= 0)
            return;

        registry.ConsumerReports.Add(new ConsumerReport
        {
            EndpointId = ConverterId,
            ResourceId = ResourceId,
            AmountRequestedPerSec = requestedPerSecond,
            IsOptional = true
        });
    }

    public void OnConversionResult(ConversionResults results)
    {
        LastResults = results;

        if (results.BrokeredConsumerReports.Count > 0)
        {
            var report = results.BrokeredConsumerReports[0];
            CurrentAmount += report.AmountGrantedPerTick;

            if (CurrentAmount > Capacity)
                CurrentAmount = Capacity;
        }
    }
}
