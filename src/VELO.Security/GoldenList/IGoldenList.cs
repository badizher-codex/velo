namespace VELO.Security.GoldenList;

public interface IGoldenList
{
    bool IsGolden(string domain);
    int Count { get; }
    DateTime LastUpdated { get; }
    Task LoadAsync(string resourcesPath);
}
