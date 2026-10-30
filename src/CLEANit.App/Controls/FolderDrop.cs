using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace CLEANit.App.Controls;

/// <summary>Accepts folders dragged from Explorer onto a control (files count as their folder). Highlights the target while dragging.</summary>
public static class FolderDrop
{
    public static void Attach(Control target, Action<IReadOnlyList<string>> dropped)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragEnterEvent, (_, e) => Over(target, e));
        target.AddHandler(DragDrop.DragOverEvent, (_, e) => Over(target, e));
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => target.Classes.Set("dragover", false));
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            target.Classes.Set("dragover", false);
            var folders = Folders(e);
            if (folders.Count > 0)
            {
                dropped(folders);
            }
        });
    }

    private static void Over(Control target, DragEventArgs e)
    {
        var ok = Folders(e).Count > 0;
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        target.Classes.Set("dragover", ok);
        e.Handled = true;
    }

    private static IReadOnlyList<string> Folders(DragEventArgs e)
    {
        var items = e.Data.GetFiles();
        if (items is null)
        {
            return [];
        }

        var folders = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            var folder = Directory.Exists(path) ? path : File.Exists(path) ? Path.GetDirectoryName(path) : null;
            if (folder is not null && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }
}
