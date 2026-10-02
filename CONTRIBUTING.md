# Contributing

Start with a focused issue describing a document-processing problem and a proposed public API. Avoid broad SDK features that are unrelated to typed extraction or text reading.

Use .NET 10 and run `python tools/verify.py`. Keep nullable annotations enabled, add XML documentation to public APIs, respect `.editorconfig`, and add regression tests for both success and failure paths. Do not globally disable analyzers or warnings to make CI pass.

Keep Core independent of provider implementations. Advertise only real adapter capabilities and use provider-specific options for features without a common meaning. Unsupported operations must be absent from optional contracts, not fake implementations. Discuss breaking changes and update migration notes before changing existing public interfaces.

Keep implementation methods private. The documented protected transport extension points are intentional; do not change them merely to satisfy a visibility rule. `ApiSurfaceTests` checks visibility and compares the public/protected surface with [PublicAPI.txt](tests/TypedDocumentAI.Tests/PublicAPI.txt), including parameter names/defaults, nullable states and generic constraints. This is an API review aid, not an exhaustive binary compatibility certification.

When an intentional API change fails that test, inspect the diff against `tests/TypedDocumentAI.Tests/bin/Release/net10.0/PublicAPI.actual.txt`, choose the appropriate SemVer increment and update the baseline only after review. Do not regenerate it automatically in CI. Breaking removals and signature changes need a major version; additions normally need a minor version. Re-run the full verification after updating the reviewed baseline.

Unit tests must use synthetic data and fake HTTP handlers. Never commit keys, personal data, real invoices, provider response bodies or sensitive screenshots. Paid live calls require explicit opt-in and are separate from ordinary verification.

A pull request should explain the problem, implementation choices, verification performed and remaining limitations. Do not report tests as passing unless they actually ran. Follow CODE_OF_CONDUCT.md and SECURITY.md. Contributions must be material you have authority to contribute under this repository's license.
