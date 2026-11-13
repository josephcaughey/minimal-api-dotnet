FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Bookings.Api/Bookings.Api.csproj src/Bookings.Api/
RUN dotnet restore src/Bookings.Api/Bookings.Api.csproj
COPY src/ src/
RUN dotnet publish src/Bookings.Api/Bookings.Api.csproj -c Release -o /app --no-restore \
    && mkdir /data

# Chiseled images have no shell or package manager and run as a non-root
# user (UID 1654), so the data directory is created above and handed over here.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=1654:1654 /data /data
ENV ConnectionStrings__Bookings="Data Source=/data/bookings.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bookings.Api.dll"]
