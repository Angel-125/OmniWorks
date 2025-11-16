// Created 11/12/2025
// By Michael Billard (mgb125)
// License: GPL-V3

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OmniWorks.Core
{
    /// <summary>
    /// Central coordinator that connects all active resource converters, meters
    /// resource flow between them, and delivers per-converter results each tick.
    ///
    /// PURPOSE:
    ///     Implements the “net-metered” simulation model: instead of converters
    ///     individually pulling from or pushing into storage tanks, the broker
    ///     collects ALL consumer and producer reports for the current tick,
    ///     balances totals, and then distributes the net metered results back
    ///     to each converter in a single pass.
    ///
    /// WHAT THE BROKER DOES EACH TICK:
    ///     1. Refreshes the ledger (only when RefreshNeeded == true- set when converter toggled on/off, rates changed, modes switched, etc.):
    ///         • Rediscovers which converters touch which resources
    ///         • Removes isolated resources (producer-only or consumer-only)
    ///         • Marks remaining reports as brokered
    ///
    ///     2. Totals all production and consumption per resource:
    ///         • Required consumption
    ///         • Optional consumption
    ///         • Surplus
    ///
    ///     3. Computes ratios:
    ///         • satisfactionRatio          = min(1, produced / required)
    ///         • optionalSatisfactionRatio  = grantedOptional / totalOptional
    ///
    ///     4. Applies results to every report:
    ///         • consumer.AmountGrantedPerTick
    ///         • producer.AmountUsedPerTick
    ///
    ///     5. Calls OnBrokerResult on every converter, passing the results down
    ///        into the IOmniResourceConverterContext host.
    ///
    /// DESIGN RULES:
    ///     • A resource is only brokered when it has at least ONE producer AND
    ///       at least ONE consumer.
    ///     • If a resource is isolated or touched by only one converter, the
    ///       broker steps aside and marks those reports as unbrokered.
    ///     • The broker never directly manipulates storage; it only meters flow.
    ///
    /// OVERALL GOAL:
    ///     Provide consistent, predictable, cross-converter resource simulation
    ///     with minimal overhead and clean separation between logic (broker)
    ///     and game-specific hosting (contexts).
    /// </summary>
    public class OmniResourceBroker : IOmniResourceBroker
    {
        #region Housekeeping
        // Map of all the resource converters
        private readonly Dictionary<Guid, WeakReference<IOmniResourceConverter>> resourceConvertersById = new Dictionary<Guid, WeakReference<IOmniResourceConverter>>();

        // List of all known resources consumed or produced
        private readonly HashSet<int> knownResourceIds = new HashSet<int>();

        // For each resourceId, which converters (EndpointIds) touch it?
        private readonly Dictionary<int, HashSet<Guid>> convertersByResourceId = new Dictionary<int, HashSet<Guid>>();
        
        // Consumer and Producer reports
        private readonly Dictionary<int, List<ProducerReport>> producerReportsByResourceId = new Dictionary<int, List<ProducerReport>>();
        private readonly Dictionary<int, List<ConsumerReport>> consumerReportsByResourceId = new Dictionary<int, List<ConsumerReport>>();

        // Totals for each resource ID
        private readonly Dictionary<int, double> totalProducedByResourceId = new Dictionary<int, double>();
        private readonly Dictionary<int, double> totalRequiredConsumedByResourceId = new Dictionary<int, double>();
        private readonly Dictionary<int, double> totalOptionallyConsumedByResourceId = new Dictionary<int, double>();

        private bool needsRefresh = true;
        #endregion

        #region IOmniResourceBroker
        /// <inheritdoc/>
        public bool NeedsRefresh
        {
            get
            {
                return needsRefresh;
            }
            set
            {
                needsRefresh = value;
            }
        }

        /// <inheritdoc/>
        public bool RegisterResourceConverter(IOmniResourceConverter converter)
        {
            WeakReference<IOmniResourceConverter> converterReference = new WeakReference<IOmniResourceConverter>(converter);

            if (!resourceConvertersById.ContainsKey(converter.Id))
            {
                resourceConvertersById.Add(converter.Id, converterReference);

                List<ConsumerReport> consumerReports = new List<ConsumerReport>();
                List<ProducerReport> producerReports = new List<ProducerReport>();
                converter.RegisterReports(consumerReports, producerReports);
                registerReports(consumerReports, producerReports);

                needsRefresh = true;
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void UnregisterConverter(IOmniResourceConverter converter)
        {
            if (resourceConvertersById.ContainsKey(converter.Id))
            {
                resourceConvertersById.Remove(converter.Id);
                removeReports(converter.Id);
                needsRefresh = true;
            }
        }

        /// <inheritdoc/>
        public void UnregisterConverter(Guid converterId)
        {
            if (resourceConvertersById.ContainsKey(converterId))
            {
                resourceConvertersById.Remove(converterId);
                removeReports(converterId);
                needsRefresh = true;
            }
        }

        /// <inheritdoc/>
        public void BuildTotals(double deltaTime)
        {
            // ---- Produced totals ----
            int resourceId = 0;
            List<ProducerReport> producerReports = null;
            totalProducedByResourceId.Clear();
            foreach (var keyValuePair in producerReportsByResourceId)
            {
                double sum = 0;
                producerReports = keyValuePair.Value;
                resourceId = keyValuePair.Key;
                for (int i = 0; i < producerReports.Count; i++)
                    sum += producerReports[i].AmountOfferedPerSec * deltaTime;
                if (sum > 0)
                    totalProducedByResourceId[resourceId] = sum;
            }

            // ---- Consumed totals (required vs optional) ----
            List<ConsumerReport> consumerReports = null;
            totalRequiredConsumedByResourceId.Clear();
            totalOptionallyConsumedByResourceId.Clear();
            foreach (var keyValuePair in consumerReportsByResourceId)
            {
                double required = 0, optional = 0;
                consumerReports = keyValuePair.Value;
                resourceId = keyValuePair.Key;
                for (int i = 0; i < consumerReports.Count; i++)
                {
                    if (consumerReports[i].IsOptional)
                        optional += consumerReports[i].AmountRequestedPerSec;
                    else
                        required += consumerReports[i].AmountRequestedPerSec;
                }

                if (required > 0)
                    totalRequiredConsumedByResourceId[resourceId] = required * deltaTime;
                if (optional > 0)
                    totalOptionallyConsumedByResourceId[resourceId] = optional * deltaTime;
            }
        }

        public void RunConverters(double deltaTime)
        {
            refreshLedgerIfNeeded();

            BuildTotals(deltaTime);

            // Balance the ledger: We'll look at all the inputs and all the outputs, figure out the net change, and then tell converters to handle the results.
            // This avoids unnecessary trips through the vessel's resource tree as individual converters add and remove resources to various storage tanks.
            // In fact, if all intermediate resources are balanced from their production and consumption, they won't ever even touch a storage tank.
            // Once we know the finall tally of resource amounts, only then do we tell individual converters to update their resources.
            foreach (var resourceId in knownResourceIds)
            {
                // Step 1: Calculate the global values for resources produced, consumed, optionally consumed, the surplus, the optional leftovers served, and the satisfaction and optional satisfaction ratios.
                if (!totalProducedByResourceId.TryGetValue(resourceId, out double totalProduced))
                    totalProduced = 0.0;

                if (!totalRequiredConsumedByResourceId.TryGetValue(resourceId, out double totalRequired))
                    totalRequired = 0.0;

                // If there's a surplus produced, how much can we take?
                if (!totalOptionallyConsumedByResourceId.TryGetValue(resourceId, out double totalOptionallyConsumed))
                    totalOptionallyConsumed = 0.0;

                // If we've produced more than we need, then there's a surplus.
                double totalSurplus = Math.Max(0.0, totalProduced - totalRequired);

                // We know how much we would like to consume (totalOptionallyConsumed), and how much surplus we have (totalSurplus), now, how much surplus can we actually provide to the optional consumers?
                // Ex: We have 10 units of surplus, but we can only take 8 units, then the total units that we can provide to optional consumers is 8 units.
                // Ex: We have 8 units of surplus but we can take up to 10 units, then the total units that we can provide to optional consumers is 8 units.
                double totalOptionallyGranted = Math.Min(totalOptionallyConsumed, totalSurplus);

                // Figure out how much we can satisfy
                double satisfactionRatio = (totalRequired <= 0) ? 1.0 : Math.Min(1.0, totalProduced / totalRequired);

                // Optional ratio for each optional consumer.
                double optionalSatisfactionRatio = (totalOptionallyConsumed > 0.0) ? (totalOptionallyGranted / totalOptionallyConsumed) : 0.0;

                // Step 2: Computes per-consumer grantedAmount, per-producer usedAmount

                // ----- Apply per-consumer results for this resource -----
                if (consumerReportsByResourceId.TryGetValue(resourceId, out var consumerReports))
                {
                    double requestedThisTick;
                    double grantedThisTick;
                    foreach (var report in consumerReports)
                    {
                        requestedThisTick = report.AmountRequestedPerSec * deltaTime;
                        grantedThisTick = report.IsOptional ? requestedThisTick * optionalSatisfactionRatio : requestedThisTick * satisfactionRatio;

                        report.AmountGrantedPerTick = grantedThisTick;
                    }
                }

                // ----- Apply per-producer results for this resource -----
                // How much required is actually served this tick?
                double totalRequiredServed = totalRequired * satisfactionRatio;
                if (producerReportsByResourceId.TryGetValue(resourceId, out var producerReports))
                {
                    double totalUsed = totalRequiredServed + totalOptionallyGranted;

                    double producerUsageRatio = (totalProduced > 0.0) ? (totalUsed / totalProduced) : 0.0;

                    double offeredThisTick;
                    double usedThisTick;
                    foreach (var report in producerReports)
                    {
                        offeredThisTick = report.AmountOfferedPerSec * deltaTime;
                        usedThisTick = offeredThisTick * producerUsageRatio;

                        report.AmountUsedPerTick = usedThisTick;
                    }
                }
            }

            // Step 3: Inform all the converters that we've completed the calculations.
            // NOTE: each converter will need to keep track of the reports that they provide.
            // These reports will contain the AmountGranted (consumer) and AmountUsed (producer)
            foreach (var converterRef in resourceConvertersById.Values)
            {
                if (converterRef.TryGetTarget(out var converter))
                {
                    converter.OnBrokerResult(deltaTime);
                }
            }
        }
        #endregion

        #region Helpers
        void removeReports(Guid converterId)
        {
            foreach (var keyValuePair in producerReportsByResourceId.ToList())
            {
                var updatedList = keyValuePair.Value
                    .Where(r => r.EndpointId != converterId)
                    .ToList();

                if (updatedList.Count == 0)
                {
                    producerReportsByResourceId.Remove(keyValuePair.Key);
                }
                else
                {
                    producerReportsByResourceId[keyValuePair.Key] = updatedList;
                }
            }


            foreach (var keyValuePair in consumerReportsByResourceId.ToList())
            {
                var updatedList = keyValuePair.Value
                    .Where(r => r.EndpointId != converterId)
                    .ToList();

                if (updatedList.Count == 0)
                {
                    consumerReportsByResourceId.Remove(keyValuePair.Key);
                }
                else
                {
                    consumerReportsByResourceId[keyValuePair.Key] = updatedList;
                }
            }
        }

        void registerReports(List<ConsumerReport> consumerReports, List<ProducerReport> producerReports)
        {
            // Add reports to our lists of consumer/producer reports by resourceID, and add the resources they consume/produce to our list of known resource IDs.
            foreach (var consumerReport in consumerReports)
            {
                if (!consumerReportsByResourceId.TryGetValue(consumerReport.ResourceId, out var registeredConsumerReports))
                {
                    registeredConsumerReports = new List<ConsumerReport>();
                    consumerReportsByResourceId.Add(consumerReport.ResourceId, registeredConsumerReports);
                }

                registeredConsumerReports.Add(consumerReport);

                knownResourceIds.Add(consumerReport.ResourceId);

                // Track per-resource which converters touch it
                // We'll need this information to know which converters aren't connected to other converters in their resource chains. An example would be a fuel tank of Aardvarks that no currently operating converter makes or consumes.
                if (!convertersByResourceId.TryGetValue(consumerReport.ResourceId, out var endpointIds))
                {
                    endpointIds = new HashSet<Guid>();
                    convertersByResourceId[consumerReport.ResourceId] = endpointIds;
                }

                endpointIds.Add(consumerReport.EndpointId);
            }

            foreach (var producerReport in producerReports)
            {
                if (!producerReportsByResourceId.TryGetValue(producerReport.ResourceId, out var registeredProducerReports))
                {
                    registeredProducerReports = new List<ProducerReport>();
                    producerReportsByResourceId.Add(producerReport.ResourceId, registeredProducerReports);
                }

                registeredProducerReports.Add(producerReport);

                knownResourceIds.Add(producerReport.ResourceId);

                // Track per-resource which converters touch it
                if (!convertersByResourceId.TryGetValue(producerReport.ResourceId, out var endpointIds))
                {
                    endpointIds = new HashSet<Guid>();
                    convertersByResourceId[producerReport.ResourceId] = endpointIds;
                }

                endpointIds.Add(producerReport.EndpointId);
            }
        }

        void refreshLedgerIfNeeded()
        {
            if (needsRefresh)
            {
                List<ConsumerReport> consumerReports = new List<ConsumerReport>();
                List<ProducerReport> producerReports = new List<ProducerReport>();
                IOmniResourceConverter converter = null;

                knownResourceIds.Clear();
                convertersByResourceId.Clear();
                consumerReportsByResourceId.Clear();
                producerReportsByResourceId.Clear();

                // Register all the consumer and producer reports from our known converters.
                foreach (var referenceConverter in resourceConvertersById.Values)
                {
                    if (referenceConverter.TryGetTarget(out converter))
                    {
                        converter.RegisterReports(consumerReports, producerReports);
                        registerReports(consumerReports, producerReports);

                        consumerReports.Clear();
                        producerReports.Clear();
                    }
                }

                // Now get rid of any resource reports that aren't connected to anything.
                cullIsolatedResources();

                // Everything left in the ledger is brokered by definition. Make sure the reports know that.
                foreach (var kvp in consumerReportsByResourceId)
                {
                    foreach (var report in kvp.Value)
                        report.IsBrokered = true;
                }

                foreach (var kvp in producerReportsByResourceId)
                {
                    foreach (var report in kvp.Value)
                        report.IsBrokered = true;
                }

                needsRefresh = false;
            }
        }

        void cullIsolatedResources()
        {
            List<ConsumerReport> consumerReports = null;
            List<ProducerReport> producerReports = null;

            foreach (var kvp in convertersByResourceId)
            {
                int resourceId = kvp.Key;
                HashSet<Guid> endpoints = kvp.Value;

                bool hasConsumers = consumerReportsByResourceId.ContainsKey(resourceId);
                bool hasProducers = producerReportsByResourceId.ContainsKey(resourceId);

                // A resource is only useful if it has at least 1 producer AND 1 consumer
                // The beauty of this is that if a tank has Ore and Aardvarks, and no other converter uses Aardvarks, then only the Aardvark reports will be removed, leaving the tanks' Ore reports intact.
                if (!hasConsumers || !hasProducers || endpoints.Count < 2)
                {
                    if (consumerReportsByResourceId.TryGetValue(resourceId, out consumerReports))
                    {
                        foreach (var consumerReport in consumerReports)
                            consumerReport.IsBrokered = false;

                        consumerReportsByResourceId.Remove(resourceId);
                    }

                    if (producerReportsByResourceId.TryGetValue(resourceId, out producerReports))
                    {
                        foreach (var producerReport in producerReports)
                            producerReport.IsBrokered = false;

                        producerReportsByResourceId.Remove(resourceId);
                    }

                    knownResourceIds.Remove(resourceId);
                }
            }
        }

        #endregion
    }
}
