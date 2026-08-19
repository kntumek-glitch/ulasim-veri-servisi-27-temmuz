FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Sadece csproj dosyasini kopyalayıp dependency'leri indiriyoruz (caching icin)
COPY ["ulasim-veri-servisi.csproj", "./"]
RUN dotnet restore "ulasim-veri-servisi.csproj"

# Kalan dosyalari kopyalayip publish aliyoruz
COPY . .
RUN dotnet publish "ulasim-veri-servisi.csproj" -c Release -o /app/publish

# Calisma ortami icin runtime imaji
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Build asamasindaki dosyalari runtime imajina tasiyoruz
COPY --from=build /app/publish .

# Uygulamayi baslatiyoruz
ENTRYPOINT ["dotnet", "ulasim-veri-servisi.dll"]
