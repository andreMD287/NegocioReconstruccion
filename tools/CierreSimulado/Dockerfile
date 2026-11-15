FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion

WORKDIR /fuente

COPY tools/CierreSimulado/CierreSimulado.csproj tools/CierreSimulado/

COPY contracts/proto/reconstruccion/v1/cierre.proto contracts/proto/reconstruccion/v1/

RUN dotnet restore tools/CierreSimulado/CierreSimulado.csproj

COPY tools/CierreSimulado/ tools/CierreSimulado/

RUN dotnet publish tools/CierreSimulado/CierreSimulado.csproj \
    -c Release \
    -o /publicado \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=compilacion /publicado ./

EXPOSE 56051
EXPOSE 56052

ENTRYPOINT ["dotnet", "CierreSimulado.dll"]