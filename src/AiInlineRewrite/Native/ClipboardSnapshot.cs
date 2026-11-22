using System.Runtime.InteropServices;
using System.Windows;

namespace AiInlineRewrite.Native;

/// <summary>
/// What was on the clipboard before AIR pasted a version, in every format that can be read back (text, rich text,
/// HTML, an image, files...), so applying a version does not lose it. Formats that cannot be copied out (delayed
/// rendering by an app that already closed, private handles) are skipped. Must run on the UI (STA) thread.
/// </summary>
public sealed class ClipboardSnapshot
{
    private readonly DataObject? _data;

    private ClipboardSnapshot(DataObject? data) => _data = data;

    public static ClipboardSnapshot Take()
    {
        try
        {
            var source = Clipboard.GetDataObject();
            if (source is null) return new ClipboardSnapshot(null);
            var copy = new DataObject();
            int kept = 0;
            foreach (string format in source.GetFormats(autoConvert: false))
            {
                try
                {
                    object? value = source.GetData(format, autoConvert: false);
                    if (value is null) continue;
                    copy.SetData(format, value, autoConvert: false);
                    kept++;
                }
                catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException or InvalidOperationException or ArgumentException)
                {
                    // a format that cannot be copied out is skipped
                }
            }
            return new ClipboardSnapshot(kept > 0 ? copy : null);
        }
        catch (Exception e) when (e is COMException or ExternalException)
        {
            return new ClipboardSnapshot(null);
        }
    }

    /// <summary>Puts the saved content back; an empty clipboard stays empty.</summary>
    public void Restore()
    {
        try
        {
            if (_data is null) Clipboard.Clear();
            else Clipboard.SetDataObject(_data, copy: true);
        }
        catch (Exception e) when (e is COMException or ExternalException)
        {
            // another app holds the clipboard; nothing more to do
        }
    }
}
