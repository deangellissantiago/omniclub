using MongoDB.Bson;
using MongoDB.Driver;

namespace Checkin.Infrastructure.Persistence.Mongo;

/// <summary>
/// Ajustes de dados idempotentes, rodados na subida da API (antes do seed).
/// </summary>
public static class MongoMigrations
{
    public static async Task RunAsync(MongoContext context, CancellationToken ct = default)
    {
        await MigrateSingleAppCheckinPointsAsync(context, ct);
    }

    /// <summary>
    /// 2026-11: ponto de check-in deixou de ter um único "App" — a mesma unidade pode aceitar
    /// Wellhub e TotalPass. Pontos antigos de TotalPass guardavam o id da unidade em ExternalId
    /// (agora reservado ao Gym ID do Wellhub) e o preço em PricePerCheckinCents (agora o do
    /// Wellhub): move os dois para os campos do TotalPass. Depois remove o campo "App" de todos.
    /// Idempotente: depois de rodar, nenhum documento tem mais "App".
    /// </summary>
    private static async Task MigrateSingleAppCheckinPointsAsync(MongoContext context, CancellationToken ct)
    {
        var points = context.CheckinPoints.Database.GetCollection<BsonDocument>("checkin_points");
        const int legacyTotalPass = 2; // IntegrationApp.TotalPass, gravado como inteiro

        var totalPassPipeline = new EmptyPipelineDefinition<BsonDocument>()
            .AppendStage<BsonDocument, BsonDocument, BsonDocument>(new BsonDocument("$set", new BsonDocument
            {
                { "TotalPassPlaceId", new BsonDocument("$cond", new BsonArray { new BsonDocument("$eq", new BsonArray { "$ExternalId", "" }), BsonNull.Value, "$ExternalId" }) },
                { "TotalPassPricePerCheckinCents", "$PricePerCheckinCents" },
                { "ExternalId", "" },
                { "PricePerCheckinCents", BsonNull.Value },
            }))
            .AppendStage<BsonDocument, BsonDocument, BsonDocument>(new BsonDocument("$unset", "App"));

        await points.UpdateManyAsync(Builders<BsonDocument>.Filter.Eq("App", legacyTotalPass),
            Builders<BsonDocument>.Update.Pipeline(totalPassPipeline), cancellationToken: ct);

        await points.UpdateManyAsync(Builders<BsonDocument>.Filter.Exists("App"),
            Builders<BsonDocument>.Update.Unset("App"), cancellationToken: ct);
    }
}
