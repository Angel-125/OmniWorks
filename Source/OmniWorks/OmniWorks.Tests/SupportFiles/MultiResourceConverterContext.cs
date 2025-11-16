using System;
using System.Collections.Generic;
using System.Diagnostics;
using OmniWorks.Core;
using Xunit;

namespace OmniWorks.Tests
{
    /// <summary>
    /// Test-only converter context that can register multiple consumer and producer reports
    /// to simulate converters with arbitrary fan-in (inputs) and fan-out (outputs).
    ///
    /// It does NOT depend on any external config files or game code; it simply exposes a way
    /// to specify:
    ///     • A list of consumed resource IDs and per-second amounts.
    ///     • A list of produced resource IDs and per-second amounts.
    ///
    /// LIFECYCLE:
    ///     • The OmniResourceConverter calls this context's RegisterReports whenever the
    ///       OmniResourceBroker refreshes its ledger (for example, when converters are
    ///       registered or unregistered, or when the broker is explicitly marked as
    ///       needing a refresh).
    ///     • In steady state, the broker does NOT call RegisterReports every simulation tick;
    ///       it reuses the cached reports in its internal ledger and only rebuilds them
    ///       when needsRefresh is true.
    ///     • OnConversionResult is invoked each tick for converters that are actually
    ///       participating in brokered resource flows, allowing tests to inspect the
    ///       computed ConversionResults.
    /// </summary>
    internal sealed class MultiResourceConverterContext : IOmniResourceConverterContext
    {
        public Guid ConverterId { get; set; }

        private readonly int[] inputResourceIds;
        private readonly double inputAmountPerSecond;
        private readonly int[] outputResourceIds;
        private readonly double outputAmountPerSecond;

        public ConversionResults LastResults { get; private set; }

        public MultiResourceConverterContext(
            int[] inputResourceIds,
            double inputAmountPerSecond,
            int[] outputResourceIds,
            double outputAmountPerSecond)
        {
            this.inputResourceIds = inputResourceIds ?? Array.Empty<int>();
            this.inputAmountPerSecond = inputAmountPerSecond;
            this.outputResourceIds = outputResourceIds ?? Array.Empty<int>();
            this.outputAmountPerSecond = outputAmountPerSecond;
        }

        public void RegisterReports(ConverterReportRegistry registry)
        {
            // Required consumers
            foreach (int resourceId in inputResourceIds)
            {
                registry.ConsumerReports.Add(new ConsumerReport
                {
                    EndpointId = ConverterId,
                    ResourceId = resourceId,
                    AmountRequestedPerSec = inputAmountPerSecond,
                    IsOptional = false
                });
            }

            // Producers
            foreach (int resourceId in outputResourceIds)
            {
                registry.ProducerReports.Add(new ProducerReport
                {
                    EndpointId = ConverterId,
                    ResourceId = resourceId,
                    AmountOfferedPerSec = outputAmountPerSecond
                });
            }
        }

        public void OnConversionResult(ConversionResults results)
        {
            LastResults = results;
        }
    }
}
