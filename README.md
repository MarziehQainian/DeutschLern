# DeutschLern

DeutschLern is a German vocabulary learning web application designed for Persian speakers, covering CEFR levels A1 through C1. The learning path is sequential: the first lesson of each level is available immediately, while each following lesson is unlocked only after the student scores at least 60% on the previous lesson's quiz.

## Features

- Student registration and authentication with ASP.NET Core Identity
- Progress dashboard with the latest attempt and highest score
- German vocabulary with word type, noun article, plural form, example sentence, and Persian translation
- Multiple-choice quizzes with retry support and post-submission feedback
- Server-side lesson access enforcement
- Admin panel for managing levels, lessons, vocabulary, examples, and quizzes
- Responsive Persian and German interface with full RTL support for Persian
- Persistent quiz attempts and per-answer results

## Technology

- .NET 10
- ASP.NET Core Blazor Web App
- Entity Framework Core Code First
- SQL Server
- ASP.NET Core Identity
- xUnit and FluentAssertions
- GitHub Actions

## Solution Structure

```text
DeutschLern.Domain           Domain models and independent business rules
DeutschLern.Application      Use-case contracts and learning rules
DeutschLern.Infrastructure   EF Core, Fluent API, and learning services
DeutschLern.Web              Blazor UI, Identity, and HTTP endpoints
DeutschLern.UnitTests        Business rule tests
DeutschLern.IntegrationTests EF, security, seeding, and quiz workflow tests
```

Dependencies point inward. The project intentionally avoids MediatR, CQRS, and a generic Repository Pattern. EF Core acts as the unit of work, and an interface is introduced only at the learning use-case boundary.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- SQL Server 2022 or SQL Server LocalDB
- EF Core CLI:

```powershell
dotnet tool install --global dotnet-ef --version 10.*
```

## SQL Server Configuration

The default Windows configuration uses LocalDB and a database named `DeutschLern`. To use another SQL Server instance, store the connection string outside Git:

```powershell
dotnet user-secrets --project DeutschLern.Web set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=DeutschLern;Trusted_Connection=True;TrustServerCertificate=True"
```

For CI or server environments, use the `ConnectionStrings__DefaultConnection` environment variable. Real connection strings and credentials must not be committed to the repository.

## Migrations and Application Startup

The application uses two DbContexts against the same database to keep Identity and learning data clearly separated:

```powershell
dotnet restore DeutschLern.slnx --configfile NuGet.Config
dotnet ef database update --project DeutschLern.Infrastructure --startup-project DeutschLern.Web --context LearningDbContext
dotnet ef database update --project DeutschLern.Web --startup-project DeutschLern.Web --context ApplicationDbContext
dotnet run --project DeutschLern.Web
```

The initial learning migration seeds the A1, A2, B1, B2, and C1 levels.

In Development, an idempotent sample dataset is also created on first startup:

- 10 ordered lessons
- 30 vocabulary entries
- 30 German example sentences with Persian translations
- 10 quizzes
- 30 quiz questions
- 120 answer options

Restarting the application does not duplicate the sample data. Development data is never seeded in Production.

## Development Admin Account

Admin credentials are not stored in the repository. Configure them through User Secrets before the first Development run:

```powershell
dotnet user-secrets --project DeutschLern.Web set "DevelopmentAdmin:Email" "admin@example.local"
dotnet user-secrets --project DeutschLern.Web set "DevelopmentAdmin:Password" "your-own-strong-password"
```

On Development startup, the `Admin` and `Student` roles are created. The configured account is assigned to the Admin role, while newly registered users are automatically assigned to the Student role.

## Build and Test

```powershell
dotnet build DeutschLern.slnx --configuration Release
dotnet test DeutschLern.slnx --configuration Release
```

The GitHub Actions workflow restores, builds, and tests the complete solution on every push and pull request.

## Security and Data Decisions

- Admin pages and endpoints use role-based authorization.
- Quiz submission endpoints validate antiforgery tokens.
- Pre-submission quiz DTOs never include `IsCorrect` or a correct option identifier.
- Option ownership, question membership, and lesson access are validated on the server.
- String lengths, unique indexes, decimal precision, relationships, and delete behavior are configured with Fluent API.
- Every quiz attempt is stored independently.
- A student's highest score never decreases, while the latest attempt is tracked separately.
- Every seeded vocabulary entry contains at least one example sentence.
- Every seeded lesson contains a quiz.
