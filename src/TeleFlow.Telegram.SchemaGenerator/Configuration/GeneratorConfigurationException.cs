namespace TeleFlow.Telegram.SchemaGenerator.Configuration;

/// <summary>
/// Signals that generation reached a semantic boundary requiring an explicit repository-owned decision.
/// The CLI maps this exception to a dedicated exit code consumed by monitor automation.
/// </summary>
internal sealed class GeneratorConfigurationException : InvalidOperationException
{
    public GeneratorConfigurationException(string message)
        : base(message)
    {
    }

    public GeneratorConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
