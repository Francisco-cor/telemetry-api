using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telemetry.Api.Infra;

internal class TelemetryApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _conn;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 1) Eliminar DbContext de Oracle registrado por la app
            var dbDesc = services.FirstOrDefault(d => d.ServiceType == typeof(DbContextOptions<TelemDb>));
            if (dbDesc is not null) services.Remove(dbDesc);

            // 2) Conectar SQLite en memoria y mantener la conexión abierta
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();

            services.AddDbContext<TelemDb>(o => o.UseSqlite(_conn));

            // 3) Crear el schema de la base de datos a partir de las migraciones de EF Core
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TelemDb>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _conn?.Close();
        _conn?.Dispose();
    }
}