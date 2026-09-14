using System.Text.Json.Serialization;

namespace Checkin.Infrastructure.Wellhub.Dtos;

// DTOs fiéis ao contrato documentado em
// https://developers.wellhub.com/product/access-control-api/1.0/endpoints

public record ValidateAccessRequest(
    [property: JsonPropertyName("gympass_id")] string GympassId,
    [property: JsonPropertyName("custom_code")] string? CustomCode);

public record ValidateAccessResponse(
    [property: JsonPropertyName("metadata")] ValidateAccessMetadata? Metadata,
    [property: JsonPropertyName("results")] ValidateAccessResults? Results,
    // Confirmado ao vivo contra o Sandbox (apitesting.partners.gympass.com) em 2026-09-10: uma
    // resposta de erro (ex.: 404) vem como {"metadata":{"total":0,"errors":1},"errors":[{"message":"...","key":"..."}]}.
    [property: JsonPropertyName("errors")] IReadOnlyList<ValidateAccessError>? Errors);

public record ValidateAccessMetadata(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("errors")] int Errors);

public record ValidateAccessError(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("key")] string? Key);

public record ValidateAccessResults(
    [property: JsonPropertyName("user")] ValidateAccessUser? User,
    [property: JsonPropertyName("gym")] ValidateAccessGym? Gym,
    [property: JsonPropertyName("validated_at")] DateTime? ValidatedAt);

public record ValidateAccessUser([property: JsonPropertyName("gympass_id")] string GympassId);

public record ValidateAccessGym(
    [property: JsonPropertyName("Id")] long Id,
    [property: JsonPropertyName("product")] ValidateAccessProduct? Product);

public record ValidateAccessProduct(
    [property: JsonPropertyName("Id")] long Id,
    [property: JsonPropertyName("description")] string? Description);

public record CustomCodeRequest([property: JsonPropertyName("custom_code")] string CustomCode);
