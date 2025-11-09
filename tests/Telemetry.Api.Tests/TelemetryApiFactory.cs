using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Api.Infra;

public class TelemetryApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _conn;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 1) Entorno de pruebas
        builder.UseEnvironment("Testing");

        // 2) Proveer la clave que tu Program espera para no fallar en el arranque
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Db"] = "DataSource=file:inmem?mode=memory&cache=shared"
            };
            config.AddInMemoryCollection(dict);
        });

        builder.ConfigureServices(services =>
        {
            // 3) Eliminar el DbContext registrado por la app (Oracle)
            var dbCtx = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TelemDb>));
            if (dbCtx is not null) services.Remove(dbCtx);

            // 4) Registrar SQLite en memoria (conexión compartida para que no se pierda al abrir/cerrar scopes)
            _conn = new SqliteConnection("DataSource=file:inmem?mode=memory&cache=shared");
            _conn.Open();

            services.AddDbContext<TelemDb>(o => o.UseSqlite(_conn));

            // 5) Crear el esquema sin migraciones (migraciones son Oracle-specific)
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TelemDb>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _conn?.Dispose();
    }
}
