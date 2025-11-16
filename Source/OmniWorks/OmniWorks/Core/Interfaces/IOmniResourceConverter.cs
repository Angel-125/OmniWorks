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
    /// Interface for a resource converter that participates in the OmniWorks
    /// event-driven resource system. Converters represent individual producers
    /// and/or consumers of resources, while delegating all inter-converter
    /// coordination and metering to an OmniResourceBroker.
    ///
    /// PURPOSE:
    ///     Acts as the adapter between:
    ///         • the IOmniResourceConverterContext (game-specific host logic)
    ///         • the OmniResourceBroker (which meters all resources)
    ///
    /// LIFECYCLE MODEL:
    ///     - Converters do NOT rebuild their reports every tick.
    ///     - Instead, the game or context sets NeedsRefresh = true whenever the
    ///       converter's resource behavior changes (on/off, rates, modes, etc.).
    ///
    ///     When the broker performs its next ledger refresh, it calls:
    ///         RegisterReports(consumerReports, producerReports)
    ///
    ///     At that moment, the converter:
    ///         1. Clears its internal registry
    ///         2. Asks the context to populate ConsumerReport / ProducerReport entries
    ///         3. Copies those entries into the lists provided by the broker
    ///
    ///     Between refreshes, the broker reuses the previously-registered reports
    ///     with zero allocations and zero churn.
    ///
    /// PER-TICK BEHAVIOR:
    ///     - The broker meters all resources using its current ledger
    ///       (previously built via a NeedsRefresh event).
    ///     - After metering, the broker calls:
    ///         OnBrokerResult(deltaTime)
    ///       so the converter can:
    ///         • split its reports into brokered/unbrokered
    ///         • update its ConversionResults
    ///         • forward that result to the host context
    ///
    /// DESIGN GOAL:
    ///     Provide a lightweight, zero-GC, event-driven connection between the
    ///     context and the broker, keeping converters minimal and efficient.
    /// </summary>
    public interface IOmniResourceConverter
    {
        /// <summary>
        /// A GUID containing the ID of the resource converter.
        /// </summary>
        Guid Id { get; set; }

        /// <summary>
        /// Flag to indicate that the converter needs a refresh.
        /// </summary>
        bool NeedsRefresh { set; }

        /// <summary>
        /// Initializes the converter
        /// </summary>
        /// <param name="broker">An IOmniResourceBroker that represents the resource broker.</param>
        /// <param name="context">An IOmniResourceConverterContext that represents the host of the converter.</param>
        void Initialize(IOmniResourceBroker broker, IOmniResourceConverterContext context);

        /// <summary>
        /// Called by the broker to obtain the converter's consumer and producer reports.
        /// </summary>
        /// <param name="consumerReports">List of ConsumerReport objects specifing resource inputs.</param>
        /// <param name="producerReports">List of ProducerReport objects specifying resource outputs.</param>
        void RegisterReports(List<ConsumerReport> consumerReports, List<ProducerReport> producerReports);

        /// <summary>
        /// Called when the broker has completed its net-metered assessments.
        /// </summary>
        /// <param name="deltaTime">A double containing the amount of time that has passed.</param>
        void OnBrokerResult(double deltaTime);
    }
}
