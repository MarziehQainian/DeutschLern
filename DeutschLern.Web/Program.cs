using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DeutschLern.Web.Components;
using DeutschLern.Web.Components.Account;
using DeutschLern.Web.Data;
using DeutschLern.Application;
using DeutschLern.Infrastructure;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[] { new CultureInfo("fa"), new CultureInfo("de") };
    options.DefaultRequestCulture = new RequestCulture("fa");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
});
builder.Services.AddLearningInfrastructure(builder.Configuration);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = !builder.Environment.IsDevelopment();
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapGet("/culture", (string value, string? redirectUri, HttpContext context) =>
{
    if (value is not ("fa" or "de"))
    {
        return Results.BadRequest();
    }

    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(value)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });
    return Results.LocalRedirect(string.IsNullOrWhiteSpace(redirectUri) ? "/" : redirectUri);
});
app.MapGet("/admin/ping", () => Results.Ok(new { status = "ok" }))
    .RequireAuthorization(policy => policy.RequireRole("Admin"));
app.MapGet("/api/quizzes/{lessonId:int}", async (
    int lessonId,
    ILearningService learningService,
    System.Security.Claims.ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
    return Results.Ok(await learningService.GetQuizAsync(lessonId, userId, cancellationToken));
}).RequireAuthorization();
app.MapPost("/api/quizzes/{lessonId:int}/submit", async (
    int lessonId,
    IReadOnlyCollection<QuizAnswerInput> answers,
    ILearningService learningService,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    HttpContext httpContext,
    System.Security.Claims.ClaimsPrincipal principal,
    CancellationToken cancellationToken) =>
{
    await antiforgery.ValidateRequestAsync(httpContext);
    var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
    return Results.Ok(await learningService.SubmitQuizAsync(lessonId, userId, answers, cancellationToken));
}).RequireAuthorization();

if (!app.Environment.IsEnvironment("Testing"))
{
    await SeedIdentityAsync(app.Services, app.Configuration, app.Environment);
}

app.Run();

static async Task SeedIdentityAsync(
    IServiceProvider services,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    await using var scope = services.CreateAsyncScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Student" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    if (!environment.IsDevelopment())
    {
        return;
    }

    var email = configuration["DevelopmentAdmin:Email"];
    var password = configuration["DevelopmentAdmin:Password"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return;
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var admin = await userManager.FindByEmailAsync(email);
    if (admin is null)
    {
        admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
    }

    if (!await userManager.IsInRoleAsync(admin, "Admin"))
    {
        await userManager.AddToRoleAsync(admin, "Admin");
    }
}

public partial class Program;
