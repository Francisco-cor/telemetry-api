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
        // 1. Proporcionar una cadena de conexión temporal para pasar la validación de Program.cs
        // El valor real se configura en ConfigureServices.
        builder.UseSetting("ConnectionStrings:Db", "DataSource=file:memdb?mode=memory&cache=shared");

        builder.ConfigureServices(services =>
        {
            // 2. Eliminar el DbContext de Oracle registrado por la app principal
            var dbContextOptions = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TelemDb>));
            if (dbContextOptions != null)
            {
                services.Remove(dbContextOptions);
            }

            // 3. Crear una única conexión a la base de datos en memoria y mantenerla abierta
            _connection = new SqliteConnection("DataSource=file:memdb?mode=memory&cache=shared");
            _connection.Open();

            // 4. Registrar el DbContext para que use SQLite con la conexión compartida
            services.AddDbContext<TelemDb>(options =>
            {
                options.UseSqlite(_connection);
            });

            // 5. Crear el esquema de la base de datos
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TelemDb>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Close();
        _connection?.Dispose();
    }
}