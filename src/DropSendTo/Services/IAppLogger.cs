namespace DropSendTo.Services;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

internal sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger()
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message)
    {
    }
}
