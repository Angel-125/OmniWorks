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
    /// Describes how much of a specific resource a converter is willing to PRODUCE,
    /// and how much of that offer the broker actually used during the current tick.
    /// 
    /// PURPOSE:
    ///     Gives the broker a per-resource "offer" from each producer so it can:
    ///         - match producers to consumers
    ///         - meter out usage fairly
    ///         - report back how much was consumed
    /// 
    /// KEY FIELDS:
    ///     - EndpointId          → identifies the owning converter instance
    ///     - ResourceId          → integer ID of the resource being produced
    ///     - AmountOfferedPerSec → how much the converter can produce per second
    ///                             (input to the broker)
    ///     - AmountUsedPerTick   → how much was actually consumed during this tick
    ///                             (output from the broker)
    ///     - IsBrokered          → true if this report was part of a brokered
    ///                             resource network; false if no matching
    ///                             consumers existed and the converter must
    ///                             manage the resource itself
    /// 
    /// LIFECYCLE:
    ///     - Created and filled by the converter/context before broker runs
    ///     - Modified by the broker (AmountUsedPerTick, IsBrokered)
    ///     - Read by the converter after broker execution to apply results
    /// </summary>
    public sealed class ProducerReport
    {
        /// <summary>
        /// ID of the producer who created the report.
        /// </summary>
        public Guid EndpointId;

        /// <summary>
        /// ID of the resource being produced.
        /// </summary>
        public int ResourceId;

        /// <summary>
        /// Amount offered up for sacrifice- er, production, per second.
        /// </summary>
        public double AmountOfferedPerSec;

        /// <summary>
        /// Of the amount offered, how much was actually used, per time tick. Managed by the broker.
        /// </summary>
        public double AmountUsedPerTick;

        /// <summary>
        /// Flag indicating whether or not the broker will manage this report.
        /// If set to false, then it means that no other converter is making use of the resource, and the converter itself needs to manage the resource.
        /// Managed by the broker.
        /// </summary>
        public bool IsBrokered;
    }
}
