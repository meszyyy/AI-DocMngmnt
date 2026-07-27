using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace AiDocMngmnt.Data;

// Used only by the `dotnet ef` design-time tools (migrations). It avoids
// booting the real host, so no Aspire connection strings are needed:
//   dotnet ef migrations add <Name> --project AiDocMngmnt.Data
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design-time-only", o => o.UseVector())
            .Options);
}
