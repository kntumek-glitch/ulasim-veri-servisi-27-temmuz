FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src

# Restore and build backend
COPY ["ulasim-veri-servisi.csproj", "./"]
RUN dotnet restore "ulasim-veri-servisi.csproj"
COPY . .
RUN dotnet publish "ulasim-veri-servisi.csproj" -c Release -o /app/publish

# Build frontend (web-ui) using Node
FROM node:20-alpine AS frontend-build
WORKDIR /web
COPY web-ui/package*.json ./
RUN npm install
COPY web-ui/ ./
RUN npm run build

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy backend publish
COPY --from=backend-build /app/publish .
# Copy frontend static files into wwwroot
COPY --from=frontend-build /web/dist ./wwwroot

ENTRYPOINT ["dotnet", "ulasim-veri-servisi.dll"]
