using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Telemetry.Api.Infra;

public class TelemetryApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 1. Eliminar el DbContext de Oracle
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TelemDb>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // 2. Crear y mantener abierta una única conexión a la base de datos en memoria
            _connection = new SqliteConnection("DataSource=file:memdb?mode=memory&cache=shared");
            _connection.Open();

            // 3. Añadir el DbContext para que use la conexión SQLite
            // La propia aplicación se encargará de ejecutar las migraciones al arrancar.
            services.AddDbContext<TelemDb>(options =>
            {
                options.UseSqlite(_connection);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Close();
        _connection?.Dispose();
    }
}