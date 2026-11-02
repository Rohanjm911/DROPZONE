using DropZone.Domain.Interfaces;

namespace DropZone.Android;

public sealed class AndroidStoragePicker : IPlatformStoragePicker
{
    private Func<string, bool, Task<IReadOnlyList<string>>>? _filePickerHandler;
    private Func<string, Task<string?>>? _folderPickerHandler;
    private Func<string, string, string?, Task<string?>>? _savePickerHandler;

    public void SetHandlers(
        Func<string, bool, Task<IReadOnlyList<string>>>? filePicker,
        Func<string, Task<string?>>? folderPicker,
        Func<string, string, string?, Task<string?>>? savePicker)
    {
        _filePickerHandler = filePicker;
        _folderPickerHandler = folderPicker;
        _savePickerHandler = savePicker;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true, CancellationToken ct = default)
    {
        if (_filePickerHandler != null)
        {
            return await _filePickerHandler(title, allowMultiple);
        }

        return Array.Empty<string>();
    }

    public async Task<string?> PickFolderAsync(string title, CancellationToken ct = default)
    {
        if (_folderPickerHandler != null)
        {
            return await _folderPickerHandler(title);
        }

        return null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string defaultFileName, string? extension = null, CancellationToken ct = default)
    {
        if (_savePickerHandler != null)
        {
            return await _savePickerHandler(title, defaultFileName, extension);
        }

        return null;
    }
}

