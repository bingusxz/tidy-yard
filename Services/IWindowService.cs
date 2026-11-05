namespace TidyYard.Services;

/// <summary>Opens and closes app windows so view models never touch views.</summary>
public interface IWindowService
{
    void ShowCalendar();

    void ShowSettings();

    void Quit();
}
