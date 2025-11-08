using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Telemetry.Api.Infra;

public class DesignTimeTelemDbFactory : IDesignTimeDbContextFactory<TelemDb>
{
    public TelemDb CreateDbContext(string[] args)
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = cfg.GetConnectionString("Oracle")
                 ?? "User Id=telem;Password=telem_pw;Data Source=localhost:1521/XEPDB1;";

        var opts = new DbContextOptionsBuilder<TelemDb>()
            .UseOracle(cs)
            .Options;

        return new TelemDb(opts);
    }
}
