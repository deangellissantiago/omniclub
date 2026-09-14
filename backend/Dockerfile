FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Checkin.Domain/Checkin.Domain.csproj Checkin.Domain/
COPY src/Checkin.Application/Checkin.Application.csproj Checkin.Application/
COPY src/Checkin.Infrastructure.Persistence.Mongo/Checkin.Infrastructure.Persistence.Mongo.csproj Checkin.Infrastructure.Persistence.Mongo/
COPY src/Checkin.Infrastructure.Wellhub/Checkin.Infrastructure.Wellhub.csproj Checkin.Infrastructure.Wellhub/
COPY src/Checkin.Infrastructure.Stripe/Checkin.Infrastructure.Stripe.csproj Checkin.Infrastructure.Stripe/
COPY src/Checkin.Api/Checkin.Api.csproj Checkin.Api/
RUN dotnet restore Checkin.Api/Checkin.Api.csproj

COPY src/ .
RUN dotnet publish Checkin.Api/Checkin.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Checkin.Api.dll"]
