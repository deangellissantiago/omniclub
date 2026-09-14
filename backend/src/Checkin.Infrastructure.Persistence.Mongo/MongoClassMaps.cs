using Checkin.Domain.Entities;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Checkin.Infrastructure.Persistence.Mongo;

/// <summary>
/// Registra o mapeamento BSON das entidades de domínio em código, para manter o Domain
/// livre de atributos/dependências do MongoDB.Driver (arquitetura hexagonal).
/// </summary>
public static class MongoClassMaps
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        RegisterMap<Tenant>();
        RegisterMap<AdminUser>();
        RegisterMap<Student>();
        RegisterMap<CheckinPoint>();
        RegisterMap<CheckinRecord>();
        RegisterMap<WellhubClass>();
        RegisterMap<Booking>();

        // ClassSlot.AvailableSpots é calculado (Capacity - BookedCount), não um campo próprio —
        // não pode passar por RegisterMap<T> genérico porque precisa de UnmapProperty.
        if (!BsonClassMap.IsClassMapRegistered(typeof(ClassSlot)))
        {
            BsonClassMap.RegisterClassMap<ClassSlot>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(typeof(ClassSlot).GetProperty(nameof(ClassSlot.Id))!).SetSerializer(new StringSerializer());
                cm.UnmapProperty(nameof(ClassSlot.AvailableSpots));
                cm.SetIgnoreExtraElements(true);
            });
        }
    }

    private static void RegisterMap<T>()
    {
        if (BsonClassMap.IsClassMapRegistered(typeof(T))) return;

        BsonClassMap.RegisterClassMap<T>(cm =>
        {
            cm.AutoMap();
            cm.MapIdMember(typeof(T).GetProperty("Id")!).SetSerializer(new StringSerializer());
            cm.SetIgnoreExtraElements(true);
        });
    }
}
