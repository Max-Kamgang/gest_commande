# syntax=docker/dockerfile:1
# Image Windows (nanoserver) du service gateway - build multi-etapes.
# Le contexte de build est la RACINE du depot : la bibliotheque BuildingBlocks et les
# proprietes communes (Directory.Build.props / Directory.Packages.props) y sont necessaires.
#
# Tags parametrables :
#   docker build --build-arg SDK_IMAGE=... --build-arg RUNTIME_IMAGE=... -f src/Gateway/Dockerfile .

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-nanoserver-ltsc2025
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-nanoserver-ltsc2025

# ---------------------------------------------------------------------------
# Etape 1 : restauration et publication
# ---------------------------------------------------------------------------
FROM ${SDK_IMAGE} AS build
WORKDIR C:\src

# Les fichiers projet sont copies avant le code : la couche de restauration NuGet
# n'est reconstruite que lorsque les dependances changent.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/GestionCommandes.BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Gateway/GestionCommandes.Gateway.csproj src/Gateway/
RUN dotnet restore src/Gateway/GestionCommandes.Gateway.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Gateway/ src/Gateway/
RUN dotnet publish src/Gateway/GestionCommandes.Gateway.csproj -c Release -o C:\app --no-restore -p:UseAppHost=false

# ---------------------------------------------------------------------------
# Etape 2 : image d'execution (ASP.NET Core runtime, nanoserver)
# ---------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS final
WORKDIR C:\app

# Le service ecoute sur 8080 a l'interieur du conteneur.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__DataPath=C:\app\data \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

# La gateway expose aussi HTTPS (8443 dans le conteneur, 443 sur l'hote). Le certificat
# .pfx est monte en volume ; le mot de passe arrive par l'environnement (voir .env).
ENV ASPNETCORE_URLS=http://+:8080;https://+:8443
EXPOSE 8443

# Les images nanoserver tournent sous ContainerUser. Ce compte doit pouvoir ECRIRE dans
# le dossier de donnees (base SQLite et journaux). Sans cette ligne, la sonde /health/ready
# et les migrations echouent au premier demarrage (AccessDenied). Le droit est accorde
# explicitement a ce dossier seulement, le reste de C:\app reste en lecture seule.
RUN mkdir C:\app\data && icacls C:\app\data /grant "ContainerUser:(OI)(CI)M"

COPY --from=build C:\app .

USER ContainerUser
ENTRYPOINT ["dotnet", "GestionCommandes.Gateway.dll"]
