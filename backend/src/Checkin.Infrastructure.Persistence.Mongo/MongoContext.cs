using Checkin.Domain.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo;

public class MongoContext
{
    private readonly IMongoDatabase _database;

    public MongoContext(IOptions<MongoDbSettings> options)
    {
        var client = new MongoClient(options.Value.ConnectionString);
        _database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoCollection<Tenant> Tenants => _database.GetCollection<Tenant>("tenants");
    public IMongoCollection<AdminUser> AdminUsers => _database.GetCollection<AdminUser>("admin_users");
    public IMongoCollection<Student> Students => _database.GetCollection<Student>("students");
    public IMongoCollection<CheckinPoint> CheckinPoints => _database.GetCollection<CheckinPoint>("checkin_points");
    public IMongoCollection<CheckinRecord> CheckinRecords => _database.GetCollection<CheckinRecord>("checkin_records");
    public IMongoCollection<WellhubClass> Classes => _database.GetCollection<WellhubClass>("classes");
    public IMongoCollection<ClassSlot> ClassSlots => _database.GetCollection<ClassSlot>("class_slots");
    public IMongoCollection<Booking> Bookings => _database.GetCollection<Booking>("bookings");
}
