# Changelog

## 1.0.0 (prepared for first publication)

### Added

Four-package architecture with a provider-neutral client and independent Mistral and OpenAI adapters. Separate OCR and structured-extraction interfaces; capability discovery; explicit named routing; schema generation and strict local result checks; bounded input/response processing; safe errors, cancellation and deadline handling; opt-in POST retries; DI and content-free diagnostic activities.

Offline contract tests, two console examples, synthetic invoice fixture, manual Linux/Windows CI, manual paid smoke workflow, OIDC NuGet publishing with automatic SemVer, tags/releases and resumable package uploads, package-content checks, migration/extension/operations documentation and MIT licensing.

### Breaking changes from the single-provider prototype

New package family/namespaces, input snapshot type, result wrapper and registration API. Inline inputs replace remote file uploads/cleanup. POST retries are now disabled by default. No public arbitrary JSON schema method, local PDF parser/page counter or universal page cap.

### Validation

The first stable release is prepared but not yet published. Local Linux compilation, 124 offline C# tests, release automation regressions, restore/audit, package creation and local-feed installation passed. GitHub Actions, Windows tests and live provider calls have not run. See docs/VALIDATION.md for evidence and limits.
