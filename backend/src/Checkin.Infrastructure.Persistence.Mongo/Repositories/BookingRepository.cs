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

    public async Task<IReadOnlyList<Booking>> ListAsync(string tenantId, DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var builder = Builders<Booking>.Filter;
        var filter = builder.Eq(b => b.TenantId, tenantId);

        if (start.HasValue) filter &= builder.Gte(b => b.RequestedAt, start.Value);
        if (end.HasValue) filter &= builder.Lte(b => b.RequestedAt, end.Value);

        return await _context.Bookings.Find(filter).SortByDescending(b => b.RequestedAt).ToListAsync(ct);
    }
}
