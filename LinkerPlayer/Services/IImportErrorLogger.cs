using LinkerPlayer.Models;
using System.Collections.ObjectModel;

namespace LinkerPlayer.Services;

public interface IImportErrorLogger
{
    ObservableCollection<ImportErrorEntry> Errors { get; }
    void Log(string path, string message);
    void Log(string path, Exception ex);
}
