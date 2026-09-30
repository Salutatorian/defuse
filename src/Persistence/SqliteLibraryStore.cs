using Defuse.Application;
using Defuse.Domain;
using Microsoft.Data.Sqlite;

namespace Defuse.Persistence;

public sealed class SqliteLibraryStore : ILibraryStore, IDisposable
{
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public SqliteLibraryStore(string databasePath, ISecretProtector protector)
    {
        _path = databasePath;
        _protector = protector;
    }

    public void Dispose()
    {
    }

    public Guid ActiveProfileId
    {
        get
        {
            var value = GetSetting("profile.active");
            return Guid.TryParse(value, out var id) ? id : LibraryProfile.Id;
        }
    }

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }

        var version = UserVersion(connection);
        if (version > 1)
            throw new InvalidOperationException("This library was created by a newer Defuse.");
        if (version == 1)
        {
            ScrubDisplayedSecrets(connection);
            return;
        }

        using var tx = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = tx;
            schema.CommandText = Schema;
            schema.ExecuteNonQuery();
        }

        using (var profile = connection.CreateCommand())
        {
            profile.Transaction = tx;
            profile.CommandText = "INSERT INTO profiles (id, name) VALUES ($id, $name);";
            profile.Parameters.AddWithValue("$id", LibraryProfile.Id.ToString("D"));
            profile.Parameters.AddWithValue("$name", LibraryProfile.Name);
            profile.ExecuteNonQuery();
        }

        using (var setting = connection.CreateCommand())
        {
            setting.Transaction = tx;
            setting.CommandText = "INSERT INTO settings (key, value) VALUES ('profile.active', $id);";
            setting.Parameters.AddWithValue("$id", LibraryProfile.Id.ToString("D"));
            setting.ExecuteNonQuery();
        }

        tx.Commit();
        using var stamp = connection.CreateCommand();
        stamp.CommandText = "PRAGMA user_version = 1;";
        stamp.ExecuteNonQuery();
        ScrubDisplayedSecrets(connection);
    }

    private static void ScrubDisplayedSecrets(SqliteConnection connection)
    {
        ScrubColumn(connection, "sources", "locator_redacted");
        ScrubColumn(connection, "subtitles", "locator_redacted");
        ScrubColumn(connection, "addons", "redacted_url");
        ScrubColumn(connection, "connections", "redacted_endpoint");
    }

    private static void ScrubColumn(SqliteConnection connection, string table, string column)
    {
        var updates = new List<(string Id, string Text)>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = $"SELECT id, {column} FROM {table};";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var current = reader.GetString(1);
                if (!Uri.TryCreate(current, UriKind.Absolute, out var uri))
                    continue;
                var redacted = UrlRedactor.Redact(uri);
                if (!string.Equals(redacted, current, StringComparison.Ordinal))
                    updates.Add((reader.GetString(0), redacted));
            }
        }

        if (updates.Count == 0)
            return;
        using var tx = connection.BeginTransaction();
        foreach (var (id, text) in updates)
        {
            using var write = connection.CreateCommand();
            write.Transaction = tx;
            write.CommandText = $"UPDATE {table} SET {column} = $text WHERE id = $id;";
            write.Parameters.AddWithValue("$text", text);
            write.Parameters.AddWithValue("$id", id);
            write.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public Guid? FindMediaIdByFingerprint(string fingerprint)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.media_id FROM sources s
            JOIN media_versions v ON v.id = s.version_id
            WHERE s.fingerprint = $fp
            """;
        command.Parameters.AddWithValue("$fp", fingerprint);
        return command.ExecuteScalar() is string text ? Guid.Parse(text) : null;
    }

    public IReadOnlyList<TitleMatch> FindTitleMatches(string normalizedTitle)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title FROM media_items WHERE normalized_title = $n ORDER BY created_at;";
        command.Parameters.AddWithValue("$n", normalizedTitle);
        using var reader = command.ExecuteReader();
        var list = new List<TitleMatch>();
        while (reader.Read())
            list.Add(new TitleMatch(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        return list;
    }

    public void CreateTitle(NewTitle title)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        EnsureFingerprintAvailable(connection, tx, title.Fingerprint, null);
        InsertMedia(connection, tx, title);
        InsertVersion(connection, tx, title);
        InsertSource(connection, tx, title, preferred: true);
        ReplaceSubtitle(connection, tx, title.VersionId, title);
        tx.Commit();
    }

    public void UpdateLocator(Guid sourceId, NewTitle replacement)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var versionId = Scalar(connection, tx, "SELECT version_id FROM sources WHERE id = $id;", ("$id", sourceId.ToString("D")))
            ?? throw new InvalidOperationException("That title is no longer in the library.");
        EnsureFingerprintAvailable(connection, tx, replacement.Fingerprint, sourceId);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            UPDATE sources
            SET kind = $kind, locator_protected = $locator, locator_redacted = $redacted, fingerprint = $fp,
                may_expire = $expire, is_loopback = $loop, stremio_hint = $stremio, dependency_note = $note
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$kind", replacement.Kind);
        command.Parameters.Add("$locator", SqliteType.Blob).Value = _protector.Protect(replacement.LocatorPlaintext);
        command.Parameters.AddWithValue("$redacted", replacement.LocatorRedacted);
        command.Parameters.AddWithValue("$fp", replacement.Fingerprint);
        command.Parameters.AddWithValue("$expire", replacement.MayExpire ? 1 : 0);
        command.Parameters.AddWithValue("$loop", replacement.IsLoopback ? 1 : 0);
        command.Parameters.AddWithValue("$stremio", replacement.SuggestsStremioServer ? 1 : 0);
        command.Parameters.AddWithValue("$note", (object?)replacement.UserMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", sourceId.ToString("D"));
        command.ExecuteNonQuery();
        ReplaceSubtitle(connection, tx, Guid.Parse(versionId), replacement);
        tx.Commit();
    }

    public void AddPreferredSource(Guid mediaId, NewTitle source)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var versionId = Scalar(connection, tx, "SELECT id FROM media_versions WHERE media_id = $id LIMIT 1;", ("$id", mediaId.ToString("D")))
            ?? throw new InvalidOperationException("That title is no longer in the library.");
        EnsureFingerprintAvailable(connection, tx, source.Fingerprint, null);
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "UPDATE sources SET preferred = 0 WHERE version_id = $version;";
            clear.Parameters.AddWithValue("$version", versionId);
            clear.ExecuteNonQuery();
        }

        InsertSource(connection, tx, source with { VersionId = Guid.Parse(versionId) }, preferred: true);
        ReplaceSubtitle(connection, tx, Guid.Parse(versionId), source);
        tx.Commit();
    }

    public PlayTarget? GetPlayTarget(Guid mediaId) => ReadTarget(mediaId, null);

    public PlayTarget? OpenSource(Guid mediaId, Guid sourceId) => ReadTarget(mediaId, sourceId);

    public IReadOnlyList<SourceChoice> ListSources(Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.kind, s.locator_redacted, s.preferred
            FROM sources s
            JOIN media_versions v ON v.id = s.version_id
            WHERE v.media_id = $id
            ORDER BY s.preferred DESC;
            """;
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<SourceChoice>();
        while (reader.Read())
            list.Add(new SourceChoice(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 1));
        return list;
    }

    public void UpsertProgress(PlaybackProgress progress)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO playback_state
                (profile_id, media_id, version_id, position_ms, duration_ms, completed, updated_at, last_source_id)
            VALUES ($profile, $media, $version, $position, $duration, $completed, $updated, $source)
            ON CONFLICT(profile_id, media_id) DO UPDATE SET
                version_id = excluded.version_id,
                position_ms = excluded.position_ms,
                duration_ms = excluded.duration_ms,
                completed = excluded.completed,
                updated_at = excluded.updated_at,
                last_source_id = excluded.last_source_id;
            """;
        BindProgress(command, progress);
        command.ExecuteNonQuery();
    }

    public PlaybackProgress? GetProgress(Guid profileId, Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT version_id, position_ms, duration_ms, completed, updated_at, last_source_id
            FROM playback_state WHERE profile_id = $profile AND media_id = $media;
            """;
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new PlaybackProgress(profileId, mediaId, Guid.Parse(reader.GetString(0)), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3) == 1, DateTimeOffset.Parse(reader.GetString(4)), Guid.Parse(reader.GetString(5)));
    }

    public void ClearProgress(Guid profileId, Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM playback_state WHERE profile_id = $profile AND media_id = $media;";
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public void MarkWatched(Guid profileId, Guid mediaId)
    {
        var existing = GetProgress(profileId, mediaId);
        var target = GetPlayTarget(mediaId) ?? throw new InvalidOperationException("That title is no longer in the library.");
        var duration = existing?.DurationMs ?? 0;
        var position = duration > 0 ? duration : Math.Max(existing?.PositionMs ?? 0, 1);
        UpsertProgress(new PlaybackProgress(profileId, mediaId, target.VersionId, position, Math.Max(duration, position), true, DateTimeOffset.UtcNow, target.SourceId));
    }

    public IReadOnlyList<ContinueItem> ListContinueWatching(Guid profileId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.id, m.title, p.position_ms, p.duration_ms, s.locator_redacted, a.cache_path
            FROM playback_state p
            JOIN media_items m ON m.id = p.media_id
            JOIN sources s ON s.id = p.last_source_id
            LEFT JOIN artwork a ON a.media_id = m.id
            WHERE p.profile_id = $profile AND p.completed = 0 AND p.position_ms >= 30000 AND p.duration_ms > 0
              AND p.position_ms < CAST(p.duration_ms * 0.95 AS INTEGER) AND COALESCE(s.seekable, 1) = 1
            ORDER BY p.updated_at DESC;
            """;
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<ContinueItem>();
        while (reader.Read())
            list.Add(new ContinueItem(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return list;
    }

    public IReadOnlyList<LibraryItem> ListLibrary() => QueryLibrary("1 = 1", null);

    public IReadOnlyList<LibraryItem> Search(string query)
    {
        var needle = $"%{TitleNormalizer.Normalize(query)}%";
        return QueryLibrary("m.normalized_title LIKE $q OR lower(ifnull(m.show_title, '')) LIKE $q", ("$q", needle));
    }

    public IReadOnlyList<LibraryItem> Episodes(string showTitle) =>
        QueryLibrary("m.show_title = $show", ("$show", showTitle));

    public IReadOnlyList<string> Shows()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT show_title FROM media_items WHERE show_title IS NOT NULL AND show_title <> '' ORDER BY show_title;";
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public void SetSeekable(Guid sourceId, bool seekable)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sources SET seekable = $seekable WHERE id = $id;";
        command.Parameters.AddWithValue("$seekable", seekable ? 1 : 0);
        command.Parameters.AddWithValue("$id", sourceId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public void SetPoster(Guid mediaId, string cachePath)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO artwork (media_id, role, cache_path, manual_override)
            VALUES ($id, 'poster', $path, 1)
            ON CONFLICT(media_id) DO UPDATE SET cache_path = excluded.cache_path, manual_override = 1;
            """;
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        command.Parameters.AddWithValue("$path", cachePath);
        command.ExecuteNonQuery();
    }

    public void RenameTitle(Guid mediaId, string title)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE media_items SET title = $title, normalized_title = $norm WHERE id = $id;";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$norm", TitleNormalizer.Normalize(title));
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> DeleteTitle(Guid mediaId)
    {
        using var connection = Open();
        var paths = new List<string>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT cache_path FROM artwork WHERE media_id = $id;";
            read.Parameters.AddWithValue("$id", mediaId.ToString("D"));
            using var reader = read.ExecuteReader();
            while (reader.Read())
                paths.Add(reader.GetString(0));
        }

        using var tx = connection.BeginTransaction();
        foreach (var sql in new[]
        {
            "DELETE FROM playback_state WHERE media_id = $id;",
            "DELETE FROM favorites WHERE media_id = $id;",
            "DELETE FROM collection_items WHERE media_id = $id;",
            "DELETE FROM playlist_items WHERE media_id = $id;",
            "DELETE FROM artwork WHERE media_id = $id;",
            "DELETE FROM intro_markers WHERE media_id = $id;",
            "DELETE FROM subtitles WHERE version_id IN (SELECT id FROM media_versions WHERE media_id = $id);",
            "DELETE FROM sources WHERE version_id IN (SELECT id FROM media_versions WHERE media_id = $id);",
            "DELETE FROM media_versions WHERE media_id = $id;",
            "DELETE FROM media_items WHERE id = $id;"
        })
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
            command.ExecuteNonQuery();
        }

        tx.Commit();
        return paths;
    }

    public bool GetClipboardWatch() => GetSetting("clipboard.watch") == "1";

    public void SetClipboardWatch(bool enabled) => SetSetting("clipboard.watch", enabled ? "1" : "0");

    public string? GetSetting(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string? value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        if (value is null)
        {
            command.CommandText = "DELETE FROM settings WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
        }
        else
        {
            command.CommandText = """
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
        }

        command.ExecuteNonQuery();
    }

    public void SetFavorite(Guid profileId, Guid mediaId, bool favorite)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = favorite
            ? "INSERT INTO favorites (profile_id, media_id) VALUES ($profile, $media) ON CONFLICT DO NOTHING;"
            : "DELETE FROM favorites WHERE profile_id = $profile AND media_id = $media;";
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CollectionCard> Collections(Guid profileId) =>
        Cards(profileId, "collections", "collection_items", "collection_id");

    public Guid CreateCollection(Guid profileId, string name) => CreateNamed(profileId, "collections", name);

    public void AddToCollection(Guid collectionId, Guid mediaId) => AddMembership("collection_items", "collection_id", collectionId, mediaId);

    public IReadOnlyList<PlaylistCard> Playlists(Guid profileId)
    {
        return Cards(profileId, "playlists", "playlist_items", "playlist_id")
            .Select(card => new PlaylistCard(card.Id, card.Name, card.Count)).ToArray();
    }

    public Guid CreatePlaylist(Guid profileId, string name) => CreateNamed(profileId, "playlists", name);

    public void AddToPlaylist(Guid playlistId, Guid mediaId) => AddMembership("playlist_items", "playlist_id", playlistId, mediaId);

    public IReadOnlyList<ProfileCard> Profiles()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, pin_protected IS NOT NULL FROM profiles ORDER BY name;";
        using var reader = command.ExecuteReader();
        var list = new List<ProfileCard>();
        while (reader.Read())
            list.Add(new ProfileCard(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2) == 1));
        return list;
    }

    public Guid CreateProfile(string name)
    {
        var id = Guid.NewGuid();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO profiles (id, name) VALUES ($id, $name);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$name", name.Trim());
        command.ExecuteNonQuery();
        return id;
    }

    public void SwitchProfile(Guid profileId) => SetSetting("profile.active", profileId.ToString("D"));

    public void SetPin(Guid profileId, byte[]? protectedPin)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE profiles SET pin_protected = $pin WHERE id = $id;";
        command.Parameters.Add("$pin", SqliteType.Blob).Value = protectedPin ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("$id", profileId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public byte[]? GetPin(Guid profileId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pin_protected FROM profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", profileId.ToString("D"));
        var value = command.ExecuteScalar();
        return value is byte[] bytes ? bytes : null;
    }

    public void SaveAddon(Guid id, string name, string manifestPlain, string redactedUrl, bool enabled)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO addons (id, name, manifest_protected, redacted_url, enabled)
            VALUES ($id, $name, $manifest, $redacted, $enabled)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, manifest_protected = excluded.manifest_protected,
                redacted_url = excluded.redacted_url, enabled = excluded.enabled;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.Add("$manifest", SqliteType.Blob).Value = _protector.Protect(manifestPlain);
        command.Parameters.AddWithValue("$redacted", redactedUrl);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AddonCard> Addons()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, redacted_url, enabled FROM addons ORDER BY name;";
        using var reader = command.ExecuteReader();
        var list = new List<AddonCard>();
        while (reader.Read())
            list.Add(new AddonCard(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 1));
        return list;
    }

    public string? AddonManifest(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT manifest_protected FROM addons WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return command.ExecuteScalar() is byte[] bytes ? _protector.Unprotect(bytes) : null;
    }

    public void SaveConnection(Guid id, string protocol, string label, string endpointPlain, string redacted, string? secretPlain)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO connections (id, protocol, label, endpoint_protected, redacted_endpoint, secret_protected)
            VALUES ($id, $protocol, $label, $endpoint, $redacted, $secret)
            ON CONFLICT(id) DO UPDATE SET protocol = excluded.protocol, label = excluded.label,
                endpoint_protected = excluded.endpoint_protected, redacted_endpoint = excluded.redacted_endpoint,
                secret_protected = excluded.secret_protected;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$protocol", protocol);
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.Add("$endpoint", SqliteType.Blob).Value = _protector.Protect(endpointPlain);
        command.Parameters.AddWithValue("$redacted", redacted);
        command.Parameters.Add("$secret", SqliteType.Blob).Value = secretPlain is null ? DBNull.Value : _protector.Protect(secretPlain);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ConnectionCard> Connections()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, protocol, label, redacted_endpoint FROM connections ORDER BY label;";
        using var reader = command.ExecuteReader();
        var list = new List<ConnectionCard>();
        while (reader.Read())
            list.Add(new ConnectionCard(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return list;
    }

    public (string Endpoint, string? Secret) OpenConnection(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT endpoint_protected, secret_protected FROM connections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("That connection is no longer saved.");
        var secret = reader.IsDBNull(1) ? null : _protector.Unprotect((byte[])reader.GetValue(1));
        return (_protector.Unprotect((byte[])reader.GetValue(0)), secret);
    }

    public void DeleteConnection(Guid id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM connections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    public void SetIntro(Guid mediaId, long endMs)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO intro_markers (media_id, end_ms) VALUES ($id, $end)
            ON CONFLICT(media_id) DO UPDATE SET end_ms = excluded.end_ms;
            """;
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        command.Parameters.AddWithValue("$end", endMs);
        command.ExecuteNonQuery();
    }

    public long? GetIntro(Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT end_ms FROM intro_markers WHERE media_id = $id;";
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public void EnqueueSync(Guid profileId, Guid? mediaId, string eventType, string redactedPayload)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sync_events (id, profile_id, media_id, event_type, payload, created_at, status)
            VALUES ($id, $profile, $media, $type, $payload, $created, 'pending');
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue("$payload", redactedPayload);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public int PendingSyncCount(Guid profileId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sync_events WHERE profile_id = $profile AND status = 'pending';";
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private PlayTarget? ReadTarget(Guid mediaId, Guid? sourceId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.title, v.id, s.id, s.locator_protected, s.locator_redacted, s.seekable,
                   s.is_loopback, s.may_expire, s.stremio_hint, s.dependency_note, sub.locator_protected,
                   p.version_id, p.position_ms, p.duration_ms, p.completed, p.updated_at, p.last_source_id,
                   m.show_title, m.season_number, m.episode_number
            FROM media_items m
            JOIN media_versions v ON v.media_id = m.id
            JOIN sources s ON s.version_id = v.id AND ($source IS NULL OR s.id = $source) AND ($source IS NOT NULL OR s.preferred = 1)
            LEFT JOIN subtitles sub ON sub.version_id = v.id
            LEFT JOIN playback_state p ON p.media_id = m.id AND p.profile_id = $profile
            WHERE m.id = $id;
            """;
        command.Parameters.AddWithValue("$profile", ActiveProfileId.ToString("D"));
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        command.Parameters.AddWithValue("$source", sourceId?.ToString("D") ?? (object)DBNull.Value);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        PlaybackProgress? progress = reader.IsDBNull(11) ? null : new PlaybackProgress(
            ActiveProfileId, mediaId, Guid.Parse(reader.GetString(11)), reader.GetInt64(12), reader.GetInt64(13),
            reader.GetInt64(14) == 1, DateTimeOffset.Parse(reader.GetString(15)), Guid.Parse(reader.GetString(16)));
        return new PlayTarget(
            mediaId, Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(0),
            _protector.Unprotect((byte[])reader.GetValue(3)),
            reader.IsDBNull(10) ? null : _protector.Unprotect((byte[])reader.GetValue(10)),
            reader.IsDBNull(5) ? null : reader.GetInt64(5) == 1,
            reader.GetInt64(6) == 1, reader.GetInt64(7) == 1, reader.GetInt64(8) == 1, reader.GetString(4),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : Convert.ToInt32(reader.GetValue(18)),
            reader.IsDBNull(19) ? null : Convert.ToInt32(reader.GetValue(19)),
            progress);
    }

    private IReadOnlyList<LibraryItem> QueryLibrary(string filter, (string Name, string Value)? parameter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.id, m.title, m.type, s.locator_redacted, a.cache_path, p.position_ms, p.completed,
                   m.show_title, m.season_number, m.episode_number,
                   EXISTS(SELECT 1 FROM favorites f WHERE f.media_id = m.id AND f.profile_id = $profile),
                   p.duration_ms
            FROM media_items m
            JOIN media_versions v ON v.media_id = m.id
            JOIN sources s ON s.version_id = v.id AND s.preferred = 1
            LEFT JOIN artwork a ON a.media_id = m.id
            LEFT JOIN playback_state p ON p.media_id = m.id AND p.profile_id = $profile
            WHERE {filter}
            ORDER BY m.created_at DESC;
            """;
        command.Parameters.AddWithValue("$profile", ActiveProfileId.ToString("D"));
        if (parameter is { } pair)
            command.Parameters.AddWithValue(pair.Name, pair.Value);
        using var reader = command.ExecuteReader();
        var list = new List<LibraryItem>();
        while (reader.Read())
        {
            list.Add(new LibraryItem(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                !reader.IsDBNull(6) && reader.GetInt64(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : Convert.ToInt32(reader.GetValue(8)),
                reader.IsDBNull(9) ? null : Convert.ToInt32(reader.GetValue(9)),
                reader.GetInt64(10) == 1,
                reader.IsDBNull(11) ? null : reader.GetInt64(11)));
        }

        return list;
    }

    private IReadOnlyList<CollectionCard> Cards(Guid profileId, string table, string items, string itemKey)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.id, c.name, (SELECT COUNT(*) FROM {items} i WHERE i.{itemKey} = c.id)
            FROM {table} c WHERE c.profile_id = $profile ORDER BY c.name;
            """;
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<CollectionCard>();
        while (reader.Read())
            list.Add(new CollectionCard(Guid.Parse(reader.GetString(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2))));
        return list;
    }

    private Guid CreateNamed(Guid profileId, string table, string name)
    {
        var id = Guid.NewGuid();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} (id, profile_id, name) VALUES ($id, $profile, $name);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$name", name.Trim());
        command.ExecuteNonQuery();
        return id;
    }

    private void AddMembership(string table, string key, Guid parentId, Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({key}, media_id, position) VALUES ($parent, $media, 0) ON CONFLICT DO NOTHING;";
        command.Parameters.AddWithValue("$parent", parentId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private void EnsureFingerprintAvailable(SqliteConnection connection, SqliteTransaction tx, string fingerprint, Guid? exceptSourceId)
    {
        var existing = Scalar(connection, tx, "SELECT id FROM sources WHERE fingerprint = $fp;", ("$fp", fingerprint));
        if (existing is not null && existing != exceptSourceId?.ToString("D"))
            throw new InvalidOperationException("That link is already saved on another title.");
    }

    private static string? Scalar(SqliteConnection connection, SqliteTransaction tx, string sql, (string Name, string Value) parameter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return command.ExecuteScalar() as string;
    }

    private void InsertMedia(SqliteConnection connection, SqliteTransaction tx, NewTitle title)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO media_items
                (id, type, title, normalized_title, year, show_title, season_number, episode_number, created_at)
            VALUES ($id, $type, $title, $norm, $year, $show, $season, $episode, $created);
            """;
        command.Parameters.AddWithValue("$id", title.MediaId.ToString("D"));
        command.Parameters.AddWithValue("$type", title.Type);
        command.Parameters.AddWithValue("$title", title.Title);
        command.Parameters.AddWithValue("$norm", title.NormalizedTitle);
        command.Parameters.AddWithValue("$year", (object?)title.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("$show", (object?)title.ShowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$season", (object?)title.SeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$episode", (object?)title.EpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertVersion(SqliteConnection connection, SqliteTransaction tx, NewTitle title)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "INSERT INTO media_versions (id, media_id) VALUES ($id, $media);";
        command.Parameters.AddWithValue("$id", title.VersionId.ToString("D"));
        command.Parameters.AddWithValue("$media", title.MediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private void InsertSource(SqliteConnection connection, SqliteTransaction tx, NewTitle title, bool preferred)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO sources
                (id, version_id, kind, locator_protected, locator_redacted, fingerprint, may_expire, is_loopback, stremio_hint, dependency_note, preferred)
            VALUES ($id, $version, $kind, $locator, $redacted, $fp, $expire, $loop, $stremio, $note, $preferred);
            """;
        command.Parameters.AddWithValue("$id", title.SourceId.ToString("D"));
        command.Parameters.AddWithValue("$version", title.VersionId.ToString("D"));
        command.Parameters.AddWithValue("$kind", title.Kind);
        command.Parameters.Add("$locator", SqliteType.Blob).Value = _protector.Protect(title.LocatorPlaintext);
        command.Parameters.AddWithValue("$redacted", title.LocatorRedacted);
        command.Parameters.AddWithValue("$fp", title.Fingerprint);
        command.Parameters.AddWithValue("$expire", title.MayExpire ? 1 : 0);
        command.Parameters.AddWithValue("$loop", title.IsLoopback ? 1 : 0);
        command.Parameters.AddWithValue("$stremio", title.SuggestsStremioServer ? 1 : 0);
        command.Parameters.AddWithValue("$note", (object?)title.UserMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$preferred", preferred ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private void ReplaceSubtitle(SqliteConnection connection, SqliteTransaction tx, Guid versionId, NewTitle title)
    {
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM subtitles WHERE version_id = $version;";
            delete.Parameters.AddWithValue("$version", versionId.ToString("D"));
            delete.ExecuteNonQuery();
        }

        if (title.SubtitlePlaintext is null)
            return;
        using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT INTO subtitles (id, version_id, locator_protected, locator_redacted) VALUES ($id, $version, $blob, $redacted);";
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("$version", versionId.ToString("D"));
        insert.Parameters.Add("$blob", SqliteType.Blob).Value = _protector.Protect(title.SubtitlePlaintext);
        insert.Parameters.AddWithValue("$redacted", title.SubtitleRedacted ?? "");
        insert.ExecuteNonQuery();
    }

    private static void BindProgress(SqliteCommand command, PlaybackProgress progress)
    {
        command.Parameters.AddWithValue("$profile", progress.ProfileId.ToString("D"));
        command.Parameters.AddWithValue("$media", progress.MediaId.ToString("D"));
        command.Parameters.AddWithValue("$version", progress.VersionId.ToString("D"));
        command.Parameters.AddWithValue("$position", progress.PositionMs);
        command.Parameters.AddWithValue("$duration", progress.DurationMs);
        command.Parameters.AddWithValue("$completed", progress.Completed ? 1 : 0);
        command.Parameters.AddWithValue("$updated", progress.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$source", progress.SourceId.ToString("D"));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static int UserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private const string Schema = """
        CREATE TABLE profiles (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          pin_protected BLOB NULL
        );
        CREATE TABLE media_items (
          id TEXT PRIMARY KEY,
          type TEXT NOT NULL,
          title TEXT NOT NULL,
          normalized_title TEXT NOT NULL,
          year INTEGER NULL,
          show_title TEXT NULL,
          season_number INTEGER NULL,
          episode_number INTEGER NULL,
          created_at TEXT NOT NULL
        );
        CREATE TABLE media_versions (
          id TEXT PRIMARY KEY,
          media_id TEXT NOT NULL REFERENCES media_items(id)
        );
        CREATE TABLE sources (
          id TEXT PRIMARY KEY,
          version_id TEXT NOT NULL REFERENCES media_versions(id),
          kind TEXT NOT NULL,
          locator_protected BLOB NOT NULL,
          locator_redacted TEXT NOT NULL,
          fingerprint TEXT NOT NULL,
          may_expire INTEGER NOT NULL,
          is_loopback INTEGER NOT NULL,
          stremio_hint INTEGER NOT NULL,
          seekable INTEGER NULL,
          dependency_note TEXT NULL,
          preferred INTEGER NOT NULL
        );
        CREATE UNIQUE INDEX ux_sources_fingerprint ON sources(fingerprint);
        CREATE TABLE subtitles (
          id TEXT PRIMARY KEY,
          version_id TEXT NOT NULL REFERENCES media_versions(id),
          locator_protected BLOB NOT NULL,
          locator_redacted TEXT NOT NULL
        );
        CREATE TABLE playback_state (
          profile_id TEXT NOT NULL,
          media_id TEXT NOT NULL,
          version_id TEXT NOT NULL,
          position_ms INTEGER NOT NULL,
          duration_ms INTEGER NOT NULL,
          completed INTEGER NOT NULL,
          updated_at TEXT NOT NULL,
          last_source_id TEXT NOT NULL,
          PRIMARY KEY (profile_id, media_id)
        );
        CREATE TABLE artwork (
          media_id TEXT PRIMARY KEY REFERENCES media_items(id),
          role TEXT NOT NULL,
          cache_path TEXT NOT NULL,
          manual_override INTEGER NOT NULL
        );
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE favorites (
          profile_id TEXT NOT NULL,
          media_id TEXT NOT NULL,
          PRIMARY KEY (profile_id, media_id)
        );
        CREATE TABLE collections (
          id TEXT PRIMARY KEY,
          profile_id TEXT NOT NULL,
          name TEXT NOT NULL
        );
        CREATE TABLE collection_items (
          collection_id TEXT NOT NULL,
          media_id TEXT NOT NULL,
          position INTEGER NOT NULL,
          PRIMARY KEY (collection_id, media_id)
        );
        CREATE TABLE playlists (
          id TEXT PRIMARY KEY,
          profile_id TEXT NOT NULL,
          name TEXT NOT NULL
        );
        CREATE TABLE playlist_items (
          playlist_id TEXT NOT NULL,
          media_id TEXT NOT NULL,
          position INTEGER NOT NULL,
          PRIMARY KEY (playlist_id, media_id)
        );
        CREATE TABLE addons (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          manifest_protected BLOB NOT NULL,
          redacted_url TEXT NOT NULL,
          enabled INTEGER NOT NULL
        );
        CREATE TABLE connections (
          id TEXT PRIMARY KEY,
          protocol TEXT NOT NULL,
          label TEXT NOT NULL,
          endpoint_protected BLOB NOT NULL,
          redacted_endpoint TEXT NOT NULL,
          secret_protected BLOB NULL
        );
        CREATE TABLE intro_markers (
          media_id TEXT PRIMARY KEY,
          end_ms INTEGER NOT NULL
        );
        CREATE TABLE sync_events (
          id TEXT PRIMARY KEY,
          profile_id TEXT NOT NULL,
          media_id TEXT NULL,
          event_type TEXT NOT NULL,
          payload TEXT NOT NULL,
          created_at TEXT NOT NULL,
          status TEXT NOT NULL
        );
        """;
}
