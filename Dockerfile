# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SreTakeHome.sln global.json ./
COPY src/CandidateApi/CandidateApi.csproj src/CandidateApi/
COPY src/CandidateApi.Contracts/CandidateApi.Contracts.csproj src/CandidateApi.Contracts/
COPY tests/CandidateApi.Tests/CandidateApi.Tests.csproj tests/CandidateApi.Tests/

RUN dotnet restore SreTakeHome.sln

COPY . .

ARG VERSION=1.0.0
RUN dotnet publish src/CandidateApi/CandidateApi.csproj \
    -c Release \
    -o /app/publish \
    /p:Version=${VERSION} \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CandidateApi.dll"]
