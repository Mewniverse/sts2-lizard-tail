using System.Text;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Migrations;
using SaveSlots.SaveSlotsCode.Core;

namespace SaveSlots.SaveSlotsCode.Game;

/// <summary>
/// Loads a parked run the way the multiplayer menu's Load does (migrate, deserialize, then check it
/// against the installed game and mods), without touching any file. Run before a swap changes
/// anything, so a run the game would refuse is never made active.
/// </summary>
public static class SaveCheck
{
    private const string ProbePath = "probe/current_run_mp.save";

    private static readonly ProbeStore Probe = new();
    private static MigrationManager? _migrations;

    /// <summary>Throws with the reason when the game could not load <paramref name="text"/> for <paramref name="hostId"/>.</summary>
    public static void Verify(string text, ulong hostId)
    {
        _migrations ??= new MigrationManager(Probe);
        Probe.Reset(ProbePath, text);

        var read = _migrations.LoadSave<SerializableRun>(ProbePath);
        if (!read.Success || read.SaveData == null)
        {
            throw new InvalidOperationException($"The game cannot read this run ({read.Status}), so it was left parked.");
        }
        if (read.Status == ReadSaveStatus.RecoveredWithDataLoss)
        {
            throw new InvalidOperationException("The game could only read part of this run, so it was left parked.");
        }

        try
        {
            RunManager.CanonicalizeSave(read.SaveData, hostId);
        }
        catch (Exception ex)
        {
            ModLog.Warn($"A parked run failed the game's load check: {ex}");
            throw new InvalidOperationException("The game would refuse to load this run with the current version and mods, so it was left parked.");
        }
    }

    /// <summary>
    /// A save store holding one file in memory. The game's loader writes copies of broken files next
    /// to them; here those writes land in memory and vanish.
    /// </summary>
    private sealed class ProbeStore : ISaveStore
    {
        private readonly Dictionary<string, string> _files = new();

        public void Reset(string path, string text)
        {
            _files.Clear();
            _files[path] = text;
        }

        public string? ReadFile(string path) => _files.GetValueOrDefault(path);

        public Task<string?> ReadFileAsync(string path) => Task.FromResult(ReadFile(path));

        public void WriteFile(string path, string content) => _files[path] = content;

        public void WriteFile(string path, byte[] content) => _files[path] = Encoding.UTF8.GetString(content);

        public Task WriteFileAsync(string path, string content)
        {
            WriteFile(path, content);
            return Task.CompletedTask;
        }

        public Task WriteFileAsync(string path, byte[] content)
        {
            WriteFile(path, content);
            return Task.CompletedTask;
        }

        public bool FileExists(string path) => _files.ContainsKey(path);

        public bool DirectoryExists(string path) => true;

        public void DeleteFile(string path) => _files.Remove(path);

        public void RenameFile(string sourcePath, string destinationPath)
        {
            if (_files.Remove(sourcePath, out var text))
            {
                _files[destinationPath] = text;
            }
        }

        public string[] GetFilesInDirectory(string directoryPath) => Array.Empty<string>();

        public string[] GetDirectoriesInDirectory(string directoryPath) => Array.Empty<string>();

        public void CreateDirectory(string directoryPath)
        {
        }

        public void DeleteDirectory(string directoryPath)
        {
        }

        public void DeleteTemporaryFiles(string directoryPath)
        {
        }

        public DateTimeOffset GetLastModifiedTime(string path) => DateTimeOffset.UtcNow;

        public int GetFileSize(string path) => _files.TryGetValue(path, out var text) ? Encoding.UTF8.GetByteCount(text) : 0;

        public void SetLastModifiedTime(string path, DateTimeOffset time)
        {
        }

        public string GetFullPath(string filename) => filename;
    }
}
