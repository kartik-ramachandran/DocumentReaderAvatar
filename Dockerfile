# Stage 1: Build Vue frontend (VITE_API_BASE empty = relative URLs, nginx proxies /api/)
FROM node:20-alpine AS web-build
WORKDIR /web
COPY web/package*.json ./
RUN npm install
COPY web/ .
RUN VITE_API_BASE="" npm run build

# Stage 2: Build .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY api/AvatarDocReader.Api.csproj ./
RUN dotnet restore
COPY api/ .
RUN dotnet publish -c Release -o /app/publish

# Stage 3: Combined runtime — dotnet runtime + nginx
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y nginx && rm -rf /var/lib/apt/lists/* \
    && rm -f /etc/nginx/sites-enabled/default

WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=web-build /web/dist /usr/share/nginx/html
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY start.sh /start.sh
RUN chmod +x /start.sh

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 80

ENTRYPOINT ["/start.sh"]
