# DeutschLern

DeutschLern یک اپلیکیشن وب آموزش واژگان آلمانی برای فارسی‌زبانان از سطح A1 تا C1 است. مسیر یادگیری مرحله‌ای است: درس اول هر سطح آزاد است و درس بعدی فقط پس از کسب حداقل ۶۰٪ در آزمون درس قبلی باز می‌شود.

## قابلیت‌ها

- ثبت‌نام و ورود دانش‌آموز با ASP.NET Core Identity
- داشبورد پیشرفت، آخرین تلاش و بالاترین نمره
- واژگان همراه نوع کلمه، حرف تعریف، جمع، مثال آلمانی و ترجمه فارسی
- آزمون چندگزینه‌ای با امکان تلاش مجدد و نمایش نتیجه پس از ثبت
- کنترل دسترسی درس بعدی در Backend
- پنل Admin برای مدیریت سطح‌ها، درس‌ها، واژگان، مثال‌ها و آزمون‌ها
- رابط فارسی/آلمانی، Responsive و RTL کامل برای فارسی
- ذخیره تمام تلاش‌های آزمون و پاسخ‌های هر تلاش

## فناوری‌ها

.NET 10، ASP.NET Core Blazor Web App، Entity Framework Core Code First، SQL Server، ASP.NET Core Identity، xUnit، FluentAssertions و GitHub Actions.

## ساختار

```text
DeutschLern.Domain           مدل‌ها و قواعد مستقل دامنه
DeutschLern.Application      use case contractها و قواعد آموزشی
DeutschLern.Infrastructure   EF Core، Fluent API و سرویس یادگیری
DeutschLern.Web              Blazor UI، Identity و endpointها
DeutschLern.UnitTests        تست‌های قواعد کسب‌وکار
DeutschLern.IntegrationTests تست EF، امنیت و جریان آزمون
```

وابستگی‌ها به سمت داخل حرکت می‌کنند. برای خوانایی پروژه از MediatR، CQRS و Generic Repository استفاده نشده است؛ EF Core مستقیماً نقش unit of work را دارد و abstraction فقط در مرز use case یادگیری تعریف شده است.

## پیش‌نیازها

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- SQL Server 2022 یا SQL Server LocalDB
- EF Core CLI:

```powershell
dotnet tool install --global dotnet-ef --version 10.*
```

## تنظیم SQL Server

تنظیم پیش‌فرض Windows از LocalDB و دیتابیس `DeutschLern` استفاده می‌کند. برای SQL Server دیگر، connection string را خارج از Git تنظیم کنید:

```powershell
dotnet user-secrets --project DeutschLern.Web set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=DeutschLern;Trusted_Connection=True;TrustServerCertificate=True"
```

در CI یا سرور می‌توان از متغیر محیطی `ConnectionStrings__DefaultConnection` استفاده کرد. connection string و رمز واقعی نباید در فایل‌های repository قرار گیرند.

## Migration و اجرا

این برنامه برای ساده نگه‌داشتن جداسازی Identity و محتوای آموزشی، دو DbContext روی یک دیتابیس دارد:

```powershell
dotnet restore DeutschLern.slnx --configfile NuGet.Config
dotnet ef database update --project DeutschLern.Infrastructure --startup-project DeutschLern.Web --context LearningDbContext
dotnet ef database update --project DeutschLern.Web --startup-project DeutschLern.Web --context ApplicationDbContext
dotnet run --project DeutschLern.Web
```

Migration اولیه سطوح A1، A2، B1، B2 و C1 را seed می‌کند.

## حساب Admin در Development

اطلاعات Admin در repository ذخیره نمی‌شود. پیش از اولین اجرا آن را با User Secrets تعیین کنید:

```powershell
dotnet user-secrets --project DeutschLern.Web set "DevelopmentAdmin:Email" "admin@example.local"
dotnet user-secrets --project DeutschLern.Web set "DevelopmentAdmin:Password" "یک-رمز-قوی-شخصی"
```

در شروع Development، نقش‌های `Admin` و `Student` ساخته می‌شوند و حساب تنظیم‌شده عضو نقش Admin خواهد شد. کاربران ثبت‌نام‌شده به‌صورت خودکار نقش Student می‌گیرند.

## Build و Test

```powershell
dotnet build DeutschLern.slnx --configuration Release
dotnet test DeutschLern.slnx --configuration Release
```

Workflow گیت‌هاب در هر Push و Pull Request عملیات Restore، Build و Test را روی Windows انجام می‌دهد.

## تصمیم‌های امنیتی و داده

- پنل و endpoint مدیریتی با Role-based Authorization محافظت شده‌اند.
- endpoint ثبت آزمون Antiforgery را اعتبارسنجی می‌کند.
- DTO آزمون پیش از Submit شامل `IsCorrect` یا شناسه پاسخ صحیح نیست.
- انتخاب گزینه، تعلق سؤال به آزمون و دسترسی به درس در سرور اعتبارسنجی می‌شوند.
- محدودیت طول، indexهای یکتا، precision و delete behaviorها با Fluent API تعریف شده‌اند.
- هر تلاش آزمون مستقل ذخیره می‌شود؛ بالاترین نمره کاهش نمی‌یابد و آخرین تلاش جداگانه نگهداری می‌شود.
