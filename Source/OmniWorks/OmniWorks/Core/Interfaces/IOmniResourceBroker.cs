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
    /// Central coordinator that connects resource producers and consumers, meters
    /// resource flow between them, and reports per-converter results each tick.
    /// 
    /// PURPOSE:
    ///     Acts as the "traffic cop" for all resources:
    ///         - discovers which converters produce/consume which ResourceIds
    ///         - builds an internal ledger of total supply and demand
    ///         - decides how much each producer supplies and each consumer receives
    ///         - marks resources as brokered or unbrokered based on network topology
    /// 
    /// CORE RESPONSIBILITIES:
    ///     - NeedsRefresh:
    ///         Indicates when the internal ledger / graph must be recomputed
    ///         (e.g., converters added/removed, modes changed).
    /// 
    ///     - RegisterConverter / UnregisterConverter:
    ///         Maintain the active set of converters participating in the network.
    /// 
    ///     - BuildTotals(double deltaTime) / RunConverters(...) (depending on impl.):
    ///         1) Collects reports from all converters
    ///         2) Builds or refreshes the resource graph
    ///         3) Meters supply vs. demand per resource
    ///         4) Updates ConversionResults for each converter
    /// 
    /// DESIGN GOAL:
    ///     "If there is nothing to connect, the broker gets out of the way."
    ///     - Resources touched by only producers OR only consumers are marked
    ///       unbrokered, and left entirely to the owning converters to handle.
    /// </summary>
    public interface IOmniResourceBroker
    {
        /// <summary>
        /// Flag to indicate that the resource broker's data models are in need of a refresh.
        /// </summary>
        bool NeedsRefresh { get; set; }

        /// <summary>
        /// Registers a resource converter with the broker.
        /// </summary>
        /// <param name="converter">The OmniResourceConverter to register with the broker.</param>
        /// <returns>A bool indicating success or failure.</returns>
        bool RegisterResourceConverter(IOmniResourceConverter converter);

        /// <summary>
        /// Unregisters a resource converter from the broker.
        /// </summary>
        /// <param name="converter">The OmniResourceConverter to unregister from the broker.</param>
        void UnregisterConverter(IOmniResourceConverter converter);

        /// <summary>
        /// Unregisters a resource converter from the broker.
        /// </summary>
        /// <param name="converterId">The Guid to unregister from the broker.</param>
        void UnregisterConverter(Guid converterId);

        /// <summary>
        /// Builds the internal ledger with all the totals for recources consumed and produced.
        /// </summary>
        /// <param name="deltaTime">The amount of time that has passed, in seconds.</param>
        void BuildTotals(double deltaTime);
    }
}
