using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OmniWorks.Core
{
    /// <summary>
    /// Standard glue layer between the OmniResourceBroker and a converter’s
    /// context host (e.g., a KSP part, Unity component, or test harness).
    ///
    /// PURPOSE:
    ///     Acts as the adapter that:
    ///         1. Coordinates when its reports need to be rebuilt
    ///         2. Exposes those reports to the broker during ledger refresh
    ///         3. Receives metered results from the broker each tick
    ///         4. Forwards those results to the IOmniResourceConverterContext host
    ///
    /// WHAT IT MANAGES:
    ///     - A unique ConverterId (Guid)
    ///     - A reference to the IOmniResourceConverterContext
    ///     - A ConverterReportRegistry holding this converter's producer/consumer reports
    ///     - A ConversionResults object reused every tick
    ///     - A NeedsRefresh flag that tells the broker when the registry/ledger
    ///       must be rebuilt
    ///
    /// WHEN REPORTS ARE BUILT:
    ///     Reports are NOT rebuilt every tick.
    ///     Instead, the context (or owning game logic) sets NeedsRefresh = true
    ///     whenever something about this converter's resource behavior changes:
    ///         - converters turned on/off
    ///         - production/consumption rates changed
    ///         - resource sets or modes changed
    ///
    ///     When the broker performs a ledger refresh, it calls back into the
    ///     converter so it can:
    ///         - clear its registry
    ///         - ask the context to re-register all ProducerReport / ConsumerReport entries
    ///         - expose that updated registry to the broker
    ///
    /// WHAT HAPPENS EACH TICK:
    ///     - The broker uses its current ledger (built from previously-registered
    ///       reports) to meter resource flow for all converters.
    ///     - After metering, the broker calls the converter to apply results.
    ///     - The converter:
    ///         1. Updates its ConversionResults instance, splitting reports into
    ///            brokered vs. unbrokered groups.
    ///         2. Passes ConversionResults to the IOmniResourceConverterContext
    ///            via OnConversionResult (or equivalent).
    ///
    /// DESIGN GOAL:
    ///     Keep converters lightweight and event-driven:
    ///         - No per-tick report rebuilds
    ///         - No unnecessary allocations
    ///         - Clear separation between:
    ///             • game-specific logic (context/host)
    ///             • resource coordination math (broker)
    ///             • adapter glue (this converter class)
    /// </summary>
    public class OmniResourceConverter : IOmniResourceConverter
    {
        #region Housekeeping
        /// <summary>
        /// Unique identifier of the converter.
        /// </summary>
        protected Guid converterId = Guid.NewGuid();

        /// <summary>
        /// Internal flag indicating that the broker and/or converter needs to be refreshed.
        /// </summary>
        protected bool needsRefresh = true;

        /// <summary>
        /// Reference to the host context
        /// </summary>
        protected WeakReference<IOmniResourceConverterContext> hostContextRef;

        /// <summary>
        /// The resource broker that will do all the bookkeeping
        /// </summary>
        protected IOmniResourceBroker resourceBroker = null;

        /// <summary>
        /// The registry of resource reports that the converter manages
        /// </summary>
        protected readonly ConverterReportRegistry reportRegistry = new ConverterReportRegistry();

        /// <summary>
        /// The latest results of the resource conversion efforts
        /// </summary>
        protected readonly ConversionResults conversionResults = new ConversionResults();
        #endregion

        #region IOmniResourceConverter
        /// <inheritdoc/>
        public virtual Guid Id { 
            get
            {
                return converterId;
            }

            set
            {
                converterId = value;
            }
        }

        /// <inheritdoc/>
        public virtual bool NeedsRefresh {
            set
            {
                if (resourceBroker != null)
                    resourceBroker.NeedsRefresh = value;

                needsRefresh = value;
            }
        }

        /// <inheritdoc/>
        public virtual void Initialize(IOmniResourceBroker broker, IOmniResourceConverterContext context)
        {
            resourceBroker = broker;
            hostContextRef = new WeakReference<IOmniResourceConverterContext>(context);
        }

        /// <inheritdoc/>
        public virtual void OnBrokerResult(double deltaTime)
        {
            conversionResults.Clear();

            conversionResults.deltaTime = deltaTime;

            // Sort our consumer and producer reports into those that are brokered and those that aren't.
            foreach (var consumerReport in reportRegistry.consumerReports)
            {
                if (consumerReport.IsBrokered)
                    conversionResults.brokeredConsumerReports.Add(consumerReport);
                else
                    conversionResults.unbrokeredConsumerReports.Add(consumerReport);
            }

            foreach (var producerReport in reportRegistry.producerReports)
            {
                if (producerReport.IsBrokered)
                    conversionResults.brokeredProducerReports.Add(producerReport);
                else
                    conversionResults.unbrokeredProducerReports.Add(producerReport);
            }

            // Inform the context of the broker's results.
            if (hostContextRef.TryGetTarget(out IOmniResourceConverterContext context))
            {
                context.OnConversionResult(conversionResults);
            }
        }

        /// <inheritdoc/>
        public virtual void RegisterReports(List<ConsumerReport> consumerReports, List<ProducerReport> producerReports)
        {
            // Make the registry request its reports from the context host.
            reportRegistry.Clear();

            if (hostContextRef.TryGetTarget(out IOmniResourceConverterContext context))
            {
                context.RegisterReports(reportRegistry);
            }

            // Now copy them into the lists the broker expects.
            consumerReports.AddRange(reportRegistry.consumerReports);
            producerReports.AddRange(reportRegistry.producerReports);
        }
        #endregion
    }
}
