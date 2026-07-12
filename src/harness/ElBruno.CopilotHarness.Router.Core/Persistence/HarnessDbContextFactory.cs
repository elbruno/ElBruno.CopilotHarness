using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ElBruno.CopilotHarness.Router.Core.Persistence;

public sealed class HarnessDbContextFactory : IDesignTimeDbContextFactory<HarnessDbContext>
{
    public HarnessDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HarnessDbContext>();
        optionsBuilder.UseSqlite("Data Source=App_Data/copilotharness-admin.db");
        return new HarnessDbContext(optionsBuilder.Options);
    }
}
