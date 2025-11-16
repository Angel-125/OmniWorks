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
    /// Describes how much of a specific resource a converter wants to CONSUME,
    /// and how much of that demand the broker actually granted during this tick.
    /// 
    /// PURPOSE:
    ///     Provides the broker with per-resource "demand" from each consumer so it can:
    ///         - allocate limited production
    ///         - respect required vs. optional consumption
    ///         - report back how much was granted
    /// 
    /// KEY FIELDS:
    ///     - EndpointId            → identifies the owning converter instance
    ///     - ResourceId            → integer ID of the resource being consumed
    ///     - AmountRequestedPerSec → how much the converter would like per second
    ///     - IsOptional            → true if this consumer can be throttled to zero
    ///                               without breaking simulation (e.g., tanks, luxuries)
    ///     - AmountGrantedPerTick  → how much the broker actually allocated this tick
    ///     - IsBrokered            → true if this report participated in a network
    ///                               with at least one producer; false if isolated
    ///                               and left for the converter itself to handle
    /// 
    /// LIFECYCLE:
    ///     - Created and filled by the converter/context before broker runs
    ///     - Modified by the broker (AmountGrantedPerTick, IsBrokered)
    ///     - Read by the converter after broker execution to apply the results
    /// </summary>
    public sealed class ConsumerReport
    {
        /// <summary>
        /// ID of the producer who created the report.
        /// </summary>
        public Guid EndpointId;

        /// <summary>
        /// ID of the resource being consumed.
        /// </summary>
        public int ResourceId;

        /// <summary>
        /// Amount of resource requested for consumption, per second.
        /// </summary>
        public double AmountRequestedPerSec;

        /// <summary>
        /// Flag indicating whether or not the requested resource is required for consumption (e.g. refinery), or optional (e.g. fuel tank).
        /// </summary>
        public bool IsOptional;

        /// <summary>
        /// Amount of resource actually granted, per time tick. Managed by the broker.
        /// </summary>
        public double AmountGrantedPerTick;

        /// <summary>
        /// Flag indicating whether or not the broker will manage this report.
        /// If set to false, then it means that no other converter is making use of the resource, and the converter itself needs to manage the resource.
        /// Managed by the broker.
        /// </summary>
        public bool IsBrokered;
    }
}
