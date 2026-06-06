namespace Wsl.Core.Settings;

public interface IThemeService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
