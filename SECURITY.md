# Security and data handling

## Reporting

Do not publish credentials, private documents or exploitation details in a public issue. After the repository owner enables GitHub private vulnerability reporting, use the repository's Security tab. If that channel is not enabled, contact a maintainer through an actual private contact they publish. This source bundle does not create a monitored mailbox or promise a response-time SLA.

The latest stable release line is maintained. Local compiler and offline tests have passed; no independent security audit or live provider verification has been performed. See [validation](docs/VALIDATION.md).

## Boundaries

Documents are sent to the explicitly selected provider. There is no automatic fallback to another organization. API keys belong in a server-side environment/secret store, not Blazor WebAssembly, browser JavaScript, a mobile bundle or source control. A configured API root is privileged configuration because it receives the document and authentication header.

The default HTTP setup requires HTTPS, disables redirects/cookies and redacts headers. Library error messages omit raw HTTP/JSON bodies, document contents and prompts; underlying response-parsing exceptions containing data are not retained as inner exceptions. Applications and custom handlers can still log sensitive payloads; audit their configuration separately. A service-generated request ID is exposed for debugging and must still be handled with normal log hygiene.

Inputs and responses are bounded per request, not globally. MIME checks do not validate file signatures, prove a file is harmless, remove malware or detect decompression bombs inside provider-side document parsers. Apply upload/authentication/rate/concurrency controls in the host application. Managed document/base64/JSON buffers are not guaranteed to be wiped from memory on release.

Requests use inline data rather than creating persistent Files API resources, so there is no upload-delete lifecycle. This does not mean zero retention. OpenAI requests use store=false, but separate provider abuse monitoring/retention rules and account controls still apply. Review [upstream data references](docs/UPSTREAM-CONTRACTS.md) and the terms applicable to each account before sending sensitive data.

## AI output is untrusted

The instructions request faithful extraction and discourage following instructions embedded in the document. This is not a prompt-injection guarantee. Keep provider outputs outside authorization decisions and command execution unless validated by an appropriate application boundary. Schema validation protects shape and conversion, not factual accuracy or business meaning.

## Retry and supply-chain controls

POST replays can duplicate paid processing, so retries are disabled until explicitly configured. Cancellation does not prove the provider stopped processing. No exactly-once guarantee is made.

The repository pins direct package versions and action commits, enables NuGet audit and includes Dependabot. These controls still require a successful restore/audit and ongoing review; source delivery is not proof that dependencies are vulnerability-free. Normal CI receives no provider/publishing keys. Only trusted commits on main should be published through the manual release workflow and its OIDC publishing environment.
