FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion

WORKDIR /fuente

COPY contracts/ contracts/
COPY src/ src/

RUN dotnet restore src/Reconstruccion.Host/Reconstruccion.Host.csproj

RUN dotnet publish src/Reconstruccion.Host/Reconstruccion.Host.csproj \
    -c Release \
    -o /publicado \
    --no-restore

# Certificado autofirmado solo para habilitar HTTPS/HTTP2 dentro del contenedor: el
# ClienteDemo no valida la cadena de confianza (ver tools/ClienteDemo/Program.cs), así que
# no necesita ser el certificado de desarrollo de dotnet dev-certs ni estar en el almacén
# de confianza del sistema.
RUN mkdir -p /publicado/https \
    && dotnet dev-certs https -ep /publicado/https/aspnetapp.pfx -p "demo-local-no-sensible"

FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

COPY --from=compilacion /publicado ./

ENV ASPNETCORE_URLS=https://+:8081
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/https/aspnetapp.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=demo-local-no-sensible

EXPOSE 8081

ENTRYPOINT ["dotnet", "Reconstruccion.Host.dll"]
