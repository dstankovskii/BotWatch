using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BotWatch.Tests;

// openapi.json сервисов лежит в web/admin/openapi, из него фронт генерирует типы.
// тест падает, если контракт поменялся, а файл нет. обновить:
// $env:BOTWATCH_UPDATE_OPENAPI=1; dotnet test --filter OpenApi
public static class OpenApiSnapshot
{
    public static async Task VerifyAsync<TEntryPoint>(string name, Action<IWebHostBuilder>? configure = null)
        where TEntryPoint : class
    {
        await using var factory = new WebApplicationFactory<TEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:" + name, "Host=localhost;Database=" + name);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.UseSetting("Encryption:Key", Convert.ToBase64String(new byte[32]));
            configure?.Invoke(builder);

            // планировщик, outbox и консьюмеры полезли бы в базу и брокер, для документа они не нужны
            builder.ConfigureServices(services =>
            {
                var ours = services
                    .Where(x => x.ServiceType == typeof(IHostedService) && IsOurs(x))
                    .ToList();

                foreach (var descriptor in ours)
                {
                    services.Remove(descriptor);
                }
            });
        });

        using var client = factory.CreateClient();
        var actual = Normalize(await client.GetStringAsync("/openapi/v1.json"));

        var path = Path.Combine(FindRepoRoot(), "web", "admin", "openapi", name + ".json");
        var expected = File.Exists(path) ? Normalize(await File.ReadAllTextAsync(path)) : null;

        if (actual == expected)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("BOTWATCH_UPDATE_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual);
            return;
        }

        Assert.Fail($"{path} is out of date. Run tests with BOTWATCH_UPDATE_OPENAPI=1, then npm run api in web/admin");
    }

    private static bool IsOurs(ServiceDescriptor descriptor)
    {
        // у фабрики возвращаемый тип IHostedService, смотрим, где объявлена сама лямбда
        var type = descriptor.ImplementationType ?? descriptor.ImplementationFactory?.Method.DeclaringType;

        return type?.Assembly.GetName().Name?.StartsWith("BotWatch") == true;
    }

    private static string Normalize(string json)
    {
        return json.Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BotWatch.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("BotWatch.slnx not found");
    }
}
