using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OmniWorks.Core
{
    /// <summary>
    /// Per-converter report of what happened during a single brokered simulation tick.
    /// 
    /// PURPOSE:
    ///     Packages all brokered and unbrokered producer/consumer activity that the
    ///     OmniResourceBroker calculated for a specific converter, along with the
    ///     deltaTime used for that tick.
    /// 
    /// WHAT IT CONTAINS:
    ///     - BrokeredConsumerReports   → consumer reports that participated in a
    ///                                  resource network with at least one producer
    ///     - BrokeredProducerReports   → producer reports that actually supplied
    ///                                  resources through the broker
    ///     - UnbrokeredConsumerReports → consumer reports for isolated resources
    ///                                  (no matching producers; converter must
    ///                                   manage them itself)
    ///     - UnbrokeredProducerReports → producer reports for isolated resources
    ///                                  (no matching consumers; converter manages
    ///                                   them or they are unused)
    /// 
    /// HOW IT'S USED:
    ///     - Created and populated by the broker each tick
    ///     - Passed to the converter’s context host via OnConversionResult()
    ///     - Read-only from the context/converter perspective
    /// 
    /// NOTES:
    ///     - The same instance is typically reused each tick and cleared internally.
    ///     - deltaTime is stored so the host can relate per-tick values back to
    ///       per-second or per-frame expectations.
    /// </summary>
    public class ConversionResults
    {
        internal double deltaTime;

        // The list of resources are brokered by the Resource Broker. The Resource Broker will handle the resource conversions.
        // Brokered resources will exist when another converter handled by the Resource Broker would like the resource(s) that we produce, or provides the resource(s) that we consume.
        internal readonly List<ConsumerReport> brokeredConsumerReports = new List<ConsumerReport>();
        internal readonly List<ProducerReport> brokeredProducerReports = new List<ProducerReport>();

        // The list of resources that are NOT brokered by the Resource Broker. WE are responsible for handling their conversions.
        // Unbrokered resources will exist when no other converter handled by the Resource Broker would like the resource(s) that we produce, or provides the resource(s) that we consume.
        internal readonly List<ConsumerReport> unbrokeredConsumerReports = new List<ConsumerReport>();
        internal readonly List<ProducerReport> unbrokeredProducerReports = new List<ProducerReport>();

        /// <summary>
        /// The read-only list of consumer reports handled by the broker.
        /// </summary>
        public IReadOnlyList<ConsumerReport> BrokeredConsumerReports => brokeredConsumerReports;

        /// <summary>
        /// The read-only list of consumer reports NOT handled by the broker.
        /// </summary>
        public IReadOnlyList<ConsumerReport> UnbrokeredConsumerReports => unbrokeredConsumerReports;

        /// <summary>
        /// The read-only list of producer reports handled by the broker.
        /// </summary>
        public IReadOnlyList<ProducerReport> BrokeredProducerReports => brokeredProducerReports;

        /// <summary>
        /// The read-only list of producer reports NOT handled by the broker.
        /// </summary>
        public IReadOnlyList<ProducerReport> UnbrokeredProducerReports => unbrokeredProducerReports;

        /// <summary>
        /// Helper to clear the brokered and unbrokered reports
        /// </summary>
        internal void Clear()
        {
            brokeredConsumerReports.Clear();
            brokeredProducerReports.Clear();
            unbrokeredConsumerReports.Clear();
            unbrokeredProducerReports.Clear();
        }
    }
}
