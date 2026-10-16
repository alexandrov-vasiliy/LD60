using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace BlenderSceneControls
{
    [Serializable]
    internal sealed class ProfileBinding
    {
        public string Id;
        public int[] Keys;
        public int[] Modifiers;
        public bool Overridden;

        // Unity exposes construction of single-key bindings only. Multi-key sequences are
        // cloned as their original public ShortcutBinding value, never reconstructed via internals.
        internal ShortcutBinding Binding => Keys.Length == 0 ? ShortcutBinding.empty : Keys.Length == 1 ?
            new ShortcutBinding(new KeyCombination((KeyCode)Keys[0], (ShortcutModifiers)Modifiers[0])) :
            throw new InvalidOperationException("Unity cannot publicly reconstruct a multi-key sequence from a journal.");

        internal bool Matches(ShortcutBinding binding)
        {
            var sequence = binding.keyCombinationSequence.ToArray();
            return Keys.SequenceEqual(sequence.Select(k => (int)k.keyCode)) &&
                Modifiers.SequenceEqual(sequence.Select(k => (int)k.modifiers));
        }

        internal static ProfileBinding Capture(IShortcutManager manager, string id)
        {
            var sequence = manager.GetShortcutBinding(id).keyCombinationSequence.ToArray();
            return new ProfileBinding { Id = id, Keys = sequence.Select(k => (int)k.keyCode).ToArray(),
                Modifiers = sequence.Select(k => (int)k.modifiers).ToArray(), Overridden = manager.IsShortcutOverridden(id) };
        }
    }

    [Serializable]
    internal sealed class ProfileChange
    {
        public ProfileBinding Before;
        public ProfileBinding After;
        public string Reason;
    }

    [Serializable]
    internal sealed class ProfileJournal
    {
        public int Version = 1;
        public string Project;
        public string Token;
        public string Original;
        public string Owned;
        public string Phase;
        public ProfileBinding[] Snapshot;
        public ProfileChange[] Changes;
        public string[] Unresolved;
        public List<string> History = new List<string>();
        public int MeshVersion;
    }

    // Stored outside Assets/Library and keyed by absolute project path, never copied with a package.
    // A shared file lock serializes cooperating projects; Unity's profile caches remain per Editor.
    internal sealed class ProfileJournalStore
    {
        internal readonly string Project;
        internal readonly string Path;
        private readonly string _directory;

        internal ProfileJournalStore(string directory, string project)
        {
            _directory = directory;
            Project = System.IO.Path.GetFullPath(project).TrimEnd('\\', '/').Replace('\\', '/').ToUpperInvariant();
            using (var hash = SHA256.Create())
                Path = System.IO.Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Project))).Replace("-", "") + ".json");
        }

        internal FileStream Lock()
        {
            Directory.CreateDirectory(_directory);
            return new FileStream(System.IO.Path.Combine(_directory, "profiles.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }

        internal ProfileJournal Load()
        {
            if (!File.Exists(Path))
            {
                if (File.Exists(Path + ".bak")) throw new InvalidOperationException("Profile journal is missing; its .bak was retained. Recover it before Apply.");
                return null;
            }
            var journal = JsonUtility.FromJson<ProfileJournal>(File.ReadAllText(Path));
            if (journal == null || journal.Version != 1 || journal.MeshVersion < 0 || journal.MeshVersion > 1 || journal.Project != Project ||
                !Guid.TryParseExact(journal.Token, "N", out _) || journal.Owned != "BSC-" + journal.Token ||
                string.IsNullOrEmpty(journal.Original) || journal.Original.StartsWith("BSC-", StringComparison.Ordinal) ||
                journal.Snapshot == null || journal.Changes == null || journal.Unresolved == null || journal.History == null ||
                !new[] { "Preparing", "Ready", "Restored", "Incomplete" }.Contains(journal.Phase) ||
                journal.Snapshot.Any(b => !Valid(b)) || journal.Snapshot.Select(b => b.Id).Distinct().Count() != journal.Snapshot.Length ||
                journal.Changes.Any(c => c == null || !Valid(c.Before) || !Valid(c.After) || c.Before.Id != c.After.Id))
                throw new InvalidOperationException("Profile journal is invalid or belongs to another project. No profiles changed. Journal: " + Path);
            return journal;
        }

        private static bool Valid(ProfileBinding b) => b != null && !string.IsNullOrEmpty(b.Id) &&
            b.Keys != null && b.Modifiers != null && b.Keys.Length == b.Modifiers.Length;

        internal void Save(ProfileJournal journal)
        {
            var temporary = Path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(journal, true));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(Path)) File.Replace(temporary, Path, Path + ".bak");
                else File.Move(temporary, Path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed class ShortcutProfileService
    {
        private readonly IShortcutManager _manager;
        private readonly ProfileJournalStore _store;
        internal ShortcutProfileService(IShortcutManager manager, ProfileJournalStore store) { _manager = manager; _store = store; }

        // UnityCsReference 6000.3: global Tools/menu commands, plus Cycle Tool Modes whose
        // ToolShortcutContext is active in SceneView/Hierarchy and overlaps our SceneView context.
        // A key match in Timeline/FPS/UI Builder is NOT sufficient evidence to remove a binding.
        private static readonly HashSet<string> ConfirmedOverlapIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Tools/View", "Tools/Move", "Tools/Rotate", "Tools/Scale", "Tools/Rect", "Tools/Transform",
            "Tools/Cycle Tool Modes", "Tools/Toggle Pivot Position", "Tools/Toggle Pivot Orientation",
            "Main Menu/Edit/Deselect All", "Main Menu/Edit/Frame Selected in Window under Cursor"
        };

        internal static Dictionary<string, ShortcutBinding> Commands => new Dictionary<string, ShortcutBinding>
        {
            { "Blender Scene Controls/Grab", Key(KeyCode.G) },
            { "Blender Scene Controls/Rotate", Key(KeyCode.R) },
            { "Blender Scene Controls/Scale", Key(KeyCode.S) },
            { "Blender Scene Controls/Duplicate", Key(KeyCode.D, ShortcutModifiers.Shift) },
            { "Blender Scene Controls/Add Primitive", Key(KeyCode.A, ShortcutModifiers.Shift) },
            { "Blender Scene Controls/Frame Selected", Key(KeyCode.F) }
        };

        internal static readonly Dictionary<SceneCommand, string> MeshCommandIds = new Dictionary<SceneCommand, string>
        {
            { SceneCommand.ToggleMeshMode, "Blender Scene Controls/Mesh Mode" },
            { SceneCommand.Vertex, "Blender Scene Controls/Vertex" },
            { SceneCommand.Edge, "Blender Scene Controls/Edge" },
            { SceneCommand.Face, "Blender Scene Controls/Face" },
            { SceneCommand.XRay, "Blender Scene Controls/X-Ray" },
            { SceneCommand.SelectAll, "Blender Scene Controls/Select All Elements" },
            { SceneCommand.DeselectAll, "Blender Scene Controls/Deselect All Elements" },
            { SceneCommand.BoxSelect, "Blender Scene Controls/Box Select Elements" }
        };
        internal static Dictionary<string, ShortcutBinding> MeshCommands => new Dictionary<string, ShortcutBinding>
        {
            { MeshCommandIds[SceneCommand.ToggleMeshMode], Key(KeyCode.Tab) },
            { MeshCommandIds[SceneCommand.Vertex], Key(KeyCode.Alpha1) },
            { MeshCommandIds[SceneCommand.Edge], Key(KeyCode.Alpha2) },
            { MeshCommandIds[SceneCommand.Face], Key(KeyCode.Alpha3) },
            { MeshCommandIds[SceneCommand.XRay], Key(KeyCode.Z, ShortcutModifiers.Alt) },
            { MeshCommandIds[SceneCommand.SelectAll], Key(KeyCode.A) },
            { MeshCommandIds[SceneCommand.DeselectAll], Key(KeyCode.A, ShortcutModifiers.Alt) },
            { MeshCommandIds[SceneCommand.BoxSelect], Key(KeyCode.B) }
        };

        internal string UpgradeMesh()
        {
            using (_store.Lock())
            {
                var journal = _store.Load();
                if (journal == null) return "Upgrade stopped: apply a profile first.";
                if (journal.Phase != "Ready" && journal.Phase != "Restored" ||
                    _manager.activeProfileId != journal.Owned || !_manager.GetAvailableProfileIds().Contains(journal.Owned) ||
                    !_manager.GetAvailableProfileIds().Contains(journal.Original) || _manager.IsProfileReadOnly(journal.Owned))
                    return "Upgrade stopped: activate the intact owned profile first. " + Describe(journal);
                if (journal.MeshVersion >= 1) return "Mesh profile already upgraded; no bindings rewritten. " + Describe(journal);
                if (MeshCommands.Keys.Any(id => !Available(id))) return "Upgrade stopped: mesh shortcut definitions are missing.";
                // Any explicit override (including empty) belongs to the user. Never clear unknown conflicts.
                var eligible = MeshCommands.Where(c => !_manager.IsShortcutOverridden(c.Key)).ToArray();
                var retained = MeshCommands.Keys.Except(eligible.Select(c => c.Key)).ToArray();
                var collisions = _manager.GetAvailableShortcutIds().Where(id => !MeshCommands.ContainsKey(id) &&
                    eligible.Any(c => _manager.GetShortcutBinding(id).keyCombinationSequence.Any() &&
                        _manager.GetShortcutBinding(id).keyCombinationSequence.First().Equals(c.Value.keyCombinationSequence.First())));
                journal.Unresolved = journal.Unresolved.Concat(collisions).Distinct().ToArray();
                Record(journal, "Incomplete", "Explicit mesh upgrade started; existing snapshot and changes retained.");
                try
                {
                    foreach (var command in eligible) Change(journal, command.Key, command.Value, "Explicit M04 mesh shortcut assignment.");
                    journal.MeshVersion = 1;
                    Record(journal, "Ready", "Mesh upgrade finished. User overrides retained: " + string.Join(", ", retained));
                    return "Mesh profile upgraded. Existing overrides and unrelated conflicts retained; inspect Unity Shortcuts for collisions. " + Describe(journal);
                }
                catch
                {
                    Record(journal, "Incomplete", "Mesh upgrade interrupted; partial changes retained for inspection. Use Restore Previous Profile.");
                    throw;
                }
            }
        }

        private static ShortcutBinding Key(KeyCode key, ShortcutModifiers modifiers = ShortcutModifiers.None) =>
            new ShortcutBinding(new KeyCombination(key, modifiers));

        private static bool Collides(ShortcutBinding binding) => binding.keyCombinationSequence.Any() &&
            Commands.Values.Any(target => target.keyCombinationSequence.First().Equals(binding.keyCombinationSequence.First()));

        internal static bool ConfirmedConflict(string id, ShortcutBinding binding) => ConfirmedOverlapIds.Contains(id) && Collides(binding);

        internal string Apply()
        {
            using (_store.Lock())
            {
                var journal = _store.Load();
                var profiles = _manager.GetAvailableProfileIds().ToArray();
                var current = _manager.activeProfileId;
                if (journal != null)
                {
                    if (current != journal.Original && current != journal.Owned)
                        return "Apply stopped: a different profile is active. Select the saved original or owned profile first. " + Describe(journal);
                    if (!profiles.Contains(journal.Owned))
                        return "Apply stopped: the owned copy is missing or renamed. No replacement was created; restore/select the original. " + Describe(journal);
                    if (journal.Phase == "Preparing" || journal.Phase == "Incomplete")
                        return "Apply stopped: an interrupted copy was retained for inspection. Use Restore Previous Profile. " + Describe(journal);
                    // Reuse never rewrites bindings, including the user's edits to our six commands.
                    _manager.activeProfileId = journal.Owned;
                    var edits = journal.Changes.Where(c => !Available(c.After.Id) ||
                        !c.After.Matches(_manager.GetShortcutBinding(c.After.Id))).Select(c => c.After.Id).ToArray();
                    Record(journal, "Ready", "Reused existing copy; no bindings rewritten.");
                    return "Owned profile activated; user edits retained." + (edits.Length == 0 ? "" :
                        " Changed/missing managed bindings: " + string.Join(", ", edits)) + " " + Describe(journal);
                }
                if (current.StartsWith("BSC-", StringComparison.Ordinal))
                    return "Apply stopped: this is an owned or orphaned BSC profile. Select a non-BSC source profile; no ownership was inferred from its name.";
                var ids = _manager.GetAvailableShortcutIds().OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (Commands.Keys.Any(id => !ids.Contains(id)))
                    return "Apply stopped: plugin shortcut definitions are missing. No profiles changed.";
                var snapshot = ids.Select(id => ProfileBinding.Capture(_manager, id)).ToArray();
                var liveBindings = ids.ToDictionary(id => id, id => _manager.GetShortcutBinding(id));
                var token = Guid.NewGuid().ToString("N");
                journal = new ProfileJournal { Project = _store.Project, Token = token, Original = current,
                    Owned = "BSC-" + token, Phase = "Preparing", Snapshot = snapshot,
                    Changes = Array.Empty<ProfileChange>(), Unresolved = snapshot.Where(b => Collides(liveBindings[b.Id]) &&
                        !Commands.ContainsKey(b.Id) && !ConfirmedOverlapIds.Contains(b.Id)).Select(b => b.Id).ToArray() };
                if (profiles.Contains(journal.Owned)) throw new InvalidOperationException("Generated profile ID already exists; nothing changed.");
                // Write the source snapshot and recovery identity BEFORE any Unity mutation.
                Record(journal, "Preparing", "Source captured; beginning explicit clone.");
                try
                {
                    _manager.CreateProfile(journal.Owned);
                    _manager.activeProfileId = journal.Owned;
                    foreach (var binding in snapshot)
                    {
                        AssertOwned(journal);
                        // Override even default-equal keys so later defaults cannot silently change the clone.
                        _manager.RebindShortcut(binding.Id, liveBindings[binding.Id]);
                    }
                    foreach (var binding in snapshot.Where(b => ConfirmedConflict(b.Id, liveBindings[b.Id])))
                        Change(journal, binding.Id, ShortcutBinding.empty, binding.Id == "Tools/Cycle Tool Modes" ?
                            "Confirmed SceneView/Hierarchy tool context overlaps a plugin command." :
                            "Confirmed global Unity shortcut overlaps a plugin command.");
                    foreach (var command in Commands)
                        Change(journal, command.Key, command.Value, "Explicit plugin command assignment.");
                    // Only a newly requested clone receives the current mesh assignments. Repeat Apply never upgrades.
                    foreach (var command in MeshCommands.Where(c => Available(c.Key)))
                    {
                        var original = snapshot.First(b => b.Id == command.Key);
                        if (!original.Overridden) Change(journal, command.Key, command.Value, "Explicit new profile mesh assignment.");
                    }
                    if (MeshCommands.Keys.All(Available)) journal.MeshVersion = 1;
                    if (snapshot.Any(b => !(journal.Changes.LastOrDefault(c => c.After.Id == b.Id)?.After ?? b)
                        .Matches(_manager.GetShortcutBinding(b.Id))))
                        throw new InvalidOperationException("Clone verification failed.");
                    Record(journal, "Ready", "Clone verified and activated.");
                    return "Shortcut profile applied. " + Describe(journal) +
                        " Public API cannot enumerate context conflicts. Unresolved key matches retained: " + string.Join(", ", journal.Unresolved);
                }
                catch
                {
                    // Never delete a partial copy or overwrite any original. Retain recovery evidence.
                    if (_manager.activeProfileId == journal.Owned && _manager.GetAvailableProfileIds().Contains(journal.Original))
                        _manager.activeProfileId = journal.Original;
                    Record(journal, "Incomplete", "Apply interrupted; copy retained, original activation attempted.");
                    throw;
                }
            }
        }

        internal string Restore()
        {
            using (_store.Lock())
            {
                var journal = _store.Load();
                if (journal == null) return "No saved previous profile for this project. No profiles changed.";
                if (!_manager.GetAvailableProfileIds().Contains(journal.Original))
                    return "Restore stopped: the original profile is missing or renamed. No fallback was substituted. " + Describe(journal);
                if (_manager.activeProfileId != journal.Owned && _manager.activeProfileId != journal.Original)
                    return "Restore stopped: a different profile is active; it was left unchanged. Select the original in Unity's Shortcuts window. " + Describe(journal);
                _manager.activeProfileId = journal.Original;
                var phase = journal.Phase == "Preparing" || journal.Phase == "Incomplete" ? "Incomplete" : "Restored";
                Record(journal, phase, "Original activated; owned copy and all user edits retained.");
                return "Previous profile restored; owned copy retained. " + Describe(journal);
            }
        }

        private bool Available(string id) => _manager.GetAvailableShortcutIds().Contains(id);
        private void AssertOwned(ProfileJournal journal)
        {
            if (_manager.activeProfileId != journal.Owned) throw new InvalidOperationException("Active profile changed during Apply; stopping without editing it.");
        }
        private void Change(ProfileJournal journal, string id, ShortcutBinding binding, string reason)
        {
            AssertOwned(journal);
            var before = ProfileBinding.Capture(_manager, id);
            var sequence = binding.keyCombinationSequence.ToArray();
            var after = new ProfileBinding { Id = id, Keys = sequence.Select(k => (int)k.keyCode).ToArray(),
                Modifiers = sequence.Select(k => (int)k.modifiers).ToArray(), Overridden = true };
            journal.Changes = journal.Changes.Concat(new[] { new ProfileChange { Before = before, After = after, Reason = reason } }).ToArray();
            _store.Save(journal);
            _manager.RebindShortcut(id, binding);
        }
        private void Record(ProfileJournal journal, string phase, string text)
        {
            journal.Phase = phase;
            journal.History.Add(DateTime.UtcNow.ToString("O") + " " + text);
            _store.Save(journal);
        }
        private string Describe(ProfileJournal journal) => "Original: " + journal.Original + "; copy: " + journal.Owned +
            "; managed changes: " + journal.Changes.Length + "; journal: " + _store.Path;
    }

    public static class ShortcutProfiles
    {
        internal static ProfileJournalStore Store => new ProfileJournalStore(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlenderSceneControls", "ShortcutProfiles"),
            System.IO.Path.GetDirectoryName(Application.dataPath));

        [MenuItem("Tools/Blender Scene Controls/Apply Shortcut Profile", false, 120)]
        public static void ApplyShortcutProfile() => Run(true);
        [MenuItem("Tools/Blender Scene Controls/Restore Previous Profile", false, 121)]
        public static void RestorePreviousProfile() => Run(false);

        [MenuItem("Tools/Blender Scene Controls/Upgrade Mesh Shortcut Profile", false, 122)]
        public static void UpgradeMeshShortcutProfile()
        {
            if (SceneControls.Active || SceneControls.NavigationActive || SceneControls.MeshEdit != null || SceneControls.TextIsEditing() ||
                EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.Log("BSC: return to idle Object Mode before upgrading the profile."); return; }
            try { Debug.Log("BSC: " + new ShortcutProfileService(ShortcutManager.instance, Store).UpgradeMesh()); }
            catch (Exception exception) { Debug.LogWarning("BSC profile upgrade stopped: " + exception.Message); }
        }

        private static void Run(bool apply)
        {
            if (SceneControls.Active || SceneControls.NavigationActive || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.Log("BSC: finish the current operation and return to idle Edit Mode before changing profiles."); return; }
            try
            {
                var service = new ShortcutProfileService(ShortcutManager.instance, Store);
                Debug.Log("BSC: " + (apply ? service.Apply() : service.Restore()));
            }
            catch (Exception exception) { Debug.LogWarning("BSC profile operation stopped: " + exception.Message); }
        }

        internal static bool Matches(string id, Event current)
        {
            if (current.type != EventType.KeyDown) return false;
            var modifiers = (current.shift ? ShortcutModifiers.Shift : ShortcutModifiers.None) |
                (current.alt ? ShortcutModifiers.Alt : ShortcutModifiers.None) |
                (current.control || current.command ? ShortcutModifiers.Action : ShortcutModifiers.None);
            return Matches(ShortcutManager.instance.GetShortcutBinding(id), current.keyCode, modifiers);
        }

        internal static string CommandHint(SceneCommand command)
        {
            if (!ShortcutProfileService.MeshCommandIds.TryGetValue(command, out var id)) return command.ToString();
            var binding = ShortcutManager.instance.GetShortcutBinding(id);
            return binding.keyCombinationSequence.Any() ? binding.ToString() : "Unbound";
        }

        internal static bool Matches(ShortcutBinding binding, KeyCode key, ShortcutModifiers modifiers)
        {
            var keys = binding.keyCombinationSequence.ToArray();
            return keys.Length == 1 && keys[0].keyCode == key && keys[0].modifiers == modifiers;
        }
    }
}
