# TypedDocumentAI

Extraire des objets C# typés à partir de documents, ou lire leur texte, avec des adaptateurs indépendants pour Mistral et OpenAI. Définir une classe `Invoice`, envoyer un PDF ou une image et récupérer un objet dont la forme est validée localement.

[Documentation anglaise](README.md) · [Architecture](docs/ARCHITECTURE.md) · [Sécurité](SECURITY.md)

Bibliothèque communautaire non officielle, sous licence MIT. Les comptes fournisseurs et leurs frais d’API restent distincts du package.

## Essayer sans clé API

Avec le SDK .NET 10, depuis ce dépôt :

```sh
dotnet run --project samples/OfflineInvoiceDemo
```

Le fournisseur de démonstration renvoie une réponse synthétique fixe. Le vrai client génère le schéma, valide le JSON et construit un objet `Invoice`. Cette démo ne réalise aucun OCR ni appel IA.

```text
OFFLINE DEMO: fixed synthetic response, no OCR, network or paid API call.
{
  "Number": "INV-001",
  "Total": 125.50,
  "Date": "2026-10-01"
}
```

.NET peut télécharger les dépendances au premier lancement ; la démo elle-même n’effectue aucun appel réseau. Le JSON peut omettre les zéros décimaux finaux.

## Installer et extraire une facture

**.NET 10 uniquement.** Installer un seul adaptateur suffit : Core et Abstractions sont ses dépendances. Python sert aux vérifications du dépôt, jamais à l’application consommatrice.

Ces commandes NuGet seront utilisables après la première publication. D’ici là, utiliser les exemples du dépôt ou des références de projets. Consulter les [releases](https://github.com/VivienMAN/TypedDocumentAI/releases) pour l’état de publication.

```sh
dotnet new console -n InvoiceDemo -f net10.0
cd InvoiceDemo
dotnet add package TypedDocumentAI.Mistral
```

Configurer `MISTRAL_API_KEY` dans l’environnement serveur ou un gestionnaire de secrets, puis remplacer `Program.cs` par cet exemple complet. Ne pas mettre la clé dans le code.

<!-- compile:quickstart -->
```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TypedDocumentAI;
using TypedDocumentAI.Mistral;

if (args.Length != 2 || args[1] != "--live")
{
    Console.Error.WriteLine("Usage: dotnet run -- <invoice.png|invoice.pdf> --live");
    Console.Error.WriteLine("--live sends your document to Mistral and may incur API charges.");
    return 2;
}

var services = new ServiceCollection();
services.AddDocumentAI().AddMistral(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("MISTRAL_API_KEY")
        ?? throw new InvalidOperationException("Set MISTRAL_API_KEY in your environment.");
});
await using var container = services.BuildServiceProvider();
var client = container.GetRequiredService<IDocumentClient>();
var document = await DocumentInput.FromFileAsync(args[0]);
var result = await client.ExtractAsync<Invoice>(document);
Console.WriteLine(JsonSerializer.Serialize(result.Value,
    new JsonSerializerOptions { WriteIndented = true }));
return 0;

public sealed class Invoice
{
    [Description("Invoice number exactly as printed, or null when absent")]
    public string? Number { get; init; }
    [Description("Printed final total including taxes, or null when absent")]
    public decimal? Total { get; init; }
    [Description("Invoice date in YYYY-MM-DD format, or null when absent")]
    public DateOnly? Date { get; init; }
}
```
<!-- /compile:quickstart -->

```sh
dotnet run -- invoice.png --live
```

Cette commande envoie le fichier à Mistral et peut être facturée. Les champs nullables représentent les informations absentes ; la validation du schéma ne garantit pas l’exactitude des valeurs extraites.

Pour OpenAI, installer `TypedDocumentAI.OpenAI`, configurer `OPENAI_API_KEY` et suivre son [exemple complet](docs/nuget/OpenAI.md). Une seule clé est nécessaire pour chaque exemple.

## Choisir un fournisseur

| Comportement | Mistral | OpenAI |
| --- | --- | --- |
| Extraction typée | Annotations du document OCR | Structured output de Responses |
| Lecture de texte | OCR natif en Markdown | Transcription générative |
| PDF, PNG, JPEG, WEBP | Pris en charge par cet adaptateur | Pris en charge par cet adaptateur |
| Pages et indices réels | Disponibles | Absents ; `Pages` reste vide |
| Sélection de pages, en-têtes et pieds de page | Options Mistral | Non exposés |

Les modèles par défaut, configurables, sont `mistral-ocr-latest` et `gpt-4.1-mini`. Les limites et disponibilités des fournisseurs évoluent indépendamment du package.

Plusieurs fournisseurs peuvent être enregistrés. La sélection passe par `ProviderName` ou un fournisseur par défaut ; aucun basculement automatique n’envoie le document ailleurs.

## Packages et limites

| Package | Rôle |
| --- | --- |
| `TypedDocumentAI.Abstractions` | Contrats, entrées, options et résultats, sans dépendance tierce. |
| `TypedDocumentAI.Core` | Client typé, schémas, validation, routage, DI et transport HTTP. |
| `TypedDocumentAI.Mistral` | Enregistrement et adaptateur Mistral. |
| `TypedDocumentAI.OpenAI` | Enregistrement et adaptateur OpenAI ; Azure OpenAI n’est pas pris en charge. |

Les valeurs par défaut sont 20 MiB par document, 16 MiB par réponse et deux minutes par opération HTTP. Les documents sont chargés en mémoire, avec un coût supplémentaire pour le base64 et les appels concurrents.

Les retries POST sont désactivés par défaut pour limiter les doubles traitements facturés. Les refus, réponses incomplètes et données invalides sont des erreurs. Les objets imbriqués, propriétés modifiables ou `init`, collections génériques et enums textuelles sont pris en charge ; dictionnaires, cycles, polymorphisme, NativeAOT et trimming sont exclus du moteur par défaut.

Les erreurs et traces de la bibliothèque omettent contenus et secrets. `store: false` pour OpenAI ne garantit pas une conservation nulle chez le fournisseur.

## Exemples, aide et contribution

- [OfflineInvoiceDemo](samples/OfflineInvoiceDemo/Program.cs) : extraction typée avec réponse simulée fixe.
- [CustomProviderDemo](samples/CustomProviderDemo/Program.cs) : enregistrement d’un fournisseur `text/plain` et lecture de texte.
- [InvoiceConsole](samples/InvoiceConsole/Program.cs) : appels réels aux fournisseurs, avec `--live` et une clé.

La [facture synthétique](samples/InvoiceConsole/Fixtures/invoice.png) indique `INV-001`, `2026-10-01` et `125.50`. Les sorties réelles peuvent varier ; elles doivent être vérifiées.

```sh
# Chaque commande peut entraîner des frais d’API :
dotnet run --project samples/InvoiceConsole -- mistral samples/InvoiceConsole/Fixtures/invoice.png extract --live
dotnet run --project samples/InvoiceConsole -- openai samples/InvoiceConsole/Fixtures/invoice.png extract --live
```

Maintenance par [VivienMAN](https://github.com/VivienMAN) et les contributeurs. Utiliser les [issues](https://github.com/VivienMAN/TypedDocumentAI/issues) pour les bugs reproductibles et demandes ciblées ; l’assistance dépend de la disponibilité des mainteneurs. Pour une vulnérabilité, suivre [SECURITY.md](SECURITY.md).

```sh
python tools/verify.py
```

Cette commande compile, vérifie les tests et visibilités/API, exécute les démos hors ligne, génère les packages et teste leur installation ainsi que la compilation des exemples documentés. Les workflows restent manuels ; les appels payants sont séparés et soumis à un lancement explicite.

Guides : [schémas](docs/SCHEMAS.md), [opérations](docs/OPERATIONS.md), [extension](docs/EXTENDING.md), [tests](docs/TESTING.md), [validation réelle](docs/VALIDATION.md), [publication](docs/RELEASING.md), [contribution](CONTRIBUTING.md) et [migration du prototype](docs/MIGRATING-V1.md).
