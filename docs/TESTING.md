# Testing

## Delivery status

Local Linux verification passed with .NET SDK 10.0.112: 124 C# tests, a warnings-as-errors build, package creation and local-feed installation. GitHub Actions, Windows execution and live provider calls have not been run. See [VALIDATION.md](VALIDATION.md) for the exact checks performed.

## Full offline verification

From the repository root with .NET 10 and Python 3.10+:

```sh
python tools/verify.py
```

Equivalent wrappers are `bash tools/verify.sh` and `./tools/verify.ps1`. The script does not suppress errors and exits with code 2 when dotnet is absent. It runs restore, a warnings-as-errors build, xUnit/VSTest with coverlet collection, the custom-provider sample, then `dotnet pack`, actual package-content checks, release-automation regression tests and installation into a disposable consumer from a local feed. Outputs are under `artifacts/`.

Normal tests use an in-memory `HttpMessageHandler`, synthetic JSON and tiny mock document byte sequences. The mock PDF bytes are intentionally not a real renderable PDF: HTTP unit tests validate our contract, not a provider's PDF decoder. No external HTTP request or credential is needed for those tests.

The tests cover input bounds/ownership, non-seekable streams, strict schemas and result validation, nested shapes, cycles, thread-safe schema caching, routing, named registration, adapter payloads, real-versus-absent metadata, refusals, incomplete output, wrong options, timeouts, caller cancellation, bounded responses, safe errors and explicit retry behavior.

## Live smoke test

The synthetic [invoice PNG](../samples/InvoiceConsole/Fixtures/invoice.png) contains no customer information. It has number INV-001, date 2026-10-01 and total 125.50. Models may format or interpret details differently; verify the result rather than treating every HTTP 200 as a quality pass.

After setting the selected provider's environment variable:

```sh
dotnet run --project samples/InvoiceConsole -- mistral samples/InvoiceConsole/Fixtures/invoice.png extract --live
dotnet run --project samples/InvoiceConsole -- openai samples/InvoiceConsole/Fixtures/invoice.png extract --live
```

Each command sends the fixture to a provider and may incur charges. The console prints the resulting data, so do not use production documents in shared CI logs. Optional MISTRAL_MODEL/OPENAI_MODEL environment variables override defaults.

The manual GitHub workflow requires explicit paid-request confirmation, the main branch and the `live-tests` environment. Configure its provider secrets and environment review rules yourself. It is never triggered by a pull request. A successful command establishes request compatibility, not statistically measured recognition quality.

## Before calling the release stable

Require green Linux and Windows CI; inspect package contents and dependency audit results; run both live providers against representative synthetic PDFs and images; add regressions for failures; verify nullable/nested/enum schemas against the selected real models. Measure accuracy on a consented evaluation set before making accuracy, reliability or performance claims. No such benchmark is included in this release line.
