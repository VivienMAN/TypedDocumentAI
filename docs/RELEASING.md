# Manual GitHub and NuGet releases

Repository: [VivienMAN/TypedDocumentAI](https://github.com/VivienMAN/TypedDocumentAI).

## One-time NuGet setup

The repository and workflows are ready to be configured. No package has been published during repository setup. The owner must first create and verify a [nuget.org account](https://www.nuget.org/users/account/LogOn). Use the profile username, not an email address.

1. In GitHub, open **Settings → Secrets and variables → Actions → Variables**, and add a repository variable named `NUGET_USER` containing your NuGet profile username.
2. The GitHub environment `nuget` is restricted to `main`. It requires no additional reviewer approval; the manual workflow launch authorizes publication.
3. Sign into nuget.org, open your account's **Trusted Publishing**, and add a policy with these exact values:

| Setting | Value |
| --- | --- |
| Repository owner | `VivienMAN` |
| Repository | `TypedDocumentAI` |
| Workflow file | `release.yml` (filename only) |
| Environment | `nuget` |
| Package glob | `TypedDocumentAI.*` |
| Scopes | Push new packages and new versions |
| Policy owner | Your NuGet account, or the organization intended to own these packages |

[Official NuGet instructions](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing). The `NuGet/login` action exchanges the GitHub OIDC identity for a temporary key immediately before publication. There is no permanent `NUGET_API_KEY` secret to create. The workflow fails before testing or reserving a version when `NUGET_USER` is missing. A missing or mismatched policy fails at login before the release is reserved.

Package IDs are `TypedDocumentAI.Abstractions`, `TypedDocumentAI.Core`, `TypedDocumentAI.Mistral` and `TypedDocumentAI.OpenAI`. The first successful upload establishes ownership; absence from NuGet search does not reserve a name. If NuGet rejects a name or permission, stop and investigate rather than renaming one project independently.

## Publish

Commit and push your changes to `main`, then open **Actions → Publish NuGet → Run workflow**:

- Branch: `main`.
- `bump`: `patch` by default, or `minor` / `major`.
- `resume_tag`: leave empty for a new publication.

The first publication is always `1.0.0`. After that, the highest completed stable release determines the next version: `1.0.0 → 1.0.1` for patch, `1.0.0 → 1.1.0` for minor, and `1.0.0 → 2.0.0` for major. All four packages share that version. `Directory.Build.props` contains the local development baseline; released versions are injected into restore/build/pack and are recorded by Git tags and releases. Do not edit the baseline for each publication or create release tags manually.

The workflow verifies the selected commit on Linux and Windows, including offline C# tests, automation regressions, dependency audit, package-content checks and installation from a local feed. After both platforms pass, it logs into NuGet, reserves `vVERSION` at the exact verified commit and creates a draft release. A `release-bundle.zip` stores the original eight package files and a manifest with version, commit and SHA-256 hashes before any NuGet upload. The same files are attached individually to the release.

Publication proceeds in dependency order: Abstractions, Core, Mistral, OpenAI, including their symbol packages. The workflow waits up to 20 minutes after submissions for all four primary packages to become downloadable, compares their payloads with the saved originals, then publishes the GitHub release with generated notes and NuGet links. [NuGet validation and indexing](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package) are asynchronous. Symbol uploads are checked for acceptance; symbol-server indexing is asynchronous and is not certified by the workflow.

No workflows run on pushes, pull requests, tags or release events. **CI** can be run manually without publication. Provider calls are excluded from both CI and releases; the separate manual smoke workflow needs explicit paid-request authorization and its own credentials. Scheduled Dependabot version updates are disabled to keep repository automation manual. Review dependency versions and pinned action commits as part of maintenance.

## Resume an interrupted publication

A four-package release is not an atomic NuGet transaction. Successful uploads cannot be overwritten. If any step fails, the release remains a draft and subsequent new versions are blocked.

Use **Re-run failed jobs** or **Re-run all jobs** for the original run. Alternatively, launch **Publish NuGet** again from `main`, setting `resume_tag` to the draft's tag, such as `v1.0.0`. The chosen increment is ignored. The saved version, source commit and original package bundle are reused, even if `main` has advanced. Re-running an already completed publication is a no-op.

If upload failed while NuGet was still indexing a previously accepted package, the workflow waits for the package and verifies its contents before treating it as already uploaded. Repository signatures and their content-type entry are ignored when comparing payloads; other changes stop publication. Symbols can be resubmitted with duplicate detection after the main package has been checked. Release assets and tags are never force-replaced.

Do not delete the draft, its tag or bundle to work around a failure after uploading packages. Fix account permissions, policy or registry availability, then resume. If indexing exceeds the workflow wait, resume later. If existing package contents differ, investigate ownership and the earlier upload; automatic publication stops rather than silently accepting the mismatch.

## Local verification

```sh
python tools/verify.py
python tools/verify.py --version 1.0.1
python -m unittest discover -s tools/tests -v
```

The version override exercises the same mechanism used by releases. Python 3.10+ and .NET SDK 10 are required. Direct dependencies and action revisions are pinned; NuGet auditing remains enabled and warnings are errors. Workflow syntax is also checked locally with `actionlint` when changing automation. Outputs and reports remain in the ignored `artifacts/` directory.
