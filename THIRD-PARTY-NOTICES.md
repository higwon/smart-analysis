# Third-Party Notices

SmartAnalysis uses the following third-party open-source components. All are permissive
(MIT/BSD/Apache-2.0) — none impose copyleft on the product (doc 20, ADR-001/006/015).

## Runtime dependencies (shipped)

| Component | Version | License | Used in | Purpose |
|---|---|---|---|---|
| TiffLibrary | 0.6.65 | MIT | `SmartAnalysis.Infrastructure` | PSIA-TIFF reading (FF01), isolated behind the `IScanFileReader` port (ADR-015) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | `SmartAnalysis.Analysis`, `SmartAnalysis.Infrastructure` | Explicit per-module DI registration (ADR-005) |

## Test-only dependencies (not distributed)

| Component | Version | License | Purpose |
|---|---|---|---|
| xUnit | 2.9.2 | Apache-2.0 | Unit tests |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 | Test runner |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT | Test host |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT | DI container for wiring tests |

This file is updated whenever a dependency is added or changed (doc 20: notice obligations).
Full license texts are available from each component's NuGet package / source repository.
