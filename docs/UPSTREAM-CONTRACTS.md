# Upstream contracts and evidence

Documentation consulted on **2026-10-02**. These are reference contracts, not evidence that a live request from this repository has succeeded. Models, permissions, limits and pricing can change independently of this source bundle.

## Mistral

- [OCR API reference](https://docs.mistral.ai/api/endpoint/ocr): POST `/v1/ocr`, document chunks, pages, annotations, header/footer options and response fields.
- [Basic OCR](https://docs.mistral.ai/studio/document-processing/basic_ocr): document/image inputs including base64 data URIs and page Markdown.
- [Document annotations](https://docs.mistral.ai/studio/document-processing/annotations): structured extraction using JSON Schema and document annotation instructions.

The adapter sends `document_url` for PDF data URIs and `image_url` for supported images. Typed extraction sets `document_annotation_format` with a strict schema and `document_annotation_prompt`. Raw OCR does not reuse that annotation prompt as an undocumented OCR instruction field. The documented annotation is JSON encoded in a string; the reader also tolerates an object representation without requiring it.

This version intentionally does not expose every modern OCR field, image asset, bounding box, confidence feature or batch API. Model defaults use a configurable alias. The common client does not embed platform-specific page limits as universal rules.

## OpenAI

- [PDF/file inputs](https://developers.openai.com/api/docs/guides/file-inputs): Responses input_file with filename and base64 file_data.
- [Images and vision](https://developers.openai.com/api/docs/guides/images-vision): image inputs and data URLs.
- [Structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs): Responses text.format JSON Schema, strict properties and handling refusals/incomplete output.
- [Responses creation reference](https://developers.openai.com/api/reference/resources/responses/methods/create): request/response envelopes and usage.
- [GPT-4.1 mini model](https://developers.openai.com/api/docs/models/gpt-4.1-mini): the configurable initial model choice.
- [Data controls](https://developers.openai.com/api/docs/guides/your-data): response storage and separate provider retention controls.

The adapter uses input_file for PDFs and input_image for images. It reads assistant message output_text, checks completion/refusal status, and requests strict structured output for typed extraction. It sets store=false and does not create Files API resources. It does not use the chat-completions response format on the Responses endpoint.

OpenAI's adapter is multimodal generative transcription, not a claim of native OCR geometry. The API response used here does not provide ground-truth page segmentation; empty Pages and nullable page counts are deliberate.

## .NET and repository tooling

- [System.Text.Json schema exporter](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema).
- [System.Text.Json nullability](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations).
- [IHttpClientFactory](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory).
- [Microsoft.Extensions.Http 10.0.9](https://www.nuget.org/packages/Microsoft.Extensions.Http/10.0.9), [Microsoft.NET.Test.Sdk 17.14.1](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1), [xUnit 2.9.3](https://www.nuget.org/packages/xunit/2.9.3), [VS runner 3.1.5](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5), [coverlet 6.0.4](https://www.nuget.org/packages/coverlet.collector/6.0.4).
- [actions/checkout](https://github.com/actions/checkout), [actions/setup-dotnet](https://github.com/actions/setup-dotnet), [actions/upload-artifact](https://github.com/actions/upload-artifact).

Package pages and action references were checked as published references, not restored/executed in this environment. Dependency audit, runner behavior, compiler/analyzer compatibility and live model behavior still require the supplied verification workflows.
