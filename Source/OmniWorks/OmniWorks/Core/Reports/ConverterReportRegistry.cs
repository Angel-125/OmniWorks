using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OmniWorks.Core
{
    /// <summary>
    /// A temporary container that a converter’s CONTEXT uses to describe all of its
    /// consumer and producer reports for a registration pass.
    /// 
    /// PURPOSE:
    ///     Acts as a glue object between the converter host (context) and the
    ///     OmniResourceBroker. The context fills it with ConsumerReport and
    ///     ProducerReport entries so the broker can build its internal graph.
    /// 
    /// HOW IT'S USED:
    ///     - The broker (or converter wrapper) creates a ConverterReportRegistry
    ///     - Calls IOmniResourceConverterContext.RegisterReports(registry)
    ///     - The context adds its ConsumerReport and ProducerReport entries to:
    ///           ConsumerReports
    ///           ProducerReports
    ///     - The broker reads these lists to build/update the ledger
    /// 
    /// NOTES:
    ///     - The lists are mutable so contexts can freely add/remove reports.
    ///     - Clear() is used internally to recycle a registry instance between ticks,
    ///       avoiding allocations.
    /// </summary>
    public sealed class ConverterReportRegistry
    {
        #region Housekeeping
        internal readonly List<ConsumerReport> consumerReports = new List<ConsumerReport>();
        internal readonly List<ProducerReport> producerReports = new List<ProducerReport>();
        #endregion

        /// <summary>
        /// Gets the list of consumer reports to register.
        /// The caller can add or remove items from this list.
        /// </summary>
        public IList<ConsumerReport> ConsumerReports => consumerReports;

        /// <summary>
        /// Gets the list of producer reports to register.
        /// The caller can add or remove items from this list.
        /// </summary>
        public IList<ProducerReport> ProducerReports => producerReports;

        /// <summary>
        /// Clears the registry
        /// </summary>
        public void Clear()
        {
            consumerReports.Clear();
            producerReports.Clear();
        }
    }
}
