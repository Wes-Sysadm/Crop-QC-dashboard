using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CropQc.Data;

public sealed class CropQcDbContextFactory : IDesignTimeDbContextFactory<CropQcDbContext>
{
    public CropQcDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CropQcDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=CropQcDashboard;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");

        return new CropQcDbContext(optionsBuilder.Options);
    }
}
