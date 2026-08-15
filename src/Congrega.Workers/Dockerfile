# =============================================================================
# Congrega.Workers — imagem multi-stage, execução como usuário não-root
# Build a partir da RAIZ do repositório:
#   docker build -f src/Congrega.Workers/Dockerfile -t congrega/workers:local .
# =============================================================================

# ---------- Estágio 1: restore ----------
# Copiar apenas os arquivos de projeto antes do código-fonte faz o restore virar
# uma camada cacheada: alterar um .cs não invalida o download de pacotes.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS restore
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Congrega.Domain/*.csproj                     src/Congrega.Domain/
COPY src/Congrega.Application/*.csproj                src/Congrega.Application/
COPY src/Congrega.Infrastructure/*.csproj             src/Congrega.Infrastructure/
COPY src/Congrega.Workers/*.csproj                    src/Congrega.Workers/

RUN dotnet restore src/Congrega.Workers/Congrega.Workers.csproj

# ---------- Estágio 2: build e publish ----------
FROM restore AS publish
COPY src/ src/

RUN dotnet publish src/Congrega.Workers/Congrega.Workers.csproj \
        --configuration Release \
        --no-restore \
        --output /app

# ---------- Estágio 3: runtime ----------
# Imagem "chiseled": sem shell, sem gerenciador de pacotes, sem utilitários.
# Reduz drasticamente a superfície de ataque e o ruído do scanner de imagem —
# uma CVE em curl não aparece se curl não existe.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS final

# As imagens chiseled já definem o usuário não-root 'app' (UID 64198). A linha
# abaixo é explícita de propósito: torna a garantia legível na revisão e sobrevive
# a uma eventual troca de imagem base. Ver §18 e §19 da skill de segurança.
USER $APP_UID

WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app .

# O fuso precisa existir no container: TimeZoneInfo.FindSystemTimeZoneById
# ("America/Sao_Paulo") falha em imagem sem tzdata, e o worker aborta na subida —
# o que é o comportamento correto, mas melhor evitar.
ENV TZ=America/Sao_Paulo
ENV DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "Congrega.Workers.dll"]
