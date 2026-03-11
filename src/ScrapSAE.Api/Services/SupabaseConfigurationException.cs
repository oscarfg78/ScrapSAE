namespace ScrapSAE.Api.Services;

public sealed class SupabaseConfigurationException : InvalidOperationException
{
    public SupabaseConfigurationException(string message)
        : base(message)
    {
    }
}
