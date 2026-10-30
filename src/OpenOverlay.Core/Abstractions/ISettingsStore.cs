using OpenOverlay.Core.Models;

namespace OpenOverlay.Core.Abstractions;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
