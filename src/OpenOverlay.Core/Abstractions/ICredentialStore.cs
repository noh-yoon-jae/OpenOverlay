namespace OpenOverlay.Core.Abstractions;

public interface ICredentialStore
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}
