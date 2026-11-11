namespace Checkin.Application.DTOs.CheckinPoints;

/// <summary>Cadastro/edição de uma unidade. Pelo menos um app precisa estar ativo.</summary>
public record UpsertCheckinPointRequest(
    string Name,
    bool Active,
    /// <summary>Gym ID do Wellhub. Vazio = a unidade não aceita Wellhub.</summary>
    string? ExternalId = null,
    /// <summary>Repasse do Wellhub por check-in. Nulo = "ainda não configurado".</summary>
    int? PricePerCheckinCents = null,
    /// <summary>A unidade aceita TotalPass. Desligar apaga as chaves e a conexão do TotalPass.</summary>
    bool TotalPassEnabled = false,
    /// <summary>Chave da unidade no TotalPass ("place_api_key"). Nula/vazia na edição = mantém a
    /// atual (o painel nunca recebe a chave de volta, só os 4 últimos caracteres).</summary>
    string? TotalPassPlaceApiKey = null,
    /// <summary>Repasse do TotalPass por check-in. Nulo = "ainda não configurado".</summary>
    int? TotalPassPricePerCheckinCents = null);
