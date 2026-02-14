using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Codec.Api.Data;

/// <summary>
/// Factory used by EF Core CLI tools to create the DbContext at design time
/// without requiring the full application host (which validates config values
/// such as Google:ClientId).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CodecDbContext>
{
    public CodecDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CodecDbContext>();
        // A dummy connection string is sufficient for generating migrations and bundles.
        optionsBuilder.UseNpgsql("Host=localhost;Database=codec_design;Username=postgres;Password=postgres");
        return new CodecDbContext(optionsBuilder.Options);
    }
}
