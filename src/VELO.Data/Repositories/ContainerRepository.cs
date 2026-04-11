using VELO.Data.Models;

namespace VELO.Data.Repositories;

public class ContainerRepository(VeloDatabase db)
{
    private readonly VeloDatabase _db = db;

    public Task<List<Container>> GetAllAsync()
        => _db.Connection.Table<Container>().ToListAsync();

    public Task SaveAsync(Container container)
        => _db.Connection.InsertOrReplaceAsync(container);

    public Task DeleteAsync(string id)
        => _db.Connection.DeleteAsync<Container>(id);
}
