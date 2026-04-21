using System.Collections.ObjectModel;
using LinkerPlayer.Models;

namespace LinkerPlayer.Services;

public interface IImportErrorLogger
{
    ObservableCollection<ImportErrorEntry> Errors { get; }
    void Log(string path, string message);
    void Log(string path, Exception ex);
}
