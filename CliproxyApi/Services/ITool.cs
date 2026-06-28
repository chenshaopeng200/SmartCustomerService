using CliproxyApi.Models;

namespace CliproxyApi.Services;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonSchema Parameters { get; }
    Task<string> ExecuteAsync(string argumentsJson);
}
