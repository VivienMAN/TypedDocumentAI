# TypedDocumentAI

**Extraction structurée et lecture de documents en C#/.NET 10, avec Mistral et OpenAI dans deux packages indépendants.**

[Documentation complète en anglais](README.md)

> **Première version prévue : `1.0.0`.** La compilation Linux, les 124 tests C# hors ligne et la génération des quatre packages avec symboles ont été vérifiés localement. Les Actions GitHub et les appels payants aux fournisseurs restent à exécuter. La publication NuGet attend la création de ton compte. Voir la [validation](docs/VALIDATION.md) et le [guide de publication](docs/RELEASING.md).

## Architecture

| Package | Rôle |
| --- | --- |
| `TypedDocumentAI.Abstractions` | Contrats, documents, options et résultats, sans dépendance tierce. |
| `TypedDocumentAI.Core` | Routage, schémas, validation, injection de dépendances et transport HTTP partagé. |
| `TypedDocumentAI.Mistral` | OCR natif et annotations structurées Mistral. |
| `TypedDocumentAI.OpenAI` | Lecture multimodale et extraction structurée via Responses. |

Le cœur ne dépend d'aucun fournisseur. Une implémentation supplémentaire peut proposer uniquement la lecture OCR, uniquement l'extraction structurée, ou les deux. Aucun basculement automatique n'envoie les documents à un autre fournisseur.

## Démarrage

Installer le SDK .NET 10 et Python 3.10+, puis exécuter à la racine :

```sh
python tools/verify.py
```

Le script restaure, compile, lance les tests hors ligne, collecte la couverture, exécute l'exemple de fournisseur local, puis génère et vérifie les quatre packages NuGet et leurs symboles. Il s'arrête à la première erreur et n'appelle aucune API payante.

Dans l'application, référencer les projets des fournisseurs utilisés, puis :

```csharp
using TypedDocumentAI;
using TypedDocumentAI.Mistral;
using TypedDocumentAI.OpenAI;

services.AddDocumentAI(options => options.DefaultProviderName = "mistral")
    .AddMistral(options => options.ApiKey = mistralApiKey)
    .AddOpenAI(options => options.ApiKey = openAiApiKey);
```

Les variables de clé viennent de la configuration sécurisée de l'application. Ne pas enregistrer un fournisseur dont la clé n'est pas disponible.

Injecter `IDocumentClient`, puis utiliser un modèle C# à propriétés publiques et modifiables ou `init` :

```csharp
var document = await DocumentInput.FromFileAsync(
    "facture.pdf", cancellationToken: cancellationToken);

var result = await client.ExtractAsync<Facture>(
    document,
    new ExtractionOptions { ProviderName = "mistral" },
    cancellationToken);

Facture facture = result.Value;
```

Remplacer `"mistral"` par `"openai"` sélectionne l'autre fournisseur. `ReadAsync()` renvoie le texte, indépendamment de l'extraction typée. `[Description]` permet de préciser le sens d'un champ et les propriétés nullables représentent les informations absentes.

## Différences à conserver

Mistral propose un OCR avec pages et options de sélection de pages, d'en-têtes et de pieds de page. OpenAI est ici une transcription générative multimodale, sans positions, pages ou scores de confiance inventés. Le résultat typé partage une forme, pas une garantie d'exactitude identique.

Les deux adaptateurs utilisent des documents PDF/PNG/JPEG/WEBP encodés dans la requête. Cette V2 ne crée pas de ressource distante dans une API Files et ne gère donc pas de suppression après upload. Les conditions de conservation du fournisseur restent applicables.

Les nouvelles tentatives HTTP sont désactivées par défaut pour limiter le risque de double traitement facturé. Les clés, contenus, annotations et prompts ne sont pas ajoutés aux logs applicatifs du package. La configuration de sécurité des logs de l'application reste à votre charge.

## Guides inclus

[Architecture](docs/ARCHITECTURE.md), [ajouter un fournisseur](docs/EXTENDING.md), [migration V1](docs/MIGRATING-V1.md), [tests](docs/TESTING.md), [publication](docs/RELEASING.md), [sécurité](SECURITY.md), [état de validation](docs/VALIDATION.md).

L'exemple `samples/CustomProviderDemo` fonctionne sans réseau. L'exemple `samples/InvoiceConsole` exige `--live` pour autoriser explicitement l'envoi d'une facture synthétique et les éventuels frais d'API.

## Publier sur NuGet

Après la configuration initiale décrite dans le [guide](docs/RELEASING.md), ouvrir **Actions → Publish NuGet → Run workflow**, choisir `main` et un incrément `patch`, `minor` ou `major`. La première publication sera `1.0.0`. Les tests, la version commune aux quatre packages, leur publication NuGet, le tag et la release GitHub sont automatiques. Aucun workflow ne démarre lors d’un push ou d’une PR.
