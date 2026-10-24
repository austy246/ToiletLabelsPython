# syntax=docker/dockerfile:1
# Build context is the repo root: docker build -f src/ToiletLabels/Dockerfile .
# Base images are pinned by digest. To bump: `docker buildx imagetools inspect <image:tag>` and copy its Digest here.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ToiletLabels/ToiletLabels.csproj src/ToiletLabels/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/ToiletLabels/ToiletLabels.csproj -r linux-x64
COPY src/ToiletLabels/ src/ToiletLabels/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/ToiletLabels/ToiletLabels.csproj -c Release -r linux-x64 --no-self-contained --no-restore -o /app

# Runtime: 10.0-noble-chiseled-extra. The plain chiseled image failed `--selftest` (Countries.Normalize did not fold
# diacritics because it ships without ICU and runs in invariant-globalization mode); -extra adds ICU.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:9e3919aff68057f3e45125c5255a71d3b5f40364e45ede883348688851b9f0df
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ToiletLabels.dll"]
