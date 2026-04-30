namespace MSLX.SDK.Interfaces;

public interface IMSLXLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}