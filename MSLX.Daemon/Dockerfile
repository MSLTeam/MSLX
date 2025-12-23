# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root

# 安装一些库和设置时区
RUN apt-get update && \
    apt-get install -y procps libfreetype6 fontconfig acl tzdata && \
    ln -fs /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    dpkg-reconfigure -f noninteractive tzdata && \
    rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=1027

WORKDIR /app
EXPOSE 1027

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MSLX.Daemon/MSLX.Daemon.csproj", "MSLX.Daemon/"]
RUN dotnet restore "./MSLX.Daemon/MSLX.Daemon.csproj"
COPY . .
WORKDIR "/src/MSLX.Daemon"
RUN dotnet build "./MSLX.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MSLX.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MSLX.Daemon.dll"]