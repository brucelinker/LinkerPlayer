using LinkerPlayer.Models;
using System.Collections.Generic;
using File = TagLib.File;

namespace LinkerPlayer.ViewModels.Properties;

/// <summary>
/// Interface for writing metadata changes back to audio files
/// </summary>
public interface IMetadataWriter
{
    /// <summary>
  /// Apply all pending changes and save to a single file
    /// </summary>
/// <param name="audioFile">File to save</param>
    /// <param name="metadataItems">Metadata items with potential changes</param>
    /// <returns>True if save was successful</returns>
    bool Save(File audioFile, IEnumerable<TagItem> metadataItems);

    /// <summary>
    /// Apply all pending changes and save to multiple files
    /// </summary>
    /// <param name="audioFiles">Files to save</param>
    /// <param name="metadataItems">Metadata items with potential changes</param>
    /// <returns>True if all saves were successful</returns>
  bool SaveMultiple(IReadOnlyList<File> audioFiles, IEnumerable<TagItem> metadataItems);
}
