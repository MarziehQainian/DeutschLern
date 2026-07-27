using DeutschLern.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeutschLern.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLearningInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
        services.AddDbContext<LearningDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ILearningService, LearningService>();
        return services;
    }
}
