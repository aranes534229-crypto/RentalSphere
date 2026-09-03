# Multi-stage build: compile in one image, run in a smaller one.
# Render deploys this as a Docker container.

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file first so Docker caches the NuGet restore layer.
COPY RentalSphere.csproj ./
RUN dotnet restore RentalSphere.csproj

# Copy the rest of the source code.
COPY . ./
RUN dotnet publish RentalSphere.csproj -c Release -o /app /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Render sets the PORT env var; listen on it on all interfaces.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
CMD ["sh", "-c", "dotnet RentalSphere.dll --urls=http://0.0.0.0:$PORT"]
