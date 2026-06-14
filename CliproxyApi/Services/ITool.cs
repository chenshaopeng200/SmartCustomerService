namespace CliproxyApi.Services;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object Parameters { get; }
    Task<string> ExecuteAsync(string argumentsJson);
}
