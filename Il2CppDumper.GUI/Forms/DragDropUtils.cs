using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Il2CppDumper
{
    internal static class DragDropUtils
    {
        private static readonly string[] EmptyStrings = new string[0];

        internal static string[] GetFilesDrop(this DragEventArgs args)
        {
            return (string[])args.Data.GetData(DataFormats.FileDrop) ?? EmptyStrings;
        }

        internal static string[] GetFilesDrop(this DragEventArgs args, string ending)
        {
            // ReSharper disable once ConvertIfStatementToReturnStatement
            if (string.IsNullOrEmpty(ending))
                return args.GetFilesDrop();

            return args.GetFilesDrop(it => it.EndsWith(ending, StringComparison.Ordinal));
        }


        internal static string[] GetFilesDrop(this DragEventArgs args, Func<string, bool> filter)
        {
            var items = args.GetFilesDrop();

            if (items == null)
                return EmptyStrings;

            return filter == null ? items : items.Where(filter).ToArray();
        }

        internal static void CheckDragEnter(this DragEventArgs e, params string[] extensions)
        {
            e.Handled = true;
            string[] files = e.GetFilesDrop();
            if (extensions.Any(ext => files[0].EndsWith(ext, StringComparison.Ordinal)))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
        }

        internal static bool CheckDragEnter(this DragEventArgs e)
        {
            e.Handled = true;
            string[] files = e.GetFilesDrop();
            if (files.Length == 1 && File.Exists(files[0]))
            {
                e.Effects = DragDropEffects.Copy;
                return true;
            }

            e.Effects = DragDropEffects.None;
            return false;
        }

        internal static bool CheckDragOver(this DragEventArgs e, params string[] extensions)
        {
            e.Handled = true;
            string[] files = e.GetFilesDrop();
            if (files.Length == 1 && extensions.Any(ext => files[0].EndsWith(ext, StringComparison.Ordinal)))
            {
                e.Effects = DragDropEffects.Move;
                return true;
            }
            e.Effects = DragDropEffects.None;
            return false;
        }

        internal static bool CheckDragOver(this DragEventArgs e)
        {
            e.Handled = true;
            string[] files = e.GetFilesDrop();
            if (files.Length == 1 && File.Exists(files[0]))
            {
                e.Effects = DragDropEffects.Move;
                return true;
            }
            e.Effects = DragDropEffects.None;
            return false;
        }

        internal static bool CheckDragOverFolder(this DragEventArgs e)
        {
            e.Handled = true;
            string[] files = e.GetFilesDrop();
            if (files.Length == 1 && Directory.Exists(files[0]))
            {
                e.Effects = DragDropEffects.Move;
                return true;
            }
            e.Effects = DragDropEffects.None;
            return false;
        }

        internal static bool CheckManyDragOver(this DragEventArgs e, params string[] extensions)
        {
            e.Handled = true;
            string[] files = e.GetFilesDrop();
            if (extensions.Any(ext => files[0].EndsWith(ext, StringComparison.Ordinal)))
            {
                e.Effects = DragDropEffects.Move;
                return true;
            }
            e.Effects = DragDropEffects.None;
            return false;
        }

        internal static bool CheckManyDragOverWithFolders(this DragEventArgs e, params string[] extensions)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (droppedFiles.Any(filePath =>
                    Directory.Exists(filePath) ||
                    extensions.Any(ext => File.Exists(filePath) && filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
                {
                    e.Effects = DragDropEffects.Move;
                    return true;
                }
            }

            e.Effects = DragDropEffects.None;
            return false;
        }

        internal static void DropOneByEnd(this DragEventArgs e, Action<string> onSuccess, params string[] extensions)
        {
            string[] files = e.GetFilesDrop();

            if (extensions.Any(ext => files[0].EndsWith(ext, StringComparison.Ordinal)))
            {
                onSuccess(files[0]);

                e.Handled = true;
            }
        }

        internal static void DropOneByEnd(this DragEventArgs e, string ext, Action<string> onSuccess)
        {
            string[] files = e.GetFilesDrop(ext);

            if (files.Length == 1)
                onSuccess(files[0]);

            e.Handled = true;
        }

        internal static void DropManyByEnd(this DragEventArgs e, string ext, Action<string[]> onSuccess)
        {
            string[] files = e.GetFilesDrop(ext);

            if (files.Length > 0)
                onSuccess(files);

            e.Handled = true;
        }

        internal static void DropManyByEnd(this DragEventArgs e, Action<string[]> onSuccess, params string[] extensions)
        {
            var files = new List<String>();

            foreach (string apk in extensions)
            {
                files.AddRange(e.GetFilesDrop(apk));

                if (files.Count > 0)
                {
                    e.Handled = true;
                    onSuccess(files.ToArray());
                }
            }
        }

        public static void DropManyByEndWithFolder(this DragEventArgs e, Action<string[]> onSuccess, params string[] extensions)
        {
            var files = new List<string>();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);

                foreach (string filePath in droppedFiles)
                {
                    if (Directory.Exists(filePath)) // Check if the dropped item is a folder
                    {
                        string[] folderFiles = Directory.GetFiles(filePath, "*.*", SearchOption.AllDirectories)
                            .Where(file => extensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                            .ToArray();

                        files.AddRange(folderFiles);
                    }
                    else if (File.Exists(filePath)) // Check if the dropped item is a file
                    {
                        if (extensions.Any(ext => filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            files.Add(filePath);
                        }
                    }
                }
            }

            if (files.Count > 0)
            {
                e.Handled = true;
                onSuccess(files.ToArray());
            }
        }
    }
}
