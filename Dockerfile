# один Dockerfile на все .NET-сервисы, какой собирать - через аргументы:
# docker build --build-arg PROJECT=src/Monitoring/BotWatch.Monitoring.Api --build-arg APP=BotWatch.Monitoring.Api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "$PROJECT" -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG APP
ENV APP_DLL=${APP}.dll
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
