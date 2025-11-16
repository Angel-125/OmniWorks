using System;
using System.Collections.Generic;
using System.Diagnostics;
using OmniWorks.Core;
using Xunit;
using Xunit.Abstractions;

namespace OmniWorks.Tests
{
    public sealed class OmniResourceBrokerPerformanceTests
    {
        private readonly ITestOutputHelper testOutput;

        public OmniResourceBrokerPerformanceTests(ITestOutputHelper testOutput)
        {
            this.testOutput = testOutput;
        }

        /// <summary>
        /// Performance Test 1 – Baseline broker tick cost with synthetic "average" converters.
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when it is loaded
        ///     with a synthetic set of converters that statistically approximate the converters
        ///     observed in production, but without any dependency on KSP or external config files.
        ///
        ///     Synthetic workload:
        ///         • Total converters: 69
        ///         • Approx. 2 inputs and 1 output per converter
        ///         • Average input throughput per converter:  ~17.27 units/sec
        ///         • Average output throughput per converter: ~23.22 units/sec
        ///
        /// CURRENT OBSERVED PERFORMANCE (on this machine):
        ///     • Average ≈ 0.0045 ms/tick
        ///
        /// PERFORMANCE BUDGET:
        ///     • maxAverageMillisecondsPerTick = 0.05 ms
        ///       (≈ 10x the observed average, leaving generous headroom for slower
        ///        hardware and minor future changes while still catching major regressions.)
        /// </summary>
        [Fact]
        public void Broker_Performance_SyntheticAverageConverters_IsUnderBudget()
        {
            // --- Arrange ---
            const int converterCount = 69;

            // From the observed statistics:
            const double averageInputThroughputPerConverter = 17.27; // units/sec (approx)
            const double averageOutputThroughputPerConverter = 23.22; // units/sec (approx)

            // We approximate 2.39 inputs as 2 inputs, and 1.41 outputs as 1 output.
            const int inputCountPerConverter = 2;
            const int outputCountPerConverter = 1;

            // Split totals evenly across inputs/outputs for this synthetic test.
            double inputAmountPerSecond = averageInputThroughputPerConverter / inputCountPerConverter;
            double outputAmountPerSecond = averageOutputThroughputPerConverter / outputCountPerConverter;

            var broker = new OmniResourceBroker();

            // We'll use a small, fixed set of resource IDs. The actual IDs don't matter for performance;
            // we just need some shared IDs so the broker's ledger has something to balance.
            int[] inputResourceIds = { 1, 2 };   // two input resources
            int[] outputResourceIds = { 100 };   // one output resource

            var contexts = new List<MultiResourceConverterContext>(converterCount);

            for (int converterIndex = 0; converterIndex < converterCount; converterIndex++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: inputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: outputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // Warmup: allow JIT and internal broker structures to stabilize.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_SyntheticAverageConverters_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.05; // 10x current observed ≈0.0045 ms

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 2 – Reference Base (5 factories, 4 habs)
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick for a
        ///     "reference design" base, using synthetic converters that
        ///     statistically approximate the real KSP converters but do NOT depend
        ///     on any game code or configuration files.
        ///
        ///     Reference base:
        ///         • 5 Factory parts, each with 6 converters  → 30 converters
        ///         • 4 Hab parts, each with 2 converters      →  8 converters
        ///           --------------------------------------------------------
        ///           Total converters                          → 38 converters
        ///
        ///     Each converter approximates the average pattern:
        ///         • ~2 inputs, 1 output
        ///         • Average input throughput per converter:  ~17.27 units/sec
        ///         • Average output throughput per converter: ~23.22 units/sec
        ///
        /// CURRENT OBSERVED PERFORMANCE (on this machine):
        ///     • Average ≈ 0.0026 ms/tick
        ///
        /// PERFORMANCE BUDGET:
        ///     • maxAverageMillisecondsPerTick = 0.03 ms
        ///       (≈ 10–12x the observed average; should remain safe across hardware,
        ///        but will still flag obvious regressions.)
        /// </summary>
        [Fact]
        public void Broker_Performance_ReferenceBase_IsUnderBudget()
        {
            // --- Arrange ---
            const int factoryCount = 5;
            const int convertersPerFactory = 6;
            const int habCount = 4;
            const int convertersPerHab = 2;

            const int factoryConverterCount = factoryCount * convertersPerFactory; // 30
            const int habConverterCount = habCount * convertersPerHab;             //  8
            const int totalConverterCount = factoryConverterCount + habConverterCount; // 38

            // From the observed statistics:
            const double averageInputThroughputPerConverter = 17.27;  // units/sec (approx)
            const double averageOutputThroughputPerConverter = 23.22; // units/sec (approx)

            const int inputCountPerConverter = 2;
            const int outputCountPerConverter = 1;

            double inputAmountPerSecond = averageInputThroughputPerConverter / inputCountPerConverter;
            double outputAmountPerSecond = averageOutputThroughputPerConverter / outputCountPerConverter;

            var broker = new OmniResourceBroker();
            var contexts = new List<MultiResourceConverterContext>(totalConverterCount);

            // "Factory" converters: 2 inputs (1,2), 1 output (100)
            int[] factoryInputResourceIds = { 1, 2 };
            int[] factoryOutputResourceIds = { 100 };

            for (int index = 0; index < factoryConverterCount; index++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: factoryInputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: factoryOutputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // "Hab" converters: 2 inputs (2,3), 1 output (101)
            int[] habInputResourceIds = { 2, 3 };
            int[] habOutputResourceIds = { 101 };

            for (int index = 0; index < habConverterCount; index++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: habInputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: habOutputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // Warmup ticks to stabilize JIT and internal broker state.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_ReferenceBase_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.03; // ~10x current observed ≈0.0026 ms

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Reference base average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 3 – Reference Base with Max Fan-In/Fan-Out Converters
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when the
        ///     reference base (5 factories, 4 habs) is populated with
        ///     synthetic converters that represent the worst-case fan-in / fan-out
        ///     pattern observed in the real converter data.
        ///
        ///     Reference base:
        ///         • 5 Factory parts, each with 6 converters  → 30 converters
        ///         • 4 Hab parts, each with 2 converters      →  8 converters
        ///           --------------------------------------------------------
        ///           Total converters                          → 38 converters
        ///
        ///     Worst-case structural pattern:
        ///         • 5 input resources per converter
        ///         • 3 output resources per converter
        ///         • Input throughput per converter  ≈ 17.27 units/sec (split over 5 inputs)
        ///         • Output throughput per converter ≈ 23.22 units/sec (split over 3 outputs)
        ///
        /// CURRENT OBSERVED PERFORMANCE (on this machine):
        ///     • Average ≈ 0.0037 ms/tick
        ///
        /// PERFORMANCE BUDGET:
        ///     • maxAverageMillisecondsPerTick = 0.04 ms
        ///       (≈ 10–11x the observed average; enough headroom for hardware variance
        ///        but still tight enough to catch big regressions in per-converter
        ///        enumeration or ledger handling.)
        /// </summary>
        [Fact]
        public void Broker_Performance_ReferenceBase_MaxFanInFanOutConverters_IsUnderBudget()
        {
            // --- Arrange ---
            const int factoryCount = 5;
            const int convertersPerFactory = 6;
            const int habCount = 4;
            const int convertersPerHab = 2;

            const int factoryConverterCount = factoryCount * convertersPerFactory; // 30
            const int habConverterCount = habCount * convertersPerHab;             //  8
            const int totalConverterCount = factoryConverterCount + habConverterCount; // 38

            // From the observed statistics:
            const double averageInputThroughputPerConverter = 17.27;  // units/sec (approx)
            const double averageOutputThroughputPerConverter = 23.22; // units/sec (approx)

            // Worst-case structural pattern from analysis:
            const int inputCountPerConverter = 5;
            const int outputCountPerConverter = 3;

            double inputAmountPerSecond = averageInputThroughputPerConverter / inputCountPerConverter;
            double outputAmountPerSecond = averageOutputThroughputPerConverter / outputCountPerConverter;

            var broker = new OmniResourceBroker();
            var contexts = new List<MultiResourceConverterContext>(totalConverterCount);

            // "Factory" converters: 5 inputs (1..5), 3 outputs (100..102)
            int[] factoryInputResourceIds = { 1, 2, 3, 4, 5 };
            int[] factoryOutputResourceIds = { 100, 101, 102 };

            for (int index = 0; index < factoryConverterCount; index++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: factoryInputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: factoryOutputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // "Hab" converters: 5 inputs (6..10), 3 outputs (110..112)
            int[] habInputResourceIds = { 6, 7, 8, 9, 10 };
            int[] habOutputResourceIds = { 110, 111, 112 };

            for (int index = 0; index < habConverterCount; index++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: habInputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: habOutputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // Warmup ticks to stabilize JIT and internal broker state.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_ReferenceBase_MaxFanInFanOutConverters_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.04; // ~10x current observed ≈0.0037 ms

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Max-fan-in/fan-out reference base average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 4 – Reference Base with Slowest-Throughput Converters
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when the
        ///     reference base (5 factories, 4 habs) is populated with
        ///     synthetic converters that emulate the *slowest overall throughput*
        ///     OMNICONVERTER observed in the real data set.
        ///
        /// REFERENCE BASE:
        ///     • 5 Factory parts, each with 6 converters  → 30 converters
        ///     • 4 Hab parts, each with 2 converters      →  8 converters
        ///       --------------------------------------------------------
        ///       Total converters                          → 38 converters
        ///
        /// SLOWEST-THROUGHPUT PATTERN (from analysis of real converters):
        ///     • Example: "Air Scrubber"–like converter
        ///         - 3 INPUT_RESOURCE entries
        ///             · Input total ≈ 0.25134 units/sec
        ///         - 1 OUTPUT_RESOURCE entry
        ///             · Output total ≈ 0.00074 units/sec
        ///         - Overall throughput (inputs + outputs) ≈ 0.25208 units/sec
        ///
        /// SYNTHETIC MODEL USED IN THIS TEST:
        ///     • All 38 converters are modeled as "slow" converters:
        ///         - 3 input resources per converter
        ///         - 1 output resource per converter
        ///     • Throughput per converter:
        ///         - Total input throughput per converter:  0.25134 units/sec
        ///         - Total output throughput per converter: 0.00074 units/sec
        ///     • Because MultiResourceConverterContext applies a single rate per
        ///       input and per output group, we split the total evenly:
        ///         - Per-input amount/sec  = 0.25134 / 3
        ///         - Per-output amount/sec = 0.00074
        ///
        ///     • "Factory" converters:
        ///         - Inputs:  resource IDs { 1, 2, 3 }
        ///         - Outputs: resource ID  { 100 }
        ///     • "Hab" converters:
        ///         - Inputs:  resource IDs { 4, 5, 6 }
        ///         - Outputs: resource ID  { 101 }
        ///
        /// TEST EXECUTION:
        ///     • Create 30 Factory converters and 8 Hab converters using the
        ///       slow-throughput pattern above.
        ///     • Register all 38 with a single OmniResourceBroker instance.
        ///     • Run 100 warmup ticks (unmeasured) to stabilize JIT and caches.
        ///     • Run 10,000 measured ticks with deltaTime = 1.0s.
        ///     • Compute average milliseconds per tick and assert it remains under
        ///       a small performance budget.
        ///
        /// WHY THIS TEST MATTERS:
        ///     This test answers:
        ///
        ///         "If my  base is made entirely of low-throughput,
        ///          trickle-rate converters, does that materially change broker
        ///          performance compared to average or high-throughput cases?"
        ///
        ///     Since broker cost should depend primarily on *counts* of converters
        ///     and reports (not the magnitude of their Ratio values), this test
        ///     helps confirm that extremely small throughputs do not introduce
        ///     hidden bottlenecks.
        /// </summary>
        [Fact]
        public void Broker_Performance_ReferenceBase_SlowestThroughputConverters_IsUnderBudget()
        {
            // --- Arrange ---
            const int factoryCount = 5;
            const int convertersPerFactory = 6;
            const int habCount = 4;
            const int convertersPerHab = 2;

            const int factoryConverterCount = factoryCount * convertersPerFactory; // 30
            const int habConverterCount = habCount * convertersPerHab;             //  8
            const int totalConverterCount = factoryConverterCount + habConverterCount; // 38

            // Slowest-throughput totals derived from real OMNICONVERTER analysis.
            const double totalInputThroughputPerConverter = 0.25134;  // units/sec (3 inputs combined)
            const double totalOutputThroughputPerConverter = 0.00074; // units/sec (single output)

            const int inputCountPerConverter = 3;
            const int outputCountPerConverter = 1;

            double inputAmountPerSecond = totalInputThroughputPerConverter / inputCountPerConverter;
            double outputAmountPerSecond = totalOutputThroughputPerConverter / outputCountPerConverter;

            var broker = new OmniResourceBroker();
            var contexts = new List<MultiResourceConverterContext>(totalConverterCount);

            // "Factory" converters: 3 inputs (1,2,3), 1 output (100)
            int[] factoryInputResourceIds = { 1, 2, 3 };
            int[] factoryOutputResourceIds = { 100 };

            for (int index = 0; index < factoryConverterCount; index++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: factoryInputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: factoryOutputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // "Hab" converters: 3 inputs (4,5,6), 1 output (101)
            int[] habInputResourceIds = { 4, 5, 6 };
            int[] habOutputResourceIds = { 101 };

            for (int index = 0; index < habConverterCount; index++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: habInputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: habOutputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);

                broker.RegisterResourceConverter(converter);
                contexts.Add(context);
            }

            // Warmup ticks to stabilize JIT and internal broker state.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_ReferenceBase_SlowestThroughputConverters_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            // Same budget as the "average" reference base; slow throughput should
            // not materially change broker cost compared to normal converters.
            const double maxAverageMillisecondsPerTick = 0.03;

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Slow-throughput reference base average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 5 – Worst-Case Per-Converter Enumeration Cost
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when a
        ///     *single* converter is configured with an extreme number of input and
        ///     output resources, in order to stress:
        ///         • Per-converter enumeration over ConsumerReports / ProducerReports
        ///         • Broker-side aggregation by resource ID
        ///
        /// SCENARIO:
        ///     • 1 OmniResourceConverter
        ///     • 64 input resources per converter
        ///     • 64 output resources per converter
        ///
        ///     This represents a "pathological" converter that touches a large number
        ///     of resources simultaneously, far beyond anything expected in normal
        ///     gameplay, and is intended specifically to probe worst-case behavior.
        ///
        /// SYNTHETIC MODEL:
        ///     • Inputs:
        ///         - Resource IDs:  1 .. 64
        ///         - Total input throughput per converter:  64.0 units/sec
        ///         - Per-input amount/sec = 64.0 / 64 = 1.0 units/sec
        ///     • Outputs:
        ///         - Resource IDs: 100 .. 163
        ///         - Total output throughput per converter: 64.0 units/sec
        ///         - Per-output amount/sec = 64.0 / 64 = 1.0 units/sec
        ///
        ///     • MultiResourceConverterContext registers:
        ///         - 64 ConsumerReports (required) for the inputs
        ///         - 64 ProducerReports for the outputs
        ///
        /// TEST EXECUTION:
        ///     • Create a single converter with the 64-in / 64-out configuration.
        ///     • Register it with an OmniResourceBroker instance.
        ///     • Run 100 warmup ticks (unmeasured) to stabilize JIT and caches.
        ///     • Run 10,000 measured ticks with deltaTime = 1.0s.
        ///     • Compute average milliseconds per tick and assert it remains under
        ///       a modest performance budget.
        ///
        /// WHY THIS TEST MATTERS:
        ///     This test answers:
        ///
        ///         "How badly does performance suffer if one converter has a very
        ///          large number of inputs and outputs?"
        ///
        ///     It specifically stresses:
        ///         • The inner loops that walk per-converter report lists.
        ///         • Broker methods that group and sum reports by resource ID.
        ///
        ///     Even if real converters never reach this scale, keeping this test
        ///     green provides a strong guardrail against future changes that might
        ///     inadvertently make per-converter enumeration expensive.
        /// </summary>
        [Fact]
        public void Broker_Performance_SingleConverter_MaxEnumerationCost_IsUnderBudget()
        {
            // --- Arrange ---
            const int consumerCount = 64;
            const int producerCount = 64;

            // Throughput totals are arbitrary; enumeration cost depends on counts,
            // not magnitudes, but we keep them reasonable and symmetric.
            const double totalInputThroughputPerConverter = 64.0;   // units/sec across all inputs
            const double totalOutputThroughputPerConverter = 64.0;  // units/sec across all outputs

            double inputAmountPerSecond = totalInputThroughputPerConverter / consumerCount;
            double outputAmountPerSecond = totalOutputThroughputPerConverter / producerCount;

            var broker = new OmniResourceBroker();

            // Build arrays of distinct resource IDs.
            var inputResourceIds = new int[consumerCount];
            var outputResourceIds = new int[producerCount];

            for (int i = 0; i < consumerCount; i++)
            {
                inputResourceIds[i] = i + 1;          // 1 .. 64
            }

            for (int i = 0; i < producerCount; i++)
            {
                outputResourceIds[i] = 100 + i;       // 100 .. 163
            }

            var context = new MultiResourceConverterContext(
                inputResourceIds: inputResourceIds,
                inputAmountPerSecond: inputAmountPerSecond,
                outputResourceIds: outputResourceIds,
                outputAmountPerSecond: outputAmountPerSecond);

            var converter = new OmniResourceConverter();
            converter.Initialize(broker, context);

            context.ConverterId = converter.Id;

            broker.RegisterResourceConverter(converter);

            // Warmup ticks to stabilize JIT and internal broker state.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_SingleConverter_MaxEnumerationCost_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            // This is a deliberately extreme converter, but we still expect the
            // broker to handle it in well under 0.05 ms/tick on typical hardware.
            const double maxAverageMillisecondsPerTick = 0.05;

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Max-enumeration single-converter average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 6 – Broker Scaling with Converter Count
        ///
        /// PURPOSE:
        ///     Measures how the average cost of a single OmniResourceBroker tick scales
        ///     as the number of converters increases. This test uses the same synthetic
        ///     "average" converter pattern as the baseline test, then runs the broker
        ///     with different converter counts to ensure:
        ///
        ///         • Per-tick cost grows roughly linearly with the number of converters.
        ///         • Even at high counts, the broker remains within a reasonable budget.
        ///
        /// SCENARIO:
        ///     • Synthetic "average" converters, matching prior statistics:
        ///         - ~2 inputs, 1 output per converter
        ///         - Average total input throughput per converter:  ~17.27 units/sec
        ///         - Average total output throughput per converter: ~23.22 units/sec
        ///
        ///     • We measure three converter counts:
        ///         - 10 converters   → "small vessel" / simple base
        ///         - 100 converters  → "large vessel" / multi-bay base
        ///         - 1000 converters → "extreme stress" scenario
        ///
        /// MODEL:
        ///     • Inputs:
        ///         - Resource IDs: { 1, 2 }
        ///         - Per-input amount/sec  = 17.27 / 2
        ///     • Outputs:
        ///         - Resource ID: { 100 }
        ///         - Per-output amount/sec = 23.22
        ///
        ///     • All converters share these resource IDs so that the broker ledger
        ///       actually has something to balance.
        ///
        /// TEST EXECUTION:
        ///     • For each converter count in {10, 100, 1000}:
        ///         1. Create the requested number of synthetic average converters,
        ///            each using MultiResourceConverterContext.
        ///         2. Register them with a fresh OmniResourceBroker instance.
        ///         3. Run 100 warmup ticks (unmeasured).
        ///         4. Run 10,000 measured ticks with deltaTime = 1.0s.
        ///         5. Compute average milliseconds per tick and log it.
        ///
        /// PERFORMANCE BUDGET:
        ///     • We expect roughly linear scaling with converter count, and we know
        ///       from previous tests that 69 converters cost ≈ 0.0045 ms/tick.
        ///       That implies on this machine a "slope" of about 0.000065 ms/tick
        ///       per converter.
        ///
        ///     • To keep the assertion simple and robust across hardware, we use:
        ///         - maxAverageMillisecondsPerTick = baseBudgetPerConverter * converterCount
        ///         - baseBudgetPerConverter        = 0.0002 ms
        ///
        ///       Examples:
        ///         - 10 converters   → 0.002 ms budget
        ///         - 100 converters  → 0.020 ms budget
        ///         - 1000 converters → 0.200 ms budget
        ///
        ///       These budgets are ≈3x or better looser than observed behavior,
        ///       while still catching major regressions in broker cost per converter.
        /// </summary>
        [Fact]
        public void Broker_Performance_ScalingWithConverterCount_IsUnderBudget()
        {
            // --- Arrange shared stats for synthetic "average" converters ---
            const double averageInputThroughputPerConverter = 17.27;  // units/sec (approx)
            const double averageOutputThroughputPerConverter = 23.22; // units/sec (approx)

            const int inputCountPerConverter = 2;
            const int outputCountPerConverter = 1;

            double inputAmountPerSecond = averageInputThroughputPerConverter / inputCountPerConverter;
            double outputAmountPerSecond = averageOutputThroughputPerConverter / outputCountPerConverter;

            // Converter counts to test.
            int[] converterCounts = { 10, 100, 1000 };

            // Base per-converter budget in ms/tick.
            const double baseBudgetPerConverter = 0.0002; // ms per converter per tick

            // Local function to build and measure a scenario for a given converter count.
            static double MeasureAverageMillisecondsPerTick(
                int converterCount,
                double inputAmountPerSecond,
                double outputAmountPerSecond)
            {
                var broker = new OmniResourceBroker();

                // Shared resource IDs for all converters.
                int[] inputResourceIds = { 1, 2 };
                int[] outputResourceIds = { 100 };

                for (int converterIndex = 0; converterIndex < converterCount; converterIndex++)
                {
                    var context = new MultiResourceConverterContext(
                        inputResourceIds: inputResourceIds,
                        inputAmountPerSecond: inputAmountPerSecond,
                        outputResourceIds: outputResourceIds,
                        outputAmountPerSecond: outputAmountPerSecond);

                    var converter = new OmniResourceConverter();
                    converter.Initialize(broker, context);
                    context.ConverterId = converter.Id;

                    broker.RegisterResourceConverter(converter);
                }

                // Warmup ticks to stabilize JIT and internal broker state.
                const int warmupTickCount = 100;
                for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
                {
                    broker.RunConverters(deltaTime: 1.0);
                }

                // Measured ticks.
                const int measuredTickCount = 10_000;
                var stopwatch = Stopwatch.StartNew();

                for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
                {
                    broker.RunConverters(deltaTime: 1.0);
                }

                stopwatch.Stop();

                return stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;
            }

            // --- Act + Assert per scenario ---
            foreach (int converterCount in converterCounts)
            {
                double averageMillisecondsPerTick =
                    MeasureAverageMillisecondsPerTick(
                        converterCount,
                        inputAmountPerSecond,
                        outputAmountPerSecond);

                PerfTestLog.Report(
                    testOutput,
                    $"Broker_Performance_ScalingWithConverterCount_{converterCount}",
                    averageMillisecondsPerTick);

                double maxAverageMillisecondsPerTick = baseBudgetPerConverter * converterCount;

                Assert.True(
                    averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                    $"Scaling test with {converterCount} converters exceeded budget: " +
                    $"average {averageMillisecondsPerTick:F4} ms/tick vs budget {maxAverageMillisecondsPerTick:F4} ms.");
            }
        }

        /// <summary>
        /// Performance Test 7 – Worst-Case Ledger Refresh Under High Converter Churn
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when the
        ///     broker is forced to refresh its internal ledger on *every tick* due to
        ///     constant converter registration ("churn").
        ///
        ///     This specifically stresses:
        ///         • refreshLedgerIfNeeded and its full rebuild path
        ///         • converter.RegisterReports → context.RegisterReports
        ///         • per-resource dictionaries being cleared and repopulated
        ///
        /// SCENARIO OVERVIEW:
        ///     • Start with an initial pool of converters (100), all using the
        ///       synthetic "average" converter pattern:
        ///         - ~2 inputs, 1 output per converter
        ///         - Average total input throughput per converter:  ~17.27 units/sec
        ///         - Average total output throughput per converter: ~23.22 units/sec
        ///
        ///     • Then, for each measured tick:
        ///         - Create a *new* synthetic average converter
        ///         - Initialize it and register it with the broker
        ///             → This sets the broker's internal needsRefresh flag
        ///         - Call RunConverters(deltaTime = 1.0)
        ///
        ///     As a result:
        ///         • The number of converters grows from 100 up to 1,100 over the
        ///           course of the test.
        ///         • The broker must rebuild its ledger on *every* RunConverters call,
        ///           using an increasingly large converter set.
        ///         • This approximates a worst-case environment where converters are
        ///           constantly added (e.g., parts spawning, modules toggling) and the
        ///           broker is never allowed to stay in "steady-state".
        ///
        /// SYNTHETIC CONVERTER MODEL (SAME AS BASELINE AVERAGE TEST):
        ///     • Inputs:
        ///         - Resource IDs: { 1, 2 }
        ///         - Total input throughput per converter:  ~17.27 units/sec
        ///         - Per-input amount/sec  = 17.27 / 2
        ///     • Outputs:
        ///         - Resource ID: { 100 }
        ///         - Total output throughput per converter: ~23.22 units/sec
        ///         - Per-output amount/sec = 23.22
        ///
        /// TEST EXECUTION:
        ///     • Create a broker.
        ///     • Create and register 100 synthetic average converters (initial steady set).
        ///     • Run 100 warmup ticks with RunConverters(deltaTime = 1.0) to stabilize JIT.
        ///     • Then:
        ///         - For measuredTickCount = 1,000 iterations:
        ///             1. Create a new synthetic average converter.
        ///             2. Initialize and register it with the broker.
        ///             3. Call RunConverters(deltaTime = 1.0).
        ///         - Measure total time and compute average ms/tick.
        ///
        /// PERFORMANCE BUDGET:
        ///     • We know from earlier tests that:
        ///         - 1000 steady-state converters cost ≈ 0.063 ms/tick on this machine.
        ///     • This test requires *full ledger rebuild* plus converter churn on
        ///       every tick and grows to 1,100 converters, so we allow a higher
        ///       budget while still catching serious regressions:
        ///
        ///         maxAverageMillisecondsPerTick = 0.20 ms
        ///
        ///     • This is intentionally lenient but still low enough to flag big
        ///       performance issues in refreshLedgerIfNeeded or registration logic.
        /// </summary>
        [Fact]
        public void Broker_Performance_WorstCase_LedgerRefreshUnderConverterChurn_IsUnderBudget()
        {
            // --- Arrange ---
            const int initialConverterCount = 100;

            // From the observed statistics (synthetic "average" converters):
            const double averageInputThroughputPerConverter = 17.27;  // units/sec (approx)
            const double averageOutputThroughputPerConverter = 23.22; // units/sec (approx)

            const int inputCountPerConverter = 2;
            const int outputCountPerConverter = 1;

            double inputAmountPerSecond = averageInputThroughputPerConverter / inputCountPerConverter;
            double outputAmountPerSecond = averageOutputThroughputPerConverter / outputCountPerConverter;

            var broker = new OmniResourceBroker();

            // Shared resource IDs for all converters.
            int[] inputResourceIds = { 1, 2 };
            int[] outputResourceIds = { 100 };

            // Initial "steady" set of converters.
            for (int converterIndex = 0; converterIndex < initialConverterCount; converterIndex++)
            {
                var context = new MultiResourceConverterContext(
                    inputResourceIds: inputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: outputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Warmup ticks to stabilize JIT and internal broker state.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 1_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                // Each tick, create and register a new converter to force needsRefresh
                // and trigger a full ledger rebuild in RunConverters.
                var context = new MultiResourceConverterContext(
                    inputResourceIds: inputResourceIds,
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: outputResourceIds,
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);

                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_WorstCase_LedgerRefreshUnderConverterChurn_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.20; // generous budget for full-rebuild every tick

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Worst-case ledger refresh under converter churn average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 8 – Many Distinct Resources with Large Converter Count
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when it
        ///     manages a large number of converters, each touching *unique* resource
        ///     IDs. This stresses:
        ///         • The size and behavior of per-resource dictionaries
        ///         • Lookup and aggregation across many distinct resource IDs
        ///
        /// SCENARIO:
        ///     • 1,000 synthetic converters
        ///     • Each converter has:
        ///         - 1 input resource (unique ID per converter)
        ///         - 1 output resource (unique ID per converter)
        ///
        ///     • This yields:
        ///         - 1,000 distinct input resource IDs
        ///         - 1,000 distinct output resource IDs
        ///         - 2,000 total resource IDs tracked by the broker's internal maps
        ///
        /// SYNTHETIC CONVERTER MODEL:
        ///     • Inputs:
        ///         - Resource ID:  (converterIndex + 1)           // 1 .. 1000
        ///         - Per-input amount/sec  = 10.0 units/sec
        ///     • Outputs:
        ///         - Resource ID:  (1000 + converterIndex + 1)    // 1001 .. 2000
        ///         - Per-output amount/sec = 10.0 units/sec
        ///
        ///     • All converters are driven by MultiResourceConverterContext, which
        ///       registers one ConsumerReport and one ProducerReport each.
        ///
        /// TEST EXECUTION:
        ///     • Create a broker.
        ///     • For converterIndex in [0, 999]:
        ///         - Create a context with a unique input and output resource ID.
        ///         - Initialize and register an OmniResourceConverter.
        ///     • Run 100 warmup ticks with RunConverters(deltaTime = 1.0).
        ///     • Run 10,000 measured ticks with RunConverters(deltaTime = 1.0).
        ///     • Compute average milliseconds per tick.
        ///
        /// PERFORMANCE BUDGET:
        ///     • From prior tests, 1000 converters with shared resource IDs cost
        ///       ≈ 0.0631 ms/tick on this machine.
        ///     • This test uses unique resource IDs (more dictionary entries and
        ///       slightly more work per tick), so we allow a bit more headroom:
        ///
        ///         maxAverageMillisecondsPerTick = 0.10 ms
        ///
        ///     • This is still tight enough to catch major regressions in
        ///       dictionary handling and per-resource aggregation.
        /// </summary>
        [Fact]
        public void Broker_Performance_ManyDistinctResources_LargeConverterCount_IsUnderBudget()
        {
            // --- Arrange ---
            const int converterCount = 1_000;

            // Throughput choice is arbitrary here; enumeration cost depends on counts
            // and dictionary usage, not magnitude, but we keep it reasonable.
            const double inputAmountPerSecond = 10.0;
            const double outputAmountPerSecond = 10.0;

            var broker = new OmniResourceBroker();

            for (int converterIndex = 0; converterIndex < converterCount; converterIndex++)
            {
                int inputResourceId = converterIndex + 1;             // 1 .. 1000
                int outputResourceId = 1000 + converterIndex + 1;     // 1001 .. 2000

                var context = new MultiResourceConverterContext(
                    inputResourceIds: new[] { inputResourceId },
                    inputAmountPerSecond: inputAmountPerSecond,
                    outputResourceIds: new[] { outputResourceId },
                    outputAmountPerSecond: outputAmountPerSecond);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Warmup ticks to stabilize JIT and internal broker state.
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime: 1.0);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_ManyDistinctResources_LargeConverterCount_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.10; // slightly above 1000-shared-ID case

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Many-distinct-resources (1000 converters) average milliseconds per tick " +
                $"({averageMillisecondsPerTick:F4} ms) exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 9 – Mixed Required and Optional Consumers Under Load
        ///
        /// PURPOSE:
        ///     Measures the average cost of a single OmniResourceBroker tick when a
        ///     large number of required and optional consumers all compete for the
        ///     same produced resource. This specifically stresses:
        ///         • The required vs optional split in BuildTotals
        ///         • Computation of satisfactionRatio and optionalSatisfactionRatio
        ///         • Per-resource aggregation when many consumers hit the same ID
        ///
        /// SCENARIO:
        ///     • 1 shared resource ID (e.g., 500)
        ///     • 100 producers
        ///     • 100 required consumers
        ///     • 300 optional consumers
        ///
        ///     Total converters: 500
        ///
        /// SYNTHETIC MODEL:
        ///     • Each producer:
        ///         - ResourceId: 500
        ///         - AmountPerSecond: 50.0
        ///     • Each required consumer:
        ///         - ResourceId: 500
        ///         - AmountPerSecond: 25.0
        ///         - IsOptional: false
        ///     • Each optional consumer:
        ///         - ResourceId: 500
        ///         - AmountPerSecond: 25.0
        ///         - IsOptional: true
        ///
        /// TEST EXECUTION:
        ///     • Create broker.
        ///     • Register 100 producer converters, 100 required consumers, 300 optional consumers.
        ///     • Run 100 warmup ticks (unmeasured) with deltaTime = 1.0.
        ///     • Run 10,000 measured ticks with deltaTime = 1.0.
        ///     • Compute average ms/tick and assert under budget.
        ///
        /// PERFORMANCE BUDGET:
        ///     • 500 converters is between the 100 and 1000 converter scaling tests.
        ///     • Observed behavior for 1000 converters is ≈ 0.063 ms/tick.
        ///     • Budget: maxAverageMillisecondsPerTick = 0.08 ms.
        /// </summary>
        [Fact]
        public void Broker_Performance_MixedRequiredAndOptionalConsumers_UnderLoad_IsUnderBudget()
        {
            // --- Arrange ---
            var broker = new OmniResourceBroker();
            const int resourceId = 500;

            const int producerCount = 100;
            const int requiredConsumerCount = 100;
            const int optionalConsumerCount = 300;

            const double producerRatePerSecond = 50.0;
            const double consumerRatePerSecond = 25.0;

            const double deltaTime = 1.0;

            // Producers
            for (int index = 0; index < producerCount; index++)
            {
                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: producerRatePerSecond,
                    isProducer: true,
                    isOptional: false);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Required consumers
            for (int index = 0; index < requiredConsumerCount; index++)
            {
                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: consumerRatePerSecond,
                    isProducer: false,
                    isOptional: false);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Optional consumers
            for (int index = 0; index < optionalConsumerCount; index++)
            {
                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: consumerRatePerSecond,
                    isProducer: false,
                    isOptional: true);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Warmup ticks
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_MixedRequiredAndOptionalConsumers_UnderLoad_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.08;

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Mixed required/optional consumers under load average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 10 – Finite-Capacity Optional Consumers at Scale
        ///
        /// PURPOSE:
        ///     Measures the average cost of a broker tick when many finite-capacity
        ///     optional consumers are active. This specifically stresses:
        ///         • FiniteCapacityOptionalConsumerContext.RegisterReports
        ///         • Capacity checks and remaining-capacity math
        ///         • Optional-consumer aggregation in BuildTotals
        ///
        /// SCENARIO:
        ///     • 1 shared resource ID (e.g., 600)
        ///     • 100 producers
        ///     • 500 finite-capacity optional consumers
        ///
        ///     Total converters: 600
        ///
        /// SYNTHETIC MODEL:
        ///     • Each producer:
        ///         - ResourceId: 600
        ///         - AmountOfferedPerSec: 50.0
        ///     • Each finite-capacity consumer:
        ///         - ResourceId: 600
        ///         - MaxFillRatePerSecond: 10.0
        ///         - Capacity: 1,000,000.0
        ///         - IsOptional: true (inside the context)
        ///
        ///     The capacity is intentionally huge so that over 10,000 ticks at
        ///     10 units/tick, the tank never fills:
        ///         10,000 ticks × 10 units = 100,000 < 1,000,000
        ///
        ///     This guarantees that all consumers stay "active" and exercise the
        ///     finite-capacity logic on every tick.
        ///
        /// TEST EXECUTION:
        ///     • Create broker.
        ///     • Register producers and finite-capacity consumers.
        ///     • Run 100 warmup ticks (unmeasured) with deltaTime = 1.0.
        ///     • Run 10,000 measured ticks with deltaTime = 1.0.
        ///     • Compute average ms/tick.
        ///
        /// PERFORMANCE BUDGET:
        ///     • 600 converters with a bit of extra context logic.
        ///     • Budget: maxAverageMillisecondsPerTick = 0.08 ms.
        /// </summary>
        [Fact]
        public void Broker_Performance_FiniteCapacityOptionalConsumers_AtScale_IsUnderBudget()
        {
            // --- Arrange ---
            var broker = new OmniResourceBroker();
            const int resourceId = 600;

            const int producerCount = 100;
            const int consumerCount = 500;

            const double producerRatePerSecond = 50.0;
            const double consumerMaxFillRatePerSecond = 10.0;
            const double consumerCapacity = 1_000_000.0;

            const double deltaTime = 1.0;

            // Producers
            for (int index = 0; index < producerCount; index++)
            {
                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: producerRatePerSecond,
                    isProducer: true,
                    isOptional: false);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Finite-capacity optional consumers
            for (int index = 0; index < consumerCount; index++)
            {
                var context = new FiniteCapacityOptionalConsumerContext(
                    resourceId: resourceId,
                    maxFillRatePerSecond: consumerMaxFillRatePerSecond,
                    capacity: consumerCapacity);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Warmup ticks
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_FiniteCapacityOptionalConsumers_AtScale_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.08;

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"Finite-capacity optional consumers at scale average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 11 – All Producers, No Consumers (Producer-Only Workload)
        ///
        /// PURPOSE:
        ///     Measures the average cost of a broker tick when *only producers*
        ///     are registered and there are no consumers at all. This is a
        ///     near-idle broker scenario where:
        ///         • Producer reports are aggregated,
        ///         • But no consumption or satisfaction math is needed.
        ///
        /// SCENARIO:
        ///     • 1,000 producer converters
        ///     • No consumers
        ///
        /// SYNTHETIC MODEL:
        ///     • Each producer:
        ///         - ResourceId: (index % 10) + 1   // 10 resource IDs shared
        ///         - AmountOfferedPerSec: 20.0
        ///
        ///     This yields:
        ///         • 10 resource IDs with many producers each.
        ///         • No consumer reports in the system.
        ///
        /// TEST EXECUTION:
        ///     • Create broker.
        ///     • Register 1,000 producers (TestConverterContext with isProducer = true).
        ///     • Run 100 warmup ticks with deltaTime = 1.0.
        ///     • Run 10,000 measured ticks with deltaTime = 1.0.
        ///     • Compute average ms/tick and assert under budget.
        ///
        /// PERFORMANCE BUDGET:
        ///     • Producer-only workload should be cheaper or comparable to mixed
        ///       producer+consumer tests at 1,000 converters (~0.063 ms/tick).
        ///     • Budget: maxAverageMillisecondsPerTick = 0.08 ms.
        /// </summary>
        [Fact]
        public void Broker_Performance_AllProducers_NoConsumers_IsUnderBudget()
        {
            // --- Arrange ---
            var broker = new OmniResourceBroker();
            const int converterCount = 1_000;
            const double producerRatePerSecond = 20.0;
            const double deltaTime = 1.0;

            for (int index = 0; index < converterCount; index++)
            {
                int resourceId = (index % 10) + 1; // spread across 10 resource IDs

                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: producerRatePerSecond,
                    isProducer: true,
                    isOptional: false);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Warmup ticks
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_AllProducers_NoConsumers_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.08;

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"All-producers/no-consumers average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

        /// <summary>
        /// Performance Test 12 – All Consumers, No Producers (Consumer-Only Workload)
        ///
        /// PURPOSE:
        ///     Measures the average cost of a broker tick when *only consumers*
        ///     are registered and there are no producers at all. This is another
        ///     near-idle broker scenario where:
        ///         • Consumer reports are aggregated,
        ///         • But production is always zero and all consumers are unbrokered.
        ///
        /// SCENARIO:
        ///     • 1,000 consumer converters
        ///     • No producers
        ///
        /// SYNTHETIC MODEL:
        ///     • Half required, half optional:
        ///         - 500 required consumers
        ///         - 500 optional consumers
        ///     • Resource IDs:
        ///         - Required:  (index % 10) + 1        // shared across 10 IDs
        ///         - Optional:  (index % 10) + 1001     // another 10 IDs
        ///
        ///     • Each consumer:
        ///         - AmountRequestedPerSec: 20.0
        ///
        /// TEST EXECUTION:
        ///     • Create broker.
        ///     • Register 500 required and 500 optional consumers (no producers).
        ///     • Run 100 warmup ticks with deltaTime = 1.0.
        ///     • Run 10,000 measured ticks with deltaTime = 1.0.
        ///     • Compute average ms/tick and assert under budget.
        ///
        /// PERFORMANCE BUDGET:
        ///     • Similar scale to the all-producers test (1,000 converters).
        ///     • Budget: maxAverageMillisecondsPerTick = 0.08 ms.
        /// </summary>
        [Fact]
        public void Broker_Performance_AllConsumers_NoProducers_IsUnderBudget()
        {
            // --- Arrange ---
            var broker = new OmniResourceBroker();
            const int requiredConsumerCount = 500;
            const int optionalConsumerCount = 500;
            const double consumerRatePerSecond = 20.0;
            const double deltaTime = 1.0;

            // Required consumers
            for (int index = 0; index < requiredConsumerCount; index++)
            {
                int resourceId = (index % 10) + 1; // 1..10

                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: consumerRatePerSecond,
                    isProducer: false,
                    isOptional: false);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Optional consumers
            for (int index = 0; index < optionalConsumerCount; index++)
            {
                int resourceId = (index % 10) + 1001; // 1001..1010

                var context = new TestConverterContext(
                    resourceId: resourceId,
                    amountPerSecond: consumerRatePerSecond,
                    isProducer: false,
                    isOptional: true);

                var converter = new OmniResourceConverter();
                converter.Initialize(broker, context);
                context.ConverterId = converter.Id;

                broker.RegisterResourceConverter(converter);
            }

            // Warmup ticks
            const int warmupTickCount = 100;
            for (int tickIndex = 0; tickIndex < warmupTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            // --- Act ---
            const int measuredTickCount = 10_000;
            var stopwatch = Stopwatch.StartNew();

            for (int tickIndex = 0; tickIndex < measuredTickCount; tickIndex++)
            {
                broker.RunConverters(deltaTime);
            }

            stopwatch.Stop();

            double averageMillisecondsPerTick =
                stopwatch.Elapsed.TotalMilliseconds / measuredTickCount;

            PerfTestLog.Report(
                testOutput,
                nameof(Broker_Performance_AllConsumers_NoProducers_IsUnderBudget),
                averageMillisecondsPerTick);

            // --- Assert ---
            const double maxAverageMillisecondsPerTick = 0.08;

            Assert.True(
                averageMillisecondsPerTick < maxAverageMillisecondsPerTick,
                $"All-consumers/no-producers average milliseconds per tick ({averageMillisecondsPerTick:F4} ms) " +
                $"exceeds performance budget of {maxAverageMillisecondsPerTick:F3} ms.");
        }

    }
}
