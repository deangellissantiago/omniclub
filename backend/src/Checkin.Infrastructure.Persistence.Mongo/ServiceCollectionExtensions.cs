using Checkin.Application.Ports.Repositories;
using Checkin.Infrastructure.Persistence.Mongo.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkin.Infrastructure.Persistence.Mongo;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongoPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        MongoClassMaps.Register();

        services.Configure<MongoDbSettings>(configuration.GetSection("MongoDb"));
        services.AddSingleton<MongoContext>();

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<ICheckinPointRepository, CheckinPointRepository>();
        services.AddScoped<ICheckinRecordRepository, CheckinRecordRepository>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IClassSlotRepository, ClassSlotRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();

        return services;
    }
}
