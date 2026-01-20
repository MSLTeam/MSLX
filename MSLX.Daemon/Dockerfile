# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root

# 自动识别 Debian / Ubuntu / 新版 / 旧版
RUN \
    # Debian 12+ (Bookworm) 的新版配置
    if [ -f /etc/apt/sources.list.d/debian.sources ]; then \
        sed -i 's/deb.debian.org/mirrors.aliyun.com/g' /etc/apt/sources.list.d/debian.sources && \
        sed -i 's/security.debian.org/mirrors.aliyun.com/g' /etc/apt/sources.list.d/debian.sources; \
    fi && \
    # Ubuntu 24.04+ (Noble) 的新版配置
    if [ -f /etc/apt/sources.list.d/ubuntu.sources ]; then \
        sed -i 's/archive.ubuntu.com/mirrors.aliyun.com/g' /etc/apt/sources.list.d/ubuntu.sources && \
        sed -i 's/security.ubuntu.com/mirrors.aliyun.com/g' /etc/apt/sources.list.d/ubuntu.sources; \
    fi && \
    # 旧版 Debian/Ubuntu 的传统配置 (sources.list)
    if [ -f /etc/apt/sources.list ]; then \
        sed -i 's/archive.ubuntu.com/mirrors.aliyun.com/g' /etc/apt/sources.list && \
        sed -i 's/security.ubuntu.com/mirrors.aliyun.com/g' /etc/apt/sources.list && \
        sed -i 's/deb.debian.org/mirrors.aliyun.com/g' /etc/apt/sources.list && \
        sed -i 's/security.debian.org/mirrors.aliyun.com/g' /etc/apt/sources.list; \
    fi

# 安装一些库和设置时区
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    procps \
    libfreetype6 \
    fontconfig \
    acl \
    tzdata \
    openjdk-17-jre-headless \
    openjdk-21-jre-headless && \
    # 设置时区
    ln -fs /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    dpkg-reconfigure -f noninteractive tzdata && \
    # 清理缓存
    rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=1027

WORKDIR /app
EXPOSE 1027

ENV host=*
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

# --platform=$BUILDPLATFORM 使用本机架构运行 SDK
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

# 目标架构
ARG TARGETARCH

WORKDIR /src
COPY ["MSLX.Daemon/MSLX.Daemon.csproj", "MSLX.Daemon/"]

RUN dotnet restore "./MSLX.Daemon/MSLX.Daemon.csproj" -a $TARGETARCH

COPY . .
WORKDIR "/src/MSLX.Daemon"

RUN dotnet build "./MSLX.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/build -a $TARGETARCH

FROM build AS publish
ARG BUILD_CONFIGURATION=Release

RUN dotnet publish "./MSLX.Daemon.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -a $TARGETARCH

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
VOLUME ["/app/DaemonData"]
ENTRYPOINT ["dotnet", "MSLX-Daemon.dll"]