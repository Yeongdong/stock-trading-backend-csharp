using StockTrading.Application.Common.Interfaces;
using StockTrading.Infrastructure.Persistence.Contexts;

namespace StockTrading.Infrastructure.Services.Common;

public class DbContextWrapper : IDbContextWrapper
{
    private readonly ApplicationDbContext _dbContext;

    public DbContextWrapper(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IDbTransactionWrapper> BeginTransactionAsync()
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync();
        return new AutoRollbackTransactionWrapper(transaction);
    }
}