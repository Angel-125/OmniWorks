# OmniWorks Performance Snapshot

This document summarizes the current performance characteristics of the `OmniResourceBroker` and its associated converter pipeline, as measured by the `OmniWorks.Tests` performance suite.

All numbers below were collected on the current development machine using:

- 10,000 measured ticks per test (`deltaTime = 1.0`)
- 100 warmup ticks per scenario to allow JIT and internal caches to stabilize
- Synthetic converter contexts (no dependency on KSP or CFG files)
- xUnit tests in the `OmniWorks.Tests` project

> **Is the broker fast?** – Yes, laughably.  
> **Does it scale?** – Yes, roughly linearly.  
> **Are edge cases or special behaviors secretly slow?** – All measured; all tiny.  
> **Could I accidentally regress something and not notice?** – Not anymore.

---

## 1. Summary of Key Scenarios

### 1.1 Realistic Pathfinder Base Scenarios

These tests model a reference Pathfinder base:

- 5 × Hacienda (6 converters each) → 30 converters  
- 4 × Casa (2 converters each) → 8 converters  
- **Total: 38 converters**

Using synthetic converters based on statistics gathered from the real KSP converters.

| Test Name                                                                          | Scenario                                                                 | Converters | Avg ms/tick |
|------------------------------------------------------------------------------------|--------------------------------------------------------------------------|-----------:|------------:|
| `Broker_Performance_ReferencePathfinderBase_IsUnderBudget`                         | Reference base, “average” converters (~2 inputs, 1 output)              |        38  | **0.0026**  |
| `Broker_Performance_ReferencePathfinderBase_MaxFanInFanOutConverters_IsUnderBudget`| Reference base, worst-case fan-in/out (5 inputs, 3 outputs per converter)|        38  | **0.0037**  |

Interpretation:

- A mid-sized Pathfinder base is processed in **~2.6–3.7 microseconds per tick**.
- This is effectively free compared to a 20 ms Unity FixedUpdate frame.

---

### 1.2 Synthetic “Average” Converter Ecosystem

This test uses the **full set of 69 synthetic converters** built to statistically match the real converter data (average input/output counts and throughputs).

| Test Name                                                          | Scenario                                       | Converters | Avg ms/tick |
|--------------------------------------------------------------------|------------------------------------------------|-----------:|------------:|
| `Broker_Performance_SyntheticAverageConverters_IsUnderBudget`      | Full synthetic ecosystem, “average” converters |        69  | **0.0045**  |

Interpretation:

- 69 converters take **4.5 microseconds** per tick.
- This is the slowest of the basic “average converter” tests and is still **absurdly fast**.

---

### 1.3 Per-Converter Worst-Case Enumeration

This test intentionally creates a “pathological” converter with a huge number of inputs and outputs to probe worst-case enumeration behavior.

| Test Name                                                                | Scenario                                   | Converters | Avg ms/tick |
|--------------------------------------------------------------------------|--------------------------------------------|-----------:|------------:|
| `Broker_Performance_SingleConverter_MaxEnumerationCost_IsUnderBudget`    | 1 converter, 64 inputs + 64 outputs        |         1  | **0.0011**  |

Interpretation:

- Even a converter with **64 inputs and 64 outputs** completes in about **1.1 microseconds** per tick.
- Per-converter enumeration and aggregation are cheap.

---

### 1.4 Scaling With Converter Count

This test runs the same “average” converter shape at different counts to verify scaling properties.

| Test Name                                                                    | Scenario                          | Converters | Avg ms/tick |
|------------------------------------------------------------------------------|-----------------------------------|-----------:|------------:|
| `Broker_Performance_ScalingWithConverterCount_IsUnderBudget (10)`            | Basic scaling – small vessel      |        10  | **0.0007**  |
| `Broker_Performance_ScalingWithConverterCount_IsUnderBudget (100)`           | Basic scaling – large vessel      |       100  | **0.0067**  |
| `Broker_Performance_ScalingWithConverterCount_IsUnderBudget (1000)`          | Basic scaling – stress test       |     1,000  | **0.0631**  |

Interpretation:

- The cost grows **roughly linearly** with converter count.
- Even with **1000 converters**, the broker uses about **0.063 ms/tick**, which is still less than **0.5%** of a 20 ms physics frame.

---

### 1.5 Worst-Case Ledger Refresh + Converter Churn

This test stresses the “refresh ledger” path, simulating a situation where converters are constantly being registered and the broker must rebuild its internal data structures every tick.

| Test Name                                                                           | Scenario                                                            | Converters (end of test) | Avg ms/tick |
|-------------------------------------------------------------------------------------|---------------------------------------------------------------------|--------------------------:|------------:|
| `Broker_Performance_WorstCase_LedgerRefreshUnderConverterChurn_IsUnderBudget`      | New converter added every tick; full ledger rebuild each tick      | ~1,100                   | **0.0639**  |

Interpretation:

- Constant converter churn and forced ledger refresh push the broker into its heaviest path.
- Even then, it stays at ~**0.064 ms/tick**, comparable to the 1000-converter steady-state scenario.

---

### 1.6 Many Distinct Resources

This test creates a **wide** ledger with many distinct resource IDs:

- 1,000 converters
- 1 unique input resource ID per converter
- 1 unique output resource ID per converter → **2,000 resource IDs total**

| Test Name                                                                      | Scenario                                   | Converters | Avg ms/tick |
|--------------------------------------------------------------------------------|--------------------------------------------|-----------:|------------:|
| `Broker_Performance_ManyDistinctResources_LargeConverterCount_IsUnderBudget`   | 1000 converters, 2000 distinct resource IDs|     1,000  | **0.0712**  |

Interpretation:

- High resource cardinality adds a bit of overhead, but **still < 0.08 ms/tick**.

---

### 1.7 Behavioral Variants: Required/Optional, Capacity, and Idle Paths

These tests probe specific behaviors and edge cases.

#### Mixed Required and Optional Consumers

| Test Name                                                                          | Scenario                                                   | Converters | Avg ms/tick |
|------------------------------------------------------------------------------------|------------------------------------------------------------|-----------:|------------:|
| `Broker_Performance_MixedRequiredAndOptionalConsumers_UnderLoad_IsUnderBudget`     | 100 producers, 100 required + 300 optional consumers       |       500  | **0.0268**  |

#### Finite-Capacity Optional Consumers

| Test Name                                                                                | Scenario                                               | Converters | Avg ms/tick |
|------------------------------------------------------------------------------------------|--------------------------------------------------------|-----------:|------------:|
| `Broker_Performance_FiniteCapacityOptionalConsumers_AtScale_IsUnderBudget`              | 100 producers, 500 finite-capacity optional consumers  |       600  | **0.0394**  |

#### All Producers, No Consumers

| Test Name                                                                            | Scenario                                | Converters | Avg ms/tick |
|--------------------------------------------------------------------------------------|-----------------------------------------|-----------:|------------:|
| `Broker_Performance_AllProducers_NoConsumers_IsUnderBudget`                         | 1000 producers, 0 consumers             |     1,000  | **0.0461**  |

#### All Consumers, No Producers

| Test Name                                                                            | Scenario                                | Converters | Avg ms/tick |
|--------------------------------------------------------------------------------------|-----------------------------------------|-----------:|------------:|
| `Broker_Performance_AllConsumers_NoProducers_IsUnderBudget`                         | 1000 consumers (500 required, 500 opt)  |     1,000  | **0.0489**  |

Interpretation:

- Required vs optional handling, finite-capacity logic, and “producer-only” / “consumer-only” cases are all **tiny** in cost.
- None of these behavioral branches introduce surprising overhead.

---

## 2. High-Level Conclusions

- **Is the broker fast?** – Yes, laughably.
  - Most realistic scenarios complete in **2–40 microseconds** per tick.
  - Even the heaviest synthetic stress tests stay well below **0.1 ms/tick**.

- **Does it scale?** – Yes, roughly linearly.
  - Scaling tests (10 / 100 / 1000 converters) show near-linear growth.
  - No signs of hidden O(n²) behavior in the normal paths.

- **Are edge cases or special behaviors secretly slow?** – All measured; all tiny.
  - Max fan-in/out converters, many distinct resources, finite capacity, and optional consumers all perform well within tight budgets.

- **Could I accidentally regress something and not notice?** – Not anymore.
  - Each scenario has a concrete performance budget encoded as a test assertion.
  - If future changes significantly slow down the broker, these tests will fail and surface the regression.

---

## 3. Interpreting These Numbers In-Game

As a rough reference:

- Unity’s default `FixedUpdate` timestep is **0.02 seconds** (20 ms).
- A “heavy” OmniWorks scenario at **0.07 ms/tick** consumes:

  \[
  \frac{0.07}{20.0} \times 100 \approx 0.35\%
  \]

  of the physics frame.

In practice, the broker is nowhere near the performance bottleneck in a typical game loop. Other systems (physics, rendering, game logic) will dominate long before OmniWorks becomes a concern.
