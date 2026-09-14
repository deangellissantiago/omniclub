using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly MongoContext _context;

    public BookingRepository(MongoContext context) => _context = context;

    public async Task<Booking> CreateAsync(Booking booking, CancellationToken ct = default)
    {
        await _context.Bookings.InsertOneAsync(booking, cancellationToken: ct);
        return booking;
    }

    public async Task<bool> UpdateAsync(Booking booking, CancellationToken ct = default)
    {
        var result = await _context.Bookings.ReplaceOneAsync(
            b => b.TenantId == booking.TenantId && b.Id == booking.Id, booking, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<Booking?> FindByExternalBookingIdAsync(string tenantId, string externalBookingId, CancellationToken ct = default) =>
        await _context.Bookings.Find(b => b.TenantId == tenantId && b.ExternalBookingId == externalBookingId).FirstOrDefaultAsync(ct);
}
