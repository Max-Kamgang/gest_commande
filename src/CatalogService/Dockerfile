# escape=`
# Image Windows du service CatalogService, en deux etapes.
# A construire depuis la RACINE du depot : le projet Shared et les fichiers
# Directory.*.props y sont necessaires.
#   docker build -f src/CatalogService/Dockerfile .
# Les tags des images de base viennent du fichier .env (SDK_IMAGE / RUNTIME_IMAGE).

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-nanoserver-ltsc2025
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-nanoserver-ltsc2025

# --- Etape 1 : restauration puis publication --------------------------------
FROM ${SDK_IMAGE} AS build
WORKDIR C:\src

# Les .csproj sont copies avant le code : la couche NuGet n'est reconstruite que
# lorsque les dependances changent, pas a chaque modification de code.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/GestionCommandes.Shared.csproj src/Shared/
COPY src/CatalogService/GestionCommandes.CatalogService.csproj src/CatalogService/
RUN dotnet restore src/CatalogService/GestionCommandes.CatalogService.csproj

COPY src/Shared/ src/Shared/
COPY src/CatalogService/ src/CatalogService/
RUN dotnet publish src/CatalogService/GestionCommandes.CatalogService.csproj -c Release -o C:\app --no-restore

# --- Etape 2 : image d'execution --------------------------------------------
FROM ${RUNTIME_IMAGE} AS final
WORKDIR C:\app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Storage__DataPath=C:\app\data

EXPOSE 8080

# Les images nanoserver tournent sous ContainerUser, qui ne peut pas ecrire
# partout : on lui accorde explicitement le droit d'ecrire dans le dossier de
# donnees (base SQLite). Sans cette ligne, le premier demarrage echoue.
RUN mkdir C:\app\data && icacls C:\app\data /grant "ContainerUser:(OI)(CI)M"

COPY --from=build C:\app .

USER ContainerUser
ENTRYPOINT ["dotnet", "GestionCommandes.CatalogService.dll"]
