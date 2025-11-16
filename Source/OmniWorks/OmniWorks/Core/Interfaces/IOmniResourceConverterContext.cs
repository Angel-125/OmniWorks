using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OmniWorks.Core
{
    /// <summary>
    /// Describes the environment in which a resource converter lives and how it
    /// communicates with its host (e.g., a KSP part, vessel component, or game object).
    /// 
    /// PURPOSE:
    ///     Separates broker/converter logic from game-specific plumbing. The context
    ///     knows about the host (part, component, fields), while the broker only
    ///     understands reports and IDs.
    /// 
    /// RESPONSIBILITIES:
    ///     - RegisterReports:
    ///         The host describes ALL resources it wants to consume and produce
    ///         by populating a ConverterReportRegistry. This is how the converter
    ///         enters the broker’s resource graph.
    /// 
    ///     - OnConversionResult:
    ///         The host receives a ConversionResults object after the broker finishes
    ///         its calculations for the current tick, and can then apply metered
    ///         production/consumption to its own state (tanks, generators, etc.).
    /// 
    /// TYPICAL IMPLEMENTATION:
    ///     - Holds a ConverterId (Guid) assigned by the converter/broker
    ///     - Maintains local fields for capacities, current amounts, on/off states, etc.
    ///     - Uses ConversionResults to update those fields each simulation tick.
    /// </summary>
    public interface IOmniResourceConverterContext
    {
        /// <summary>
        /// Requests that the context host register its desired consumer and producer reports with the supplied registry.
        /// </summary>
        /// <param name="registry">A ConverterReportRegistry containing the lists of consumers and producers</param>
        void RegisterReports(ConverterReportRegistry registry);

        /// <summary>
        /// Called when the converter informs its context host that it has results of the resource conversions.
        /// </summary>
        /// <param name="results">A ConversionResults containing all the results.</param>
        void OnConversionResult(ConversionResults results);
    }
}
