using System;
using Xunit;
using OmniWorks.Core;

namespace OmniWorks.Tests
{
    public class OmniResourceBrokerIntegrationTests
    {
        /// <summary>
        /// If one converter produces Resource 1 and another consumes Resource 1, and production >= consumption,
        /// then the consumer gets fully granted and the producer reports matching usage.
        /// </summary>
        [Fact]
        public void ProducerAndConsumerForSameResourceAreBrokeredCorrectly()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 1;
            const double producedPerSecond = 10.0;
            const double consumedPerSecond = 5.0;
            const double deltaTime = 1.0;

            // ----- Producer side -----
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Consumer side -----
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register converters with the broker
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            // Assert: consumer should have 1 brokered consumer report, fully satisfied.
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.Equal(consumedPerSecond * deltaTime, consumerReport.AmountGrantedPerTick, 6);

            // Assert: producer should have 1 brokered producer report, used amount == consumer demand.
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.Equal(consumedPerSecond * deltaTime, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Consumer with no matching producer → should be marked unbrokered.
        /// </summary>
        [Fact]
        public void ConsumerWithoutMatchingProducerIsUnbrokered()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 2;
            const double consumedPerSecond = 5.0;
            const double deltaTime = 1.0;

            // Consumer-only context (no producer for this resource)
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register only the consumer converter with the broker
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            // Assert
            Assert.NotNull(consumerContext.LastResults);

            // No brokered consumers when there's no producer
            Assert.Empty(consumerContext.LastResults.BrokeredConsumerReports);

            // Exactly one unbrokered consumer report
            Assert.Single(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.UnbrokeredConsumerReports[0];

            // The broker decided not to manage this one
            Assert.False(consumerReport.IsBrokered);

            // No production means nothing can be granted
            Assert.Equal(0.0, consumerReport.AmountGrantedPerTick, 6);
        }

        /// <summary>
        /// Producer offers less than the consumer requests → consumer gets partially satisfied, producer uses all it offered.
        /// </summary>
        [Fact]
        public void RequiredConsumerIsPartiallySatisfiedWhenProductionIsInsufficient()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 3;
            const double producedPerSecond = 5.0;
            const double consumedPerSecond = 10.0;
            const double deltaTime = 1.0;

            // ----- Producer -----
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Consumer (required, not optional) -----
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both converters
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            // Assert: consumer should have 1 brokered consumer report
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];

            // Requested 10/sec for 1s, but only half can be satisfied -> 5 units granted
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(consumedPerSecond * deltaTime * 0.5, consumerReport.AmountGrantedPerTick, 6);

            // Assert: producer should have 1 brokered producer report
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];

            // Producer offered 5/sec for 1s, all of it should be used
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(producedPerSecond * deltaTime, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Test idea: two required consumers share limited production
        /// Scenario:
        /// Producer: 10 units / sec
        /// Consumer A: 10 units / sec (required)
        /// Consumer B: 10 units / sec (required)
        /// 
        /// Totals:
        /// totalProduced = 10
        /// totalRequired = 20
        /// satisfactionRatio = totalProduced / totalRequired = 0.5
        /// So each consumer should get half of what they requested:
        /// Consumer A granted = 10 * 1 * 0.5 = 5
        /// Consumer B granted = 10 * 1 * 0.5 = 5
        /// Producer used = 10 * 1 = 10
        /// </summary>
        [Fact]
        public void MultipleRequiredConsumersShareInsufficientProductionProportionally()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 4;
            const double producedPerSecond = 10.0;
            const double consumedPerSecondEach = 10.0;
            const double deltaTime = 1.0;

            // ----- Producer -----
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Consumer A (required) -----
            var consumerContextA = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecondEach,
                isProducer: false,
                isOptional: false);

            var consumerConverterA = new OmniResourceConverter();
            consumerConverterA.Initialize(broker, consumerContextA);
            consumerContextA.ConverterId = consumerConverterA.Id;

            // ----- Consumer B (required) -----
            var consumerContextB = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecondEach,
                isProducer: false,
                isOptional: false);

            var consumerConverterB = new OmniResourceConverter();
            consumerConverterB.Initialize(broker, consumerContextB);
            consumerContextB.ConverterId = consumerConverterB.Id;

            // Register all converters
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverterA);
            broker.RegisterResourceConverter(consumerConverterB);

            // Act
            broker.RunConverters(deltaTime);

            // Assert: each consumer should have 1 brokered report, half satisfied (5 units).
            Assert.NotNull(consumerContextA.LastResults);
            Assert.Single(consumerContextA.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContextA.LastResults.UnbrokeredConsumerReports);

            var consumerReportA = consumerContextA.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReportA.IsBrokered);
            Assert.Equal(consumedPerSecondEach * deltaTime * 0.5, consumerReportA.AmountGrantedPerTick, 6);

            Assert.NotNull(consumerContextB.LastResults);
            Assert.Single(consumerContextB.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContextB.LastResults.UnbrokeredConsumerReports);

            var consumerReportB = consumerContextB.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReportB.IsBrokered);
            Assert.Equal(consumedPerSecondEach * deltaTime * 0.5, consumerReportB.AmountGrantedPerTick, 6);

            // Assert: producer is fully used (10 units).
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(producedPerSecond * deltaTime, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when total production is insufficient to satisfy all consumers,
        /// required consumers are fully satisfied before any optional consumers receive resources.
        /// 
        /// Scenario:
        /// - Producer provides 10 units/sec
        /// - Consumer A requires 10 units/sec (required)
        /// - Consumer B requests 10 units/sec (optional)
        /// - DeltaTime = 1 sec
        ///
        /// Expected:
        /// - Required consumer receives full 10 units
        /// - Optional consumer receives 0 units
        /// - Producer uses all 10 units
        /// </summary>
        [Fact]
        public void RequiredConsumersArePrioritizedOverOptionalConsumers()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 5;
            const double producedPerSecond = 10.0;
            const double requiredConsumedPerSecond = 10.0;
            const double optionalConsumedPerSecond = 10.0;
            const double deltaTime = 1.0;

            // ----- Producer -----
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Required consumer -----
            var requiredContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: requiredConsumedPerSecond,
                isProducer: false,
                isOptional: false);

            var requiredConverter = new OmniResourceConverter();
            requiredConverter.Initialize(broker, requiredContext);
            requiredContext.ConverterId = requiredConverter.Id;

            // ----- Optional consumer -----
            var optionalContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: optionalConsumedPerSecond,
                isProducer: false,
                isOptional: true);

            var optionalConverter = new OmniResourceConverter();
            optionalConverter.Initialize(broker, optionalContext);
            optionalContext.ConverterId = optionalConverter.Id;

            // Register converters
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(requiredConverter);
            broker.RegisterResourceConverter(optionalConverter);

            // Act
            broker.RunConverters(deltaTime);

            // ----- Required consumer assertions -----
            Assert.NotNull(requiredContext.LastResults);
            Assert.Single(requiredContext.LastResults.BrokeredConsumerReports);

            var requiredReport = requiredContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(requiredReport.IsBrokered);
            Assert.Equal(requiredConsumedPerSecond * deltaTime, requiredReport.AmountGrantedPerTick, 6);

            // ----- Optional consumer assertions -----
            Assert.NotNull(optionalContext.LastResults);
            Assert.Single(optionalContext.LastResults.BrokeredConsumerReports);

            var optionalReport = optionalContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(optionalReport.IsBrokered);
            Assert.Equal(0.0, optionalReport.AmountGrantedPerTick, 6);

            // ----- Producer assertions -----
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(producedPerSecond * deltaTime, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when multiple producers supply the same resource for a single required consumer,
        /// the consumer is fully satisfied if total production meets or exceeds demand, and each producer's
        /// used amount is proportional to its offered amount.
        ///
        /// Scenario:
        /// - Producer A offers 6 units/sec
        /// - Producer B offers 4 units/sec
        /// - Single required consumer requests 8 units/sec
        /// - DeltaTime = 1 sec
        ///
        /// Totals:
        /// - Total produced = 10 units
        /// - Total required = 8 units
        /// - Required demand is fully satisfied (satisfactionRatio = 1.0)
        /// - Total used = 8 units
        /// - Producer usage ratio = 8 / 10 = 0.8
        ///
        /// Expected:
        /// - Consumer is granted 8 units
        /// - Producer A uses 4.8 units (6 * 0.8)
        /// - Producer B uses 3.2 units (4 * 0.8)
        /// </summary>
        [Fact]
        public void MultipleProducersShareLoadProportionallyForSingleRequiredConsumer()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 6;
            const double producedPerSecondA = 6.0;
            const double producedPerSecondB = 4.0;
            const double consumedPerSecond = 8.0;
            const double deltaTime = 1.0;

            // ----- Producer A -----
            var producerContextA = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecondA,
                isProducer: true);

            var producerConverterA = new OmniResourceConverter();
            producerConverterA.Initialize(broker, producerContextA);
            producerContextA.ConverterId = producerConverterA.Id;

            // ----- Producer B -----
            var producerContextB = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecondB,
                isProducer: true);

            var producerConverterB = new OmniResourceConverter();
            producerConverterB.Initialize(broker, producerContextB);
            producerContextB.ConverterId = producerConverterB.Id;

            // ----- Required consumer -----
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register all converters
            broker.RegisterResourceConverter(producerConverterA);
            broker.RegisterResourceConverter(producerConverterB);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            // ----- Consumer assertions -----
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(consumedPerSecond * deltaTime, consumerReport.AmountGrantedPerTick, 6);

            // ----- Producer A assertions -----
            Assert.NotNull(producerContextA.LastResults);
            Assert.Single(producerContextA.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContextA.LastResults.UnbrokeredProducerReports);

            var producerReportA = producerContextA.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReportA.IsBrokered);
            Assert.Equal(producedPerSecondA * deltaTime * 0.8, producerReportA.AmountUsedPerTick, 6);

            // ----- Producer B assertions -----
            Assert.NotNull(producerContextB.LastResults);
            Assert.Single(producerContextB.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContextB.LastResults.UnbrokeredProducerReports);

            var producerReportB = producerContextB.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReportB.IsBrokered);
            Assert.Equal(producedPerSecondB * deltaTime * 0.8, producerReportB.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that required consumers are fully satisfied before optional consumers, and that
        /// optional consumers receive a proportional share of any remaining surplus production.
        ///
        /// Scenario:
        /// - Producer offers 12 units/sec
        /// - Required consumer requests 10 units/sec
        /// - Optional consumer requests 10 units/sec
        /// - deltaTime = 1 sec
        ///
        /// Totals:
        /// - totalProduced = 12
        /// - totalRequired = 10
        /// - surplus = 2
        /// - optionalSatisfactionRatio = 2 / 10 = 0.2
        ///
        /// Expected:
        /// - Required consumer receives 10 units
        /// - Optional consumer receives 2 units
        /// - Producer uses all 12 units
        /// </summary>
        [Fact]
        public void MixedRequiredAndOptionalConsumersReceiveCorrectAllocations()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 7;
            const double producerRate = 12.0;
            const double requiredRate = 10.0;
            const double optionalRate = 10.0;
            const double deltaTime = 1.0;

            // ----- Producer -----
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producerRate,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Required Consumer -----
            var requiredContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: requiredRate,
                isProducer: false,
                isOptional: false);

            var requiredConverter = new OmniResourceConverter();
            requiredConverter.Initialize(broker, requiredContext);
            requiredContext.ConverterId = requiredConverter.Id;

            // ----- Optional Consumer -----
            var optionalContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: optionalRate,
                isProducer: false,
                isOptional: true);

            var optionalConverter = new OmniResourceConverter();
            optionalConverter.Initialize(broker, optionalContext);
            optionalContext.ConverterId = optionalConverter.Id;

            // Register all converters
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(requiredConverter);
            broker.RegisterResourceConverter(optionalConverter);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Required consumer assertions
            //
            Assert.NotNull(requiredContext.LastResults);
            Assert.Single(requiredContext.LastResults.BrokeredConsumerReports);

            var requiredReport = requiredContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(requiredReport.IsBrokered);
            Assert.Equal(requiredRate * deltaTime, requiredReport.AmountGrantedPerTick, 6);

            //
            // Optional consumer assertions
            //
            Assert.NotNull(optionalContext.LastResults);
            Assert.Single(optionalContext.LastResults.BrokeredConsumerReports);

            var optionalReport = optionalContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(optionalReport.IsBrokered);
            Assert.Equal(2.0, optionalReport.AmountGrantedPerTick, 6);   // 10 * 0.2

            //
            // Producer assertions
            //
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(12.0, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when a converter's resource usage changes and it flags NeedsRefresh,
        /// the resource broker rebuilds its ledger on the next run and stops brokering the
        /// old resource connections.
        /// 
        /// Scenario:
        /// - Producer and consumer initially both use resource 8 at 10 units/sec
        /// - First RunConverters:
        ///     - Consumer is fully satisfied for resource 8
        ///     - Producer is fully used for resource 8
        /// - Then the consumer switches to resource 9 and sets NeedsRefresh = true
        /// - Second RunConverters:
        ///     - Producer now has no matching consumer for resource 8 -> unbrokered
        ///     - Consumer now has no matching producer for resource 9 -> unbrokered
        ///     - No amounts are granted or used for either side
        /// </summary>
        [Fact]
        public void NeedsRefreshCausesLedgerRebuildWhenConverterChangesResources()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const double ratePerSecond = 10.0;
            const double deltaTime = 1.0;

            const int initialResourceId = 8;
            const int updatedResourceId = 9;

            // ----- Producer (always produces resource 8) -----
            var producerContext = new TestConverterContext(
                resourceId: initialResourceId,
                amountPerSecond: ratePerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Consumer (starts consuming resource 8) -----
            var consumerContext = new TestConverterContext(
                resourceId: initialResourceId,
                amountPerSecond: ratePerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register converters with the broker
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            //
            // Phase 1: initial run - producer and consumer are connected on resource 8
            //
            broker.RunConverters(deltaTime);

            // Consumer should be fully brokered and satisfied on resource 8
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var initialConsumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(initialConsumerReport.IsBrokered);
            Assert.Equal(ratePerSecond * deltaTime, initialConsumerReport.AmountGrantedPerTick, 6);

            // Producer should be brokered and fully used on resource 8
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var initialProducerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(initialProducerReport.IsBrokered);
            Assert.Equal(ratePerSecond * deltaTime, initialProducerReport.AmountUsedPerTick, 6);

            //
            // Phase 2: change consumer's resource and flag NeedsRefresh
            //
            consumerContext.ResourceId = updatedResourceId;
            consumerConverter.NeedsRefresh = true;   // propagates to broker.NeedsRefresh

            broker.RunConverters(deltaTime);

            //
            // After refresh:
            // - Producer's resource (8) is isolated -> unbrokered
            // - Consumer's resource (9) is isolated -> unbrokered
            //

            // Producer assertions after refresh
            Assert.NotNull(producerContext.LastResults);
            Assert.Empty(producerContext.LastResults.BrokeredProducerReports);
            Assert.Single(producerContext.LastResults.UnbrokeredProducerReports);

            var refreshedProducerReport = producerContext.LastResults.UnbrokeredProducerReports[0];
            Assert.False(refreshedProducerReport.IsBrokered);
            Assert.Equal(0.0, refreshedProducerReport.AmountUsedPerTick, 6);

            // Consumer assertions after refresh
            Assert.NotNull(consumerContext.LastResults);
            Assert.Empty(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Single(consumerContext.LastResults.UnbrokeredConsumerReports);

            var refreshedConsumerReport = consumerContext.LastResults.UnbrokeredConsumerReports[0];
            Assert.False(refreshedConsumerReport.IsBrokered);
            Assert.Equal(0.0, refreshedConsumerReport.AmountGrantedPerTick, 6);
        }

        /// <summary>
        /// Verifies that the broker culls isolated resources for a converter that touches
        /// both shared and isolated resources, by:
        /// - Keeping reports for resources that are connected to at least one other converter
        ///   (i.e., producer + consumer) and marking them brokered, and
        /// - Marking reports for isolated resources as unbrokered and removing them from
        ///   the broker's internal ledgers.
        ///
        /// Scenario:
        /// - One producer converter offers two resources:
        ///     - Primary resource 100 at 10 units/sec
        ///     - Isolated resource 101 at 5 units/sec
        /// - One consumer converter requires resource 100 at 6 units/sec
        /// - deltaTime = 1 sec
        ///
        /// Expected:
        /// - Resource 100 is shared between producer and consumer:
        ///     - Consumer is brokered and granted 6 units
        ///     - Producer's primary report is brokered and uses 6 units
        /// - Resource 101 is only touched by the producer:
        ///     - Broker culls it as isolated
        ///     - Producer's isolated report is marked unbrokered
        ///     - Isolated report's AmountUsedPerTick is 0
        /// </summary>
        [Fact]
        public void BrokerCullsIsolatedResourcesForMultiResourceConverter()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const double deltaTime = 1.0;

            const int primaryResourceId = 100;
            const int isolatedResourceId = 101;

            const double primaryProducedPerSecond = 10.0;
            const double isolatedProducedPerSecond = 5.0;
            const double consumedPerSecond = 6.0;   // required

            // ----- Producer: offers primary + isolated resources -----
            var producerContext = new DualResourceProducerContext(
                primaryResourceId: primaryResourceId,
                primaryAmountPerSecond: primaryProducedPerSecond,
                isolatedResourceId: isolatedResourceId,
                isolatedAmountPerSecond: isolatedProducedPerSecond);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Consumer: requires only the primary resource -----
            var consumerContext = new TestConverterContext(
                resourceId: primaryResourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both converters with the broker
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Consumer assertions (primary resource should be brokered and satisfied)
            //
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(consumedPerSecond * deltaTime, consumerReport.AmountGrantedPerTick, 6);

            //
            // Producer assertions:
            // - One brokered producer report for primary resource
            // - One unbrokered producer report for isolated resource
            //
            Assert.NotNull(producerContext.LastResults);

            Assert.Equal(1, producerContext.LastResults.BrokeredProducerReports.Count);
            Assert.Equal(1, producerContext.LastResults.UnbrokeredProducerReports.Count);

            var brokeredProducerReport = producerContext.LastResults.BrokeredProducerReports[0];
            var unbrokeredProducerReport = producerContext.LastResults.UnbrokeredProducerReports[0];

            // Primary (shared) resource 100: brokered and partially used according to demand.
            Assert.True(brokeredProducerReport.IsBrokered);
            Assert.Equal(primaryResourceId, brokeredProducerReport.ResourceId);
            Assert.Equal(consumedPerSecond * deltaTime, brokeredProducerReport.AmountUsedPerTick, 6);

            // Isolated resource 101: culled and unbrokered, unused by the broker.
            Assert.False(unbrokeredProducerReport.IsBrokered);
            Assert.Equal(isolatedResourceId, unbrokeredProducerReport.ResourceId);
            Assert.Equal(0.0, unbrokeredProducerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that a producer whose resource is not consumed by any other converter
        /// is treated as unbrokered: its resource reports are marked IsBrokered = false and
        /// the broker does not use any of its offered amount.
        ///
        /// Scenario:
        /// - Single producer offers 10 units/sec of resource 200
        /// - No consumers exist for resource 200
        /// - deltaTime = 1 sec
        ///
        /// Expected:
        /// - Producer has no brokered producer reports
        /// - Producer has exactly one unbrokered producer report
        /// - Unbrokered report has IsBrokered == false
        /// - Unbrokered report's AmountUsedPerTick == 0
        /// </summary>
        [Fact]
        public void ProducerWithoutMatchingConsumerIsUnbrokered()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 200;
            const double producedPerSecond = 10.0;
            const double deltaTime = 1.0;

            // Producer-only setup: no consumers for this resource.
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // Register only the producer with the broker.
            broker.RegisterResourceConverter(producerConverter);

            // Act
            broker.RunConverters(deltaTime);

            // Assert
            Assert.NotNull(producerContext.LastResults);

            // No brokered producers when nobody wants the resource.
            Assert.Empty(producerContext.LastResults.BrokeredProducerReports);

            // Exactly one unbrokered producer report.
            Assert.Single(producerContext.LastResults.UnbrokeredProducerReports);

            var unbrokeredProducerReport = producerContext.LastResults.UnbrokeredProducerReports[0];

            // Broker does not manage this report.
            Assert.False(unbrokeredProducerReport.IsBrokered);

            // No production should be recorded as "used" by the broker.
            Assert.Equal(0.0, unbrokeredProducerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when a producer offers zero units of a resource but a required consumer
        /// requests a nonzero amount, both sides are still treated as brokered (they share a resource),
        /// but no amounts are granted or used.
        /// 
        /// Scenario:
        /// - Producer offers 0 units/sec of resource 300
        /// - Required consumer requests 5 units/sec of resource 300
        /// - deltaTime = 1 sec
        ///
        /// Expected:
        /// - Both producer and consumer have brokered reports for resource 300
        /// - Consumer's AmountGrantedPerTick == 0
        /// - Producer's AmountUsedPerTick == 0
        /// </summary>
        [Fact]
        public void ZeroProductionWithRequiredConsumerResultsInNoTransferButStillBrokered()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 300;
            const double producedPerSecond = 0.0;
            const double consumedPerSecond = 5.0;
            const double deltaTime = 1.0;

            // Producer offers 0 units/sec of the resource.
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // Required consumer requests 5 units/sec.
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both converters with the broker.
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Consumer: should be brokered, but granted 0.
            //
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(0.0, consumerReport.AmountGrantedPerTick, 6);

            //
            // Producer: should be brokered, but used 0.
            //
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(0.0, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when a producer offers a nonzero amount of a resource but a required consumer
        /// requests zero units, both sides are still treated as brokered (they share a resource),
        /// but no amounts are granted or used.
        /// 
        /// Scenario:
        /// - Producer offers 10 units/sec of resource 301
        /// - Required consumer requests 0 units/sec of resource 301
        /// - deltaTime = 1 sec
        ///
        /// Expected:
        /// - Both producer and consumer have brokered reports for resource 301
        /// - Consumer's AmountGrantedPerTick == 0
        /// - Producer's AmountUsedPerTick == 0
        /// </summary>
        [Fact]
        public void ZeroDemandWithProducerResultsInNoTransferButStillBrokered()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 301;
            const double producedPerSecond = 10.0;
            const double consumedPerSecond = 0.0;
            const double deltaTime = 1.0;

            // Producer offers 10 units/sec of the resource.
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // Required consumer requests 0 units/sec.
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both converters with the broker.
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Consumer: should be brokered, but granted 0.
            //
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(0.0, consumerReport.AmountGrantedPerTick, 6);

            //
            // Producer: should be brokered, but used 0.
            //
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(0.0, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when deltaTime is zero, producers and consumers that share a resource
        /// are still treated as brokered, but no transfer occurs for the current tick.
        /// 
        /// Scenario:
        /// - Producer offers 10 units/sec of resource 302
        /// - Required consumer requests 10 units/sec of resource 302
        /// - deltaTime = 0
        ///
        /// Expected:
        /// - Both producer and consumer have brokered reports for resource 302
        /// - Consumer's AmountGrantedPerTick == 0
        /// - Producer's AmountUsedPerTick == 0
        /// </summary>
        [Fact]
        public void ZeroDeltaTimeProducesNoTransferButMaintainsBrokeredConnections()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 302;
            const double producedPerSecond = 10.0;
            const double consumedPerSecond = 10.0;
            const double deltaTime = 0.0;

            // Producer offers 10 units/sec.
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // Required consumer requests 10 units/sec.
            var consumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: consumedPerSecond,
                isProducer: false,
                isOptional: false);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both converters.
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Consumer: brokered, but granted 0.
            //
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            var consumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(0.0, consumerReport.AmountGrantedPerTick, 6);

            //
            // Producer: brokered, but used 0.
            //
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(0.0, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when there is no required demand and only optional consumers,
        /// the broker distributes production proportionally among optional consumers,
        /// and the producer's used amount matches the total granted to these consumers.
        ///
        /// Scenario:
        /// - Producer offers 10 units/sec of resource 303
        /// - Optional consumer A requests 10 units/sec
        /// - Optional consumer B requests 10 units/sec
        /// - deltaTime = 1 sec
        ///
        /// Totals:
        /// - totalProduced = 10
        /// - totalRequired = 0
        /// - totalOptional = 20
        /// - optionalSatisfactionRatio = 10 / 20 = 0.5
        ///
        /// Expected:
        /// - Each optional consumer is brokered and granted 5 units
        /// - Producer is brokered and uses all 10 units
        /// </summary>
        [Fact]
        public void OptionalOnlyConsumersShareProductionProportionally()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 303;
            const double producedPerSecond = 10.0;
            const double optionalConsumedPerSecond = 10.0;
            const double deltaTime = 1.0;

            // ----- Producer -----
            var producerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producedPerSecond,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Optional consumer A -----
            var optionalContextA = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: optionalConsumedPerSecond,
                isProducer: false,
                isOptional: true);

            var optionalConverterA = new OmniResourceConverter();
            optionalConverterA.Initialize(broker, optionalContextA);
            optionalContextA.ConverterId = optionalConverterA.Id;

            // ----- Optional consumer B -----
            var optionalContextB = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: optionalConsumedPerSecond,
                isProducer: false,
                isOptional: true);

            var optionalConverterB = new OmniResourceConverter();
            optionalConverterB.Initialize(broker, optionalContextB);
            optionalContextB.ConverterId = optionalConverterB.Id;

            // Register all converters with the broker.
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(optionalConverterA);
            broker.RegisterResourceConverter(optionalConverterB);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Optional consumer A: brokered, 5 units granted.
            //
            Assert.NotNull(optionalContextA.LastResults);
            Assert.Single(optionalContextA.LastResults.BrokeredConsumerReports);
            Assert.Empty(optionalContextA.LastResults.UnbrokeredConsumerReports);

            var optionalReportA = optionalContextA.LastResults.BrokeredConsumerReports[0];
            Assert.True(optionalReportA.IsBrokered);
            Assert.Equal(5.0, optionalReportA.AmountGrantedPerTick, 6);

            //
            // Optional consumer B: brokered, 5 units granted.
            //
            Assert.NotNull(optionalContextB.LastResults);
            Assert.Single(optionalContextB.LastResults.BrokeredConsumerReports);
            Assert.Empty(optionalContextB.LastResults.UnbrokeredConsumerReports);

            var optionalReportB = optionalContextB.LastResults.BrokeredConsumerReports[0];
            Assert.True(optionalReportB.IsBrokered);
            Assert.Equal(5.0, optionalReportB.AmountGrantedPerTick, 6);

            //
            // Producer: brokered, uses all 10 units.
            //
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(10.0, producerReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that with two producers and a single optional consumer that has finite storage,
        /// the consumer fills its storage over multiple ticks and then stops consuming the resource,
        /// causing both producers to become unbrokered after a ledger refresh.
        /// 
        /// Scenario:
        /// - Producer A offers 6 units/sec of resource 400
        /// - Producer B offers 4 units/sec of resource 400
        /// - Optional consumer:
        ///     - Max fill rate: 10 units/sec
        ///     - Capacity: 12 units
        /// - deltaTime = 1 sec
        ///
        /// Tick 1:
        /// - totalProduced = 10, totalOptional = 10
        /// - optionalSatisfactionRatio = 1.0
        /// - consumer granted 10, tank = 10/12
        ///
        /// Tick 2:
        /// - consumer still requests 10 units/sec (no refresh yet)
        /// - totalProduced = 10, totalOptional = 10
        /// - optionalSatisfactionRatio = 1.0
        /// - consumer granted another 10, but internal storage clamps at 12/12
        ///
        /// After Tick 2:
        /// - consumer is full and sets NeedsRefresh = true
        ///
        /// Tick 3 (after ledger rebuild):
        /// - consumer registers no reports (tank is full)
        /// - both producers now have no matching consumer and become unbrokered
        /// - no amounts are used
        /// </summary>
        [Fact]
        public void OptionalConsumerWithFiniteStorageFillsThenStopsConsuming()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 400;
            const double producerRateA = 6.0;
            const double producerRateB = 4.0;
            const double consumerMaxRate = 10.0;
            const double consumerCapacity = 12.0;
            const double deltaTime = 1.0;

            // ----- Producer A -----
            var producerContextA = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producerRateA,
                isProducer: true);

            var producerConverterA = new OmniResourceConverter();
            producerConverterA.Initialize(broker, producerContextA);
            producerContextA.ConverterId = producerConverterA.Id;

            // ----- Producer B -----
            var producerContextB = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: producerRateB,
                isProducer: true);

            var producerConverterB = new OmniResourceConverter();
            producerConverterB.Initialize(broker, producerContextB);
            producerContextB.ConverterId = producerConverterB.Id;

            // ----- Optional consumer with finite storage -----
            var consumerContext = new FiniteCapacityOptionalConsumerContext(
                resourceId: resourceId,
                maxFillRatePerSecond: consumerMaxRate,
                capacity: consumerCapacity);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register converters with the broker.
            broker.RegisterResourceConverter(producerConverterA);
            broker.RegisterResourceConverter(producerConverterB);
            broker.RegisterResourceConverter(consumerConverter);

            //
            // Tick 1: tank starts empty, consumer requests 10/sec, gets 10.
            //
            broker.RunConverters(deltaTime);

            Assert.Equal(10.0, consumerContext.CurrentAmount, 6);
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            var tick1ConsumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.Equal(10.0, tick1ConsumerReport.AmountGrantedPerTick, 6);

            //
            // Tick 2: tank at 10/12, consumer still requests 10/sec (no refresh yet).
            // Broker grants another 10, but the context clamps to capacity (12).
            //
            broker.RunConverters(deltaTime);

            Assert.Equal(12.0, consumerContext.CurrentAmount, 6); // clamped to capacity
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            var tick2ConsumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.Equal(10.0, tick2ConsumerReport.AmountGrantedPerTick, 6);

            //
            // Now that the tank is full, we signal that the converter no longer consumes
            // this resource by setting NeedsRefresh. On the next RunConverters, the ledger
            // is rebuilt and the consumer registers no reports.
            //
            consumerConverter.NeedsRefresh = true;

            //
            // Tick 3: after refresh, consumer registers no reports. Producers are unbrokered.
            //
            broker.RunConverters(deltaTime);

            // Consumer: no longer participates for this resource.
            Assert.NotNull(consumerContext.LastResults);
            Assert.Empty(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Empty(consumerContext.LastResults.UnbrokeredConsumerReports);

            // Producer A: unbrokered, 0 used.
            Assert.NotNull(producerContextA.LastResults);
            Assert.Empty(producerContextA.LastResults.BrokeredProducerReports);
            Assert.Single(producerContextA.LastResults.UnbrokeredProducerReports);
            var tick3ProducerAReport = producerContextA.LastResults.UnbrokeredProducerReports[0];
            Assert.False(tick3ProducerAReport.IsBrokered);
            Assert.Equal(0.0, tick3ProducerAReport.AmountUsedPerTick, 6);

            // Producer B: unbrokered, 0 used.
            Assert.NotNull(producerContextB.LastResults);
            Assert.Empty(producerContextB.LastResults.BrokeredProducerReports);
            Assert.Single(producerContextB.LastResults.UnbrokeredProducerReports);
            var tick3ProducerBReport = producerContextB.LastResults.UnbrokeredProducerReports[0];
            Assert.False(tick3ProducerBReport.IsBrokered);
            Assert.Equal(0.0, tick3ProducerBReport.AmountUsedPerTick, 6);
        }

        /// <summary>
        /// Verifies that when a single producer shuts off after running for several ticks,
        /// an optional finite-capacity consumer stops receiving resources from the broker,
        /// and the consumer's reports become unbrokered while the producer contributes no reports.
        ///
        /// Scenario:
        /// - Producer offers 1 unit/sec of resource 500
        /// - Optional consumer:
        ///     - Max fill rate: 10 units/sec
        ///     - Capacity: 10 units
        /// - deltaTime = 1 sec
        ///
        /// Ticks 1–3:
        /// - Producer enabled, consumer not full
        /// - Consumer receives 1 unit per tick, ending at 3 units total
        ///
        /// After Tick 3:
        /// - Producer is disabled (IsEnabled = false)
        /// - Producer converter sets NeedsRefresh = true
        ///
        /// Tick 4:
        /// - Producer registers no reports
        /// - Consumer still wants the resource and registers a consumer report
        /// - Broker finds consumers but no producers for resource 500 and unbrokers the consumer
        /// - Consumer's AmountGrantedPerTick == 0 and storage remains at 3 units
        /// </summary>
        [Fact]
        public void OptionalConsumerStopsReceivingWhenProducerShutsOffAfterSeveralTicks()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const int resourceId = 500;
            const double producerRate = 1.0;
            const double consumerMaxRate = 10.0;
            const double consumerCapacity = 10.0;
            const double deltaTime = 1.0;

            // ----- Switchable producer -----
            var producerContext = new SwitchableProducerContext(
                resourceId: resourceId,
                amountPerSecond: producerRate);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // ----- Optional finite-capacity consumer -----
            var consumerContext = new FiniteCapacityOptionalConsumerContext(
                resourceId: resourceId,
                maxFillRatePerSecond: consumerMaxRate,
                capacity: consumerCapacity);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both with the broker.
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            //
            // Tick 1: consumer gets 1 unit, total = 1.
            //
            broker.RunConverters(deltaTime);
            Assert.Equal(1.0, consumerContext.CurrentAmount, 6);
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            var tick1ConsumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.Equal(1.0, tick1ConsumerReport.AmountGrantedPerTick, 6);

            //
            // Tick 2: consumer gets another 1 unit, total = 2.
            //
            broker.RunConverters(deltaTime);
            Assert.Equal(2.0, consumerContext.CurrentAmount, 6);
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            var tick2ConsumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.Equal(1.0, tick2ConsumerReport.AmountGrantedPerTick, 6);

            //
            // Tick 3: consumer gets another 1 unit, total = 3.
            //
            broker.RunConverters(deltaTime);
            Assert.Equal(3.0, consumerContext.CurrentAmount, 6);
            Assert.NotNull(consumerContext.LastResults);
            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            var tick3ConsumerReport = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.Equal(1.0, tick3ConsumerReport.AmountGrantedPerTick, 6);

            //
            // Producer shuts off after 3 seconds; request a ledger refresh.
            //
            producerContext.IsEnabled = false;
            producerConverter.NeedsRefresh = true;

            //
            // Tick 4: broker rebuilds ledger.
            // - Producer registers no reports.
            // - Consumer registers an optional consumer report for resource 500.
            // - Broker finds only consumers (no producers) and unbrokers them.
            //
            broker.RunConverters(deltaTime);

            // Producer: no reports at all this tick.
            Assert.NotNull(producerContext.LastResults);
            Assert.Empty(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            // Consumer: one unbrokered consumer report, no brokered reports.
            Assert.NotNull(consumerContext.LastResults);
            Assert.Empty(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Single(consumerContext.LastResults.UnbrokeredConsumerReports);

            var tick4ConsumerReport = consumerContext.LastResults.UnbrokeredConsumerReports[0];
            Assert.False(tick4ConsumerReport.IsBrokered);
            Assert.Equal(0.0, tick4ConsumerReport.AmountGrantedPerTick, 6);

            // Storage should remain at 3 units (no change on Tick 4).
            Assert.Equal(3.0, consumerContext.CurrentAmount, 6);
        }

        /// <summary>
        /// Verifies that a consumer requesting *two* resources — one that is brokered
        /// (because a matching producer exists) and one that is isolated (no producers) —
        /// receives the correct split of ConversionResults:
        ///
        /// • The shared primary resource should produce a brokered consumer report,
        ///   with AmountGrantedPerTick equal to its requested amount.
        ///
        /// • The isolated resource should produce an unbrokered consumer report,
        ///   with AmountGrantedPerTick = 0 and IsBrokered = false.
        ///
        /// This test mirrors the existing DualResourceProducer test, but exercises the
        /// consumer-side glue layer to ensure it distributes reports to the correct
        /// brokered/unbrokered lists.
        /// </summary>
        [Fact]
        public void DualResourceConsumerGetsBrokeredAndUnbrokeredConsumerResults()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const double deltaTime = 1.0;

            const int primaryResourceId = 2000;
            const int isolatedResourceId = 2001;

            const double producerRate = 10.0;
            const double primaryConsumerRate = 6.0;
            const double isolatedConsumerRate = 4.0;

            // Producer for the primary resource
            var producerContext = new TestConverterContext(
                resourceId: primaryResourceId,
                amountPerSecond: producerRate,
                isProducer: true);

            var producerConverter = new OmniResourceConverter();
            producerConverter.Initialize(broker, producerContext);
            producerContext.ConverterId = producerConverter.Id;

            // Consumer requesting one shared (brokerable) and one isolated resource
            var consumerContext = new DualResourceConsumerContext(
                primaryResourceId: primaryResourceId,
                primaryAmountPerSecond: primaryConsumerRate,
                isolatedResourceId: isolatedResourceId,
                isolatedAmountPerSecond: isolatedConsumerRate);

            var consumerConverter = new OmniResourceConverter();
            consumerConverter.Initialize(broker, consumerContext);
            consumerContext.ConverterId = consumerConverter.Id;

            // Register both converters with the broker
            broker.RegisterResourceConverter(producerConverter);
            broker.RegisterResourceConverter(consumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            // Assert: producer results
            Assert.NotNull(producerContext.LastResults);
            Assert.Single(producerContext.LastResults.BrokeredProducerReports);
            Assert.Empty(producerContext.LastResults.UnbrokeredProducerReports);

            var producerReport = producerContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(primaryConsumerRate * deltaTime, producerReport.AmountUsedPerTick, 6);

            // Assert: consumer results (brokered + unbrokered)
            Assert.NotNull(consumerContext.LastResults);

            Assert.Single(consumerContext.LastResults.BrokeredConsumerReports);
            Assert.Single(consumerContext.LastResults.UnbrokeredConsumerReports);

            var brokeredConsumer = consumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(brokeredConsumer.IsBrokered);
            Assert.Equal(primaryResourceId, brokeredConsumer.ResourceId);
            Assert.Equal(primaryConsumerRate * deltaTime, brokeredConsumer.AmountGrantedPerTick, 6);

            var unbrokeredConsumer = consumerContext.LastResults.UnbrokeredConsumerReports[0];
            Assert.False(unbrokeredConsumer.IsBrokered);
            Assert.Equal(isolatedResourceId, unbrokeredConsumer.ResourceId);
            Assert.Equal(0.0, unbrokeredConsumer.AmountGrantedPerTick, 6);
        }

        /// <summary>
        /// Confirms that a *single* converter acting as both producer and consumer of
        /// the *same resource* is treated as isolated when no other converters touch
        /// that resource.
        ///
        /// The broker should classify the resource as "unbrokered" because there is
        /// only one endpoint in the graph. This means:
        ///
        /// • No broker participation
        /// • Producer report goes to UnbrokeredProducerReports
        /// • Consumer report goes to UnbrokeredConsumerReports
        ///
        /// This test guarantees that multi-role converters do not accidentally trick
        /// the broker into thinking "producer + consumer = 2 endpoints."
        /// </summary>
        [Fact]
        public void SingleMultiRoleConverterForSameResourceIsUnbrokeredWhenIsolated()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const double deltaTime = 1.0;

            const int resourceId = 2100;
            const double producedPerSecond = 10.0;
            const double consumedPerSecond = 5.0;

            var context = new MultiRoleProducerConsumerContext(
                producedResourceId: resourceId,
                producedPerSecond: producedPerSecond,
                consumedResourceId: resourceId,
                consumedPerSecond: consumedPerSecond);

            var converter = new OmniResourceConverter();
            converter.Initialize(broker, context);
            context.ConverterId = converter.Id;

            broker.RegisterResourceConverter(converter);

            // Act
            broker.RunConverters(deltaTime);

            // Assert: everything should be unbrokered
            Assert.NotNull(context.LastResults);

            Assert.Empty(context.LastResults.BrokeredProducerReports);
            Assert.Empty(context.LastResults.BrokeredConsumerReports);

            Assert.Single(context.LastResults.UnbrokeredProducerReports);
            Assert.Single(context.LastResults.UnbrokeredConsumerReports);

            var unbrokeredProducer = context.LastResults.UnbrokeredProducerReports[0];
            Assert.False(unbrokeredProducer.IsBrokered);
            Assert.Equal(0.0, unbrokeredProducer.AmountUsedPerTick, 6);

            var unbrokeredConsumer = context.LastResults.UnbrokeredConsumerReports[0];
            Assert.False(unbrokeredConsumer.IsBrokered);
            Assert.Equal(0.0, unbrokeredConsumer.AmountGrantedPerTick, 6);
        }

        /// <summary>
        /// Ensures that a converter which both produces and consumes a resource becomes
        /// *fully brokered* when another converter also touches that same resource.
        ///
        /// The multi-role converter should receive:
        /// • 1 brokered producer report (its production is used)
        /// • 1 brokered consumer report (IsBrokered = true, even if requesting zero)
        ///
        /// The external consumer should receive:
        /// • A brokered consumer report with full allocation
        ///
        /// This test verifies that the glue layer correctly classifies *both roles* of
        /// a multi-role converter once the resource is no longer isolated.
        /// </summary>
        [Fact]
        public void MultiRoleConverterIsBrokeredWhenSharingResourceWithAnotherConverter()
        {
            // Arrange
            var broker = new OmniResourceBroker();
            const double deltaTime = 1.0;

            const int resourceId = 2200;
            const double producedPerSecond = 10.0;

            // Multi-role converter: produces 10/sec, consumes 0/sec
            var multiRoleContext = new MultiRoleProducerConsumerContext(
                producedResourceId: resourceId,
                producedPerSecond: producedPerSecond,
                consumedResourceId: resourceId,
                consumedPerSecond: 0.0);

            var multiRoleConverter = new OmniResourceConverter();
            multiRoleConverter.Initialize(broker, multiRoleContext);
            multiRoleContext.ConverterId = multiRoleConverter.Id;

            // Other consumer: demands 10/sec
            const double externalConsumerRate = 10.0;
            var externalConsumerContext = new TestConverterContext(
                resourceId: resourceId,
                amountPerSecond: externalConsumerRate,
                isProducer: false);

            var externalConsumerConverter = new OmniResourceConverter();
            externalConsumerConverter.Initialize(broker, externalConsumerContext);
            externalConsumerContext.ConverterId = externalConsumerConverter.Id;

            broker.RegisterResourceConverter(multiRoleConverter);
            broker.RegisterResourceConverter(externalConsumerConverter);

            // Act
            broker.RunConverters(deltaTime);

            //
            // Assert: multi-role converter should be fully brokered
            //
            Assert.NotNull(multiRoleContext.LastResults);

            Assert.Single(multiRoleContext.LastResults.BrokeredProducerReports);
            Assert.Single(multiRoleContext.LastResults.BrokeredConsumerReports);

            Assert.Empty(multiRoleContext.LastResults.UnbrokeredProducerReports);
            Assert.Empty(multiRoleContext.LastResults.UnbrokeredConsumerReports);

            var producerReport = multiRoleContext.LastResults.BrokeredProducerReports[0];
            Assert.True(producerReport.IsBrokered);
            Assert.Equal(producedPerSecond * deltaTime, producerReport.AmountUsedPerTick, 6);

            var consumerReport = multiRoleContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(consumerReport.IsBrokered);
            Assert.Equal(0.0, consumerReport.AmountGrantedPerTick, 6);

            //
            // Assert: external consumer fully satisfied and brokered
            //
            Assert.NotNull(externalConsumerContext.LastResults);
            Assert.Single(externalConsumerContext.LastResults.BrokeredConsumerReports);

            var externalReport = externalConsumerContext.LastResults.BrokeredConsumerReports[0];
            Assert.True(externalReport.IsBrokered);
            Assert.Equal(externalConsumerRate * deltaTime, externalReport.AmountGrantedPerTick, 6);
        }
    }
}
