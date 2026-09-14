using System.Text.Json.Serialization;

namespace Checkin.Infrastructure.Wellhub.Dtos;

// DTOs da Booking API — confirmados em 2026-09-10 contra os requests reais da collection do
// Postman do parceiro ("Old - Gympass Quick Start Guide - Booking & Access Control API Copy"),
// não mais um palpite. Paths: ver WellhubBookingGatewayAdapter.

public record WellhubProductsResponse(
    [property: JsonPropertyName("gym_id")] long GymId,
    [property: JsonPropertyName("products")] IReadOnlyList<WellhubProductItem>? Products);

public record WellhubProductItem(
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("virtual")] bool Virtual);

public record CreateClassesRequest([property: JsonPropertyName("classes")] IReadOnlyList<CreateClassItem> Classes);

public record CreateClassItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("bookable")] bool Bookable,
    [property: JsonPropertyName("visible")] bool Visible,
    [property: JsonPropertyName("is_virtual")] bool IsVirtual,
    [property: JsonPropertyName("product_id")] long ProductId);

// Confirmado ao vivo em 2026-09-10 (POST real contra o Sandbox): {"classes":[{"id":...,"name":...,"links":[...]}]}.
public record CreateClassesResponse([property: JsonPropertyName("classes")] IReadOnlyList<CreatedClassItem>? Classes);

public record CreatedClassItem([property: JsonPropertyName("id")] long Id);

public record CreateSlotRequest(
    [property: JsonPropertyName("occur_date")] DateTime OccurDate,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("room")] string Room,
    [property: JsonPropertyName("length_in_minutes")] int LengthInMinutes,
    [property: JsonPropertyName("total_capacity")] int TotalCapacity,
    [property: JsonPropertyName("total_booked")] int TotalBooked,
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("booking_window")] BookingWindow BookingWindow,
    [property: JsonPropertyName("cancellable_until")] DateTime CancellableUntil,
    [property: JsonPropertyName("instructors")] IReadOnlyList<object> Instructors,
    [property: JsonPropertyName("rate")] decimal Rate);

public record BookingWindow(
    [property: JsonPropertyName("opens_at")] DateTime OpensAt,
    [property: JsonPropertyName("closes_at")] DateTime ClosesAt);

// Confirmado ao vivo em 2026-09-10 (POST real contra o Sandbox): mesmo envelope metadata/results
// do /access/v1/validate — {"metadata":{...},"results":[{"id":...,"class_id":...,...}]}.
public record CreateSlotResponse(
    [property: JsonPropertyName("metadata")] object? Metadata,
    [property: JsonPropertyName("results")] IReadOnlyList<CreatedSlotItem>? Results);

public record CreatedSlotItem([property: JsonPropertyName("id")] long Id);

public record PatchSlotVacancyRequest(
    [property: JsonPropertyName("total_capacity")] int TotalCapacity,
    [property: JsonPropertyName("total_booked")] int TotalBooked);

public record ValidateBookingRequest(
    [property: JsonPropertyName("class_id")] long ClassId,
    [property: JsonPropertyName("status")] int Status);
