# DeutschLern

DeutschLern is a German vocabulary learning web application designed for Persian speakers, covering levels A1 through C1. The learning path is progressive: the first lesson of each level is unlocked by default, while subsequent lessons become available only after the student scores at least 60% on the previous lesson’s quiz.

## Features

* Student registration and authentication with ASP.NET Core Identity
* Progress dashboard showing the latest attempt and highest score
* Vocabulary entries with word type, article, plural form, German example sentences, and Persian translations
* Multiple-choice quizzes with retry support and results displayed after submission
* Backend enforcement of lesson progression rules
* Admin panel for managing levels, lessons, vocabulary, examples, and quizzes
* Responsive Persian/German interface with full RTL support for Persian
* Storage of all quiz attempts and the submitted answers for each attempt

## Technologies

.NET 10, ASP.NET Core Blazor Web App, Entity Framework Core Code First, SQL Server, ASP.NET Core Identity, xUnit, FluentAssertions, and GitHub Actions.

## Project Structure

```text
DeutschLern.Domain            Domain models and independent business rules
DeutschLern.Application       Use-case contracts and learning rules
DeutschLern.Infrastructure    EF Core, Fluent API, and learning services
DeutschLern.Web               Blazor UI, Identity, and endpoints
DeutschLern.UnitTests         Business-rule tests
DeutschLern.IntegrationTests  EF Core, security, and quiz-flow tests
```

Dependencies point inward. MediatR, CQRS, and a generic repository have intentionally not been used to keep the project easy to understand. EF Core directly provides unit-of-work functionality, while abstractions are defined only at the boundaries of learning use cases.

## Prerequisites

* .NET 10 SDK
* SQL Server 2022 or SQL Server LocalDB
* EF Core CLI:

```bash
dotnet tool install --global dotnet-ef --version 10.*
```

## SQL Server Configuration

The default Windows configuration uses LocalDB with a database named `DeutschLern`. To use another SQL Server instance, configure the connection string outside Git:

```bash
dotnet user-secrets --project DeutschLern.Web set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=DeutschLern;Trusted_Connection=True;TrustServerCertificate=True"
```

In CI or server environments, the `ConnectionStrings__DefaultConnection` environment variable can be used. Real connection strings and passwords must not be stored in repository files.

## Migrations and Running the Application

To keep Identity and learning content separated, the application uses two `DbContext` classes connected to the same database:

```bash
dotnet restore DeutschLern.slnx --configfile NuGet.Config

dotnet ef database update \
  --project DeutschLern.Infrastructure \
  --startup-project DeutschLern.Web \
  --context LearningDbContext

dotnet ef database update \
  --project DeutschLern.Web \
  --startup-project DeutschLern.Web \
  --context ApplicationDbContext

dotnet run --project DeutschLern.Web
```

The initial migration seeds the A1, A2, B1, B2, and C1 language levels.

## Development Admin Account

Admin credentials are not stored in the repository. Configure them with User Secrets before the first run:

```bash
dotnet user-secrets --project DeutschLern.Web set "DevelopmentAdmin:Email" "admin@example.local"

dotnet user-secrets --project DeutschLern.Web set "DevelopmentAdmin:Password" "your-personal-strong-password"
```

When the application starts in the Development environment, the `Admin` and `Student` roles are created. The configured development account is assigned to the `Admin` role. Newly registered users are automatically assigned to the `Student` role.

## Build and Test

```bash
dotnet build DeutschLern.slnx --configuration Release
dotnet test DeutschLern.slnx --configuration Release
```

The GitHub Actions workflow runs Restore, Build, and Test on Windows for every push and pull request.

## Security and Data Design Decisions

* The Admin panel and administrative endpoints are protected with role-based authorization.
* The quiz submission endpoint validates antiforgery tokens.
* Quiz DTOs do not expose `IsCorrect` or the correct option ID before submission.
* The server validates selected options, question–quiz relationships, and lesson access.
* String-length limits, unique indexes, numeric precision, and delete behaviors are configured with the EF Core Fluent API.
* Every quiz attempt is stored independently.
* A student’s highest score never decreases, while the latest attempt is tracked separately.
