using PermaDel.Core.Metadata;

namespace PermaDel.Models;

/// <summary>A selected file and what PermaDel found in it, as shown in the Remove metadata dialog.</summary>
internal sealed record MetadataFile(FileEntry Entry, MetadataInspection Inspection);

/// <summary>How the dialog was closed: save a copy next to each file, or replace the originals.</summary>
internal enum MetadataRemovalMode
{
    Copy,
    Replace,
}
