using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CropQc.Data;

public sealed class CropQcDbContextFactory : IDesignTimeDbContextFactory<CropQcDbContext>
{
    public CropQcDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CropQcDbContext>();
        var provider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
            ?? Environment.GetEnvironmentVariable("Database__Provider")
            ?? CropQcDatabase.DefaultProvider;
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CropQc")
            ?? CropQcDatabase.DefaultSqlServerConnectionString;
        CropQcDatabase.Configure(optionsBuilder, provider, connectionString);

        return new CropQcDbContext(optionsBuilder.Options);
    }
}
