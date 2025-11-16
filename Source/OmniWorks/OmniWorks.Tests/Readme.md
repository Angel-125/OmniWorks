# OmniWorks.Tests  
### High-Performance & Regression-Proof Test Suite for the OmniWorks Resource Broker

<p align="center">
  <img src="https://img.shields.io/badge/Tested%20With-xUnit-blue?style=flat-square"/>
  <img src="https://img.shields.io/badge/Performance-Microsecond%20Scale-brightgreen?style=flat-square"/>
  <img src="https://img.shields.io/badge/Status-Stable-success?style=flat-square"/>
</p>

---

## 🚀 Overview

**OmniWorks.Tests** contains the complete **integration + performance test suite** for the **OmniWorks Resource Broker**.  
Everything is synthetic and self-contained—no KSP assemblies, no config files, no PartModules.

The suite answers the four essential questions:

- **Is the broker fast?** → Yes, microsecond-level per tick.  
- **Does it scale?** → Nearly linear scaling.  
- **Are edge-cases secretly slow?** → All tested; all tiny.  
- **Could regressions slip in unnoticed?** → Not with these tests.

---

## 📁 Project Layout

```
OmniWorks.Tests/
 ├── Integration
 │     • Required vs Optional Consumers
 │     • Multi-Resource Converters
 │     • Producer–Consumer correctness
 │     • Finite-Capacity Consumers
 │     • Ledger Refresh + culling correctness
 │
 ├── Performance
 │     • Synthetic average converters
 │     • Pathfinder Reference Base (~38 converters)
 │     • Max fan‑in / fan‑out (5 inputs, 3 outputs)
 │     • Slowest-throughput converters
 │     • 64 input / 64 output enumeration stress test
 │     • Scaling: 10, 100, 1000 converters
 │     • Ledger rebuild every tick under churn
 │     • 2000 resource IDs (dictionary stress)
 │     • Required + optional consumers under load
 │     • Finite-capacity optional consumers at scale
 │     • Producer-only / Consumer-only workloads
 │
 ├── Helpers
 │     • MultiResourceConverterContext
 │     • TestConverterContext
 │     • DualResourceProducerContext
 │     • FiniteCapacityOptionalConsumerContext
 │     • PerfTestLog (conditional logger)
 │
 └── README.md (this file)
```

---

## ▶️ Running the Tests

### **Visual Studio**
1. Open **Test Explorer**
2. Click **Run All**
3. View logs under:  
   - **Standard Output** (per test)  
   - **Output → Tests** (PerfTestLog output)

### **dotnet CLI**
```bash
dotnet test
```

### **Run only performance tests**
```bash
dotnet test --filter FullyQualifiedName~Performance
```

---

## 📊 Enabling Performance Logs

Performance logs only appear when the symbol `OMNIWORKS_PERF_LOG` is defined.

### **Enable via .csproj**
```xml
<PropertyGroup>
    <DefineConstants>OMNIWORKS_PERF_LOG</DefineConstants>
</PropertyGroup>
```

### **Sample Output**
```
[Perf] Broker_Performance_SyntheticAverageConverters_IsUnderBudget:
       average = 0.0045 ms/tick
```

Logs appear in:
- **Test Explorer → Standard Output**
- **Output → Tests**
- **Debug output**

---

## 🧪 Performance Methodology

Every performance test uses the same stable, reproducible steps:

1. Construct synthetic converter contexts.
2. Register them with **OmniResourceBroker**.
3. Run **100 warmup ticks** (stabilizes JIT + caches).
4. Run **10,000 measured ticks**.
5. Measure microseconds per tick.
6. Log via `PerfTestLog.Report`.
7. Compare against scenario-specific budgets.

This methodology guarantees:
- No external dependencies  
- Consistent results  
- Detection of regressions long before they become problems  

---

## 📈 Performance Summary

**Machine used for development:** mid‑range Windows PC  
All values below: **steady**, **repeatable**, and **under budget**.

| Scenario | Avg ms/tick | Budget | Status |
|---------|-------------|--------|--------|
| Synthetic average converters | ~0.0045 | 0.05 | ✅ |
| Pathfinder 38‑converter base | ~0.0026 | 0.03 | ✅ |
| Max fan‑in/out | ~0.0037 | 0.04 | ✅ |
| Slowest-throughput converters | ~0.003 | 0.03 | ✅ |
| 64-in / 64-out converter | ~0.008 | 0.05 | ✅ |
| Scaling (10→100→1000) | Linear | Dynamic | ✅ |
| Ledger rebuild every tick | ~0.15 | 0.20 | ✅ |
| 2000 resource IDs | ~0.08 | 0.10 | ✅ |
| Mixed required/optional consumers | ~0.06 | 0.08 | ✅ |
| Finite-capacity optional consumers | ~0.05 | 0.08 | ✅ |
| 1000 producers only | ~0.03 | 0.08 | ✅ |
| 1000 consumers only | ~0.03 | 0.08 | ✅ |

**Conclusion:**  
Every scenario remains well within microsecond-scale performance expectations.

---

## 🛠️ Troubleshooting

### **I don’t see performance logs!**
Make sure `OMNIWORKS_PERF_LOG` is defined.  
Without it, logging calls are removed at compile time.

### **Tests seem slow. Why?**
This usually happens when:
- The debugger is attached  
- CPU is in power-saving mode  
- Visual Studio is indexing  
- Running inside a VM or WSL

### **Why 10,000 ticks in every test?**
Because:
- Micro-benchmarks need statistical mass  
- .NET JIT warms up after ~100 iterations  
- Dictionary caches stabilize  
- GC behavior becomes predictable  

---

## 📜 License

This project is licensed under **GPL‑v3**, matching OmniWorks.Core.

---

<p align="center">
  <strong>OmniWorks Resource Broker – Fast. Deterministic. Regression-Proof.</strong>
</p>
